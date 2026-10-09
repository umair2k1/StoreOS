#!/usr/bin/env python3
"""StoreOS Local Development Runner.

Orchestrates running the full StoreOS stack locally:
- Docker backing services (PostgreSQL 16, MinIO S3, OpenTelemetry Collector)
- Database schema migrations & post-migrate permissions
- Demo tenant seeding (Apex Mobiles)
- Backend API (ASP.NET Core / .NET 10) on port 5000
- Frontend Web (Angular 21) on port 4200 with API proxying

Zero external dependencies — pure Python 3 standard library.
Works on Windows, macOS, Linux, and Android Termux (Debian).
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import signal
import socket
import subprocess
import sys
import threading
import time
import urllib.request
import webbrowser
from pathlib import Path
from typing import List, Optional, Tuple

# Enable ANSI escape sequences on Windows console
if sys.platform == "win32":
    try:
        os.system("")
    except Exception:
        pass


class Colors:
    """ANSI color codes with terminal detection."""
    _USE_COLOR = sys.stdout.isatty() and not os.environ.get("NO_COLOR")

    RESET = "\033[0m" if _USE_COLOR else ""
    BOLD = "\033[1m" if _USE_COLOR else ""
    DIM = "\033[2m" if _USE_COLOR else ""

    RED = "\033[31m" if _USE_COLOR else ""
    GREEN = "\033[32m" if _USE_COLOR else ""
    YELLOW = "\033[33m" if _USE_COLOR else ""
    BLUE = "\033[34m" if _USE_COLOR else ""
    MAGENTA = "\033[35m" if _USE_COLOR else ""
    CYAN = "\033[36m" if _USE_COLOR else ""
    WHITE = "\033[37m" if _USE_COLOR else ""


def log_sys(msg: str) -> None:
    print(f"{Colors.MAGENTA}[sys]{Colors.RESET} {msg}", flush=True)


def log_info(msg: str) -> None:
    print(f"{Colors.CYAN}[info]{Colors.RESET} {msg}", flush=True)


def log_success(msg: str) -> None:
    print(f"{Colors.GREEN}[ok]{Colors.RESET} {msg}", flush=True)


def log_warn(msg: str) -> None:
    print(f"{Colors.YELLOW}[warn]{Colors.RESET} {msg}", flush=True)


def log_error(msg: str) -> None:
    print(f"{Colors.RED}[error]{Colors.RESET} {msg}", file=sys.stderr, flush=True)


def get_repo_root() -> Path:
    """Resolve repository root directory."""
    candidate = Path(__file__).resolve().parent
    if (candidate / "backend").exists() and (candidate / "frontend").exists():
        return candidate
    if (candidate.parent / "backend").exists() and (candidate.parent / "frontend").exists():
        return candidate.parent
    cwd = Path.cwd()
    if (cwd / "backend").exists() and (cwd / "frontend").exists():
        return cwd
    return candidate.parent


def check_tool_exists(cmd: str) -> Optional[str]:
    """Check if tool exists in PATH and return its absolute path."""
    return shutil.which(cmd)


def check_port_open(port: int, host: str = "127.0.0.1", timeout: float = 0.5) -> bool:
    """Return True if port is actively listening."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(timeout)
        return s.connect_ex((host, port)) == 0


def wait_for_port(port: int, service_name: str, host: str = "127.0.0.1", timeout: float = 30.0) -> bool:
    """Wait for a port to become available."""
    start = time.time()
    while time.time() - start < timeout:
        if check_port_open(port, host):
            return True
        time.sleep(0.5)
    return False


def wait_for_http(url: str, timeout: float = 45.0) -> bool:
    """Wait for an HTTP endpoint to return HTTP 200 OK."""
    start = time.time()
    while time.time() - start < timeout:
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "StoreOS-Runner"})
            with urllib.request.urlopen(req, timeout=2.0) as resp:
                if 200 <= resp.status < 300:
                    return True
        except Exception:
            pass
        time.sleep(0.5)
    return False


def kill_process_tree(proc: subprocess.Popen) -> None:
    """Forcefully kill process and all its child processes."""
    if proc.poll() is not None:
        return
    try:
        if sys.platform == "win32":
            subprocess.run(
                ["taskkill", "/F", "/T", "/PID", str(proc.pid)],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            )
        else:
            os.killpg(os.getpgid(proc.pid), signal.SIGTERM)
    except Exception:
        try:
            proc.kill()
        except Exception:
            pass


# -----------------------------------------------------------------------------
# Prerequisite & Dependency Checks
# -----------------------------------------------------------------------------

def verify_prerequisites(repo_root: Path, check_docker: bool = True) -> bool:
    """Verify system prerequisites (.NET 10, Node.js, Docker)."""
    log_sys("Verifying development prerequisites...")
    all_ok = True

    # 1. Docker
    if check_docker:
        docker_bin = check_tool_exists("docker")
        if not docker_bin:
            log_error("Docker is not installed or not in PATH.")
            log_error("Please install Docker Desktop: https://www.docker.com/products/docker-desktop/")
            all_ok = False
        else:
            res = subprocess.run([docker_bin, "info"], capture_output=True, text=True)
            if res.returncode != 0:
                log_error("Docker daemon is not running.")
                log_error("Please start Docker Desktop or the docker service and retry.")
                all_ok = False
            else:
                log_success("Docker is running and healthy.")

    # 2. .NET SDK
    dotnet_bin = check_tool_exists("dotnet")
    if not dotnet_bin:
        log_error(".NET SDK is not installed or not in PATH.")
        log_error("StoreOS requires .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0")
        all_ok = False
    else:
        res = subprocess.run([dotnet_bin, "--version"], capture_output=True, text=True)
        version_str = res.stdout.strip()
        log_success(f".NET SDK found: v{version_str}")
        if not version_str.startswith("10."):
            log_warn(f"Warning: StoreOS is optimized for .NET 10.x (found {version_str}).")

    # 3. Node.js & npm
    node_bin = check_tool_exists("node")
    npm_bin = check_tool_exists("npm")
    if not node_bin or not npm_bin:
        log_error("Node.js and npm are required for the frontend.")
        log_error("Please install Node.js (>= 20, recommended 24): https://nodejs.org/")
        all_ok = False
    else:
        node_res = subprocess.run([node_bin, "--version"], capture_output=True, text=True)
        log_success(f"Node.js found: {node_res.stdout.strip()}")

    # 4. Dotnet local tools (dotnet-ef)
    tools_manifest = repo_root / ".config" / "dotnet-tools.json"
    if tools_manifest.exists() and dotnet_bin:
        # Restore dotnet-ef if needed
        subprocess.run(
            [dotnet_bin, "tool", "restore"],
            cwd=str(repo_root),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )

    # 5. Frontend node_modules check
    frontend_dir = repo_root / "frontend"
    node_modules_dir = frontend_dir / "node_modules"
    if frontend_dir.exists() and not node_modules_dir.exists():
        log_warn("Frontend dependencies (node_modules) not found. Running npm install...")
        npm_cmd = npm_bin or "npm"
        install_res = subprocess.run([npm_cmd, "install"], cwd=str(frontend_dir), check=False)
        if install_res.returncode != 0:
            log_error("npm install failed. Please inspect frontend/package.json.")
            all_ok = False
        else:
            log_success("Frontend dependencies installed.")

    return all_ok


# -----------------------------------------------------------------------------
# Backing Infrastructure (Docker Compose)
# -----------------------------------------------------------------------------

def start_infrastructure(repo_root: Path) -> bool:
    """Start Postgres, MinIO, and OpenTelemetry collector via Docker Compose."""
    compose_file = repo_root / "deploy" / "docker-compose.yml"
    if not compose_file.exists():
        log_error(f"Compose file not found at {compose_file}")
        return False

    docker_bin = check_tool_exists("docker") or "docker"
    log_sys("Starting backing services (PostgreSQL, MinIO, OTel Collector)...")
    cmd = [
        docker_bin,
        "compose",
        "-f",
        str(compose_file),
        "up",
        "-d",
        "--wait",
        "postgres",
        "minio",
        "otel-collector",
    ]

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    if res.returncode != 0:
        log_error(f"Failed to start Docker backing services:\n{res.stderr or res.stdout}")
        return False

    # Wait for PostgreSQL (5432) and MinIO (9000)
    if not wait_for_port(5432, "PostgreSQL", timeout=20.0):
        log_error("Timeout waiting for PostgreSQL port 5432.")
        return False
    if not wait_for_port(9000, "MinIO", timeout=20.0):
        log_error("Timeout waiting for MinIO port 9000.")
        return False

    log_success("Backing infrastructure is healthy (PostgreSQL: 5432, MinIO: 9000, OTel: 4317).")
    return True


def stop_infrastructure(repo_root: Path) -> bool:
    """Stop all StoreOS Docker containers."""
    compose_file = repo_root / "deploy" / "docker-compose.yml"
    docker_bin = check_tool_exists("docker") or "docker"
    log_sys("Stopping StoreOS Docker containers...")
    res = subprocess.run(
        [docker_bin, "compose", "-f", str(compose_file), "down"],
        cwd=str(repo_root),
        capture_output=True,
        text=True,
    )
    if res.returncode == 0:
        log_success("Docker containers stopped successfully.")
        return True
    else:
        log_error(f"Failed to stop containers:\n{res.stderr or res.stdout}")
        return False


def show_infrastructure_status(repo_root: Path) -> None:
    """Print status of StoreOS Docker services."""
    compose_file = repo_root / "deploy" / "docker-compose.yml"
    docker_bin = check_tool_exists("docker") or "docker"
    subprocess.run([docker_bin, "compose", "-f", str(compose_file), "ps"], cwd=str(repo_root))


# -----------------------------------------------------------------------------
# Database Migrations & Permissions
# -----------------------------------------------------------------------------

def run_migrations(repo_root: Path) -> bool:
    """Run EF Core database update and apply post-migrate SQL grants."""
    dotnet_bin = check_tool_exists("dotnet") or "dotnet"
    infra_proj = repo_root / "backend" / "src" / "StoreOS.Infrastructure"
    api_proj = repo_root / "backend" / "src" / "StoreOS.Api"

    log_sys("Applying EF Core database schema migrations...")
    conn_str = os.environ.get(
        "STOREOS_MIGRATIONS_CONNECTION",
        "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password",
    )

    cmd = [
        dotnet_bin,
        "ef",
        "database",
        "update",
        "--project",
        str(infra_proj),
        "--startup-project",
        str(api_proj),
        "--context",
        "StoreOsDbContext",
        "--connection",
        conn_str,
    ]

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    if res.returncode != 0:
        log_error(f"Database migration failed:\n{res.stderr or res.stdout}")
        return False

    log_success("EF Core migrations applied.")

    # Apply post-migrate grants (deploy/db/post-migrate.sql)
    log_sys("Applying RLS grants and permissions (post-migrate.sql)...")
    post_migrate_sql = repo_root / "deploy" / "db" / "post-migrate.sql"
    if not post_migrate_sql.exists():
        log_warn("deploy/db/post-migrate.sql not found, skipping grants.")
        return True

    psql_bin = check_tool_exists("psql")
    if psql_bin:
        uri = os.environ.get(
            "STOREOS_MIGRATIONS_CONNECTION_URI",
            "postgresql://storeos:storeos_dev_password@localhost:5432/storeos",
        )
        grant_res = subprocess.run(
            [psql_bin, uri, "-v", "ON_ERROR_STOP=1", "-f", str(post_migrate_sql)],
            capture_output=True,
            text=True,
        )
        if grant_res.returncode == 0:
            log_success("Post-migrate grants applied via local psql.")
            return True

    # Fallback to docker exec inside storeos-postgres
    try:
        docker_bin = check_tool_exists("docker") or "docker"
        sql_bytes = post_migrate_sql.read_bytes()
        grant_res = subprocess.run(
            [docker_bin, "exec", "-i", "storeos-postgres", "psql", "-U", "storeos", "-d", "storeos"],
            input=sql_bytes,
            capture_output=True,
            check=False,
        )
        if grant_res.returncode == 0:
            log_success("Post-migrate grants applied via Docker PostgreSQL.")
            return True
        else:
            log_error(f"Post-migrate grants failed:\n{grant_res.stderr.decode('utf-8', 'ignore')}")
            return False
    except Exception as ex:
        log_error(f"Failed to execute post-migrate SQL in Docker: {ex}")
        return False


# -----------------------------------------------------------------------------
# Demo Tenant Seeding
# -----------------------------------------------------------------------------

def seed_demo_tenant(repo_root: Path, force: bool = False) -> Optional[dict]:
    """Seed or verify demo tenant (Apex Mobiles Demo)."""
    dotnet_bin = check_tool_exists("dotnet") or "dotnet"
    seed_proj = repo_root / "tools" / "seed"
    if not seed_proj.exists():
        return None

    log_sys("Verifying demo tenant seed data...")
    cmd = [
        dotnet_bin,
        "run",
        "--project",
        str(seed_proj),
        "--",
        "--tenant-name",
        "Apex Mobiles Demo",
        "--subdomain",
        "apex",
    ]
    if force:
        cmd.append("--force")

    res = subprocess.run(cmd, cwd=str(repo_root), capture_output=True, text=True)
    tenant_info = {"subdomain": "apex", "name": "Apex Mobiles Demo"}

    # Extract Tenant ID from stdout
    match = re.search(r"Tenant ID:\s*([a-f0-9\-]+)", res.stdout, re.IGNORECASE)
    if match:
        tenant_info["id"] = match.group(1).strip()
    else:
        tenant_info["id"] = "890082a5-ec33-4d3c-a57a-6e4beeb2b641"

    if "already exists" in res.stdout:
        log_success(f"Demo tenant active: {tenant_info['name']} (ID: {tenant_info['id']})")
    elif res.returncode == 0:
        log_success(f"Demo tenant seeded: {tenant_info['name']} (ID: {tenant_info['id']})")
    else:
        log_warn(f"Seed command output: {res.stderr or res.stdout}")

    return tenant_info


# -----------------------------------------------------------------------------
# Output Banner
# -----------------------------------------------------------------------------

def print_banner(api_port: int, web_port: int, tenant_info: Optional[dict] = None) -> None:
    """Print welcome banner with quick links and demo credentials."""
    t_id = tenant_info.get("id", "890082a5-ec33-4d3c-a57a-6e4beeb2b641") if tenant_info else "890082a5-ec33-4d3c-a57a-6e4beeb2b641"

    print()
    print(f"{Colors.BOLD}{Colors.CYAN}========================================================================{Colors.RESET}")
    print(f"{Colors.BOLD}{Colors.CYAN}                   StoreOS — Local Development Stack                   {Colors.RESET}")
    print(f"{Colors.BOLD}{Colors.CYAN}========================================================================{Colors.RESET}")
    print(f" {Colors.GREEN}●{Colors.RESET} {Colors.BOLD}Frontend Web App:{Colors.RESET}   http://localhost:{web_port}")
    print(f"   {Colors.DIM}(Subdomain mode):{Colors.RESET}   http://apex.localhost:{web_port}")
    print(f" {Colors.GREEN}●{Colors.RESET} {Colors.BOLD}Backend API:{Colors.RESET}        http://localhost:{api_port}")
    print(f" {Colors.GREEN}●{Colors.RESET} {Colors.BOLD}Swagger UI:{Colors.RESET}         http://localhost:{api_port}/swagger")
    print(f" {Colors.GREEN}●{Colors.RESET} {Colors.BOLD}Health Live/Ready:{Colors.RESET}  http://localhost:{api_port}/health/ready")
    print(f" {Colors.GREEN}●{Colors.RESET} {Colors.BOLD}MinIO S3 Console:{Colors.RESET}   http://localhost:9001  {Colors.DIM}(user: storeos_minio / pass: storeos_minio_password){Colors.RESET}")
    print(f"------------------------------------------------------------------------")
    print(f" {Colors.BOLD}Demo Tenant:{Colors.RESET}        Apex Mobiles Demo  {Colors.DIM}(subdomain: apex){Colors.RESET}")
    print(f" {Colors.BOLD}Tenant ID:{Colors.RESET}          {Colors.YELLOW}{t_id}{Colors.RESET}")
    print(f"------------------------------------------------------------------------")
    print(f" {Colors.BOLD}Demo Login Accounts{Colors.RESET} (Password for all: {Colors.YELLOW}Demo@12345{Colors.RESET}):")
    print(f"   • Owner:          {Colors.CYAN}owner@apex.example{Colors.RESET}       (All Branches)")
    print(f"   • Cashier:        {Colors.CYAN}cashier@apex.example{Colors.RESET}     (Branch B01)")
    print(f"   • Branch Manager: {Colors.CYAN}manager@apex.example{Colors.RESET}     (Branch B01)")
    print(f"   • Technician:     {Colors.CYAN}technician@apex.example{Colors.RESET}  (Branch B01)")
    print(f"------------------------------------------------------------------------")
    print(f" {Colors.DIM}Press Ctrl+C to stop all local processes.{Colors.RESET}")
    print(f"{Colors.BOLD}{Colors.CYAN}========================================================================{Colors.RESET}")
    print()


# -----------------------------------------------------------------------------
# Process Supervisor & Streamer
# -----------------------------------------------------------------------------

def stream_reader(proc: subprocess.Popen, tag: str, color: str, stop_event: threading.Event) -> None:
    """Stream lines from subprocess stdout/stderr with formatted prefix."""
    prefix = f"{color}[{tag}]{Colors.RESET} "
    try:
        assert proc.stdout is not None
        for line in iter(proc.stdout.readline, ""):
            if stop_event.is_set():
                break
            if not line:
                break
            # Skip empty or whitespace-only lines
            cleaned = line.rstrip("\r\n")
            if cleaned:
                print(f"{prefix}{cleaned}", flush=True)
    except Exception:
        pass


def run_stack(
    repo_root: Path,
    api_port: int = 5000,
    web_port: int = 4200,
    api_only: bool = False,
    web_only: bool = False,
    use_spa: bool = False,
    use_watch: bool = False,
    auto_open: bool = False,
    tenant_info: Optional[dict] = None,
) -> int:
    """Run backend API and frontend Web processes concurrently with live logging."""
    dotnet_bin = check_tool_exists("dotnet") or "dotnet"
    node_bin = check_tool_exists("node") or "node"
    npm_bin = check_tool_exists("npm") or "npm"

    active_procs: List[subprocess.Popen] = []
    threads: List[threading.Thread] = []
    stop_event = threading.Event()

    def handle_shutdown(signum=None, frame=None):
        if stop_event.is_set():
            return
        stop_event.set()
        log_sys("Shutting down development servers...")
        for p in active_procs:
            kill_process_tree(p)
        log_success("All local processes stopped cleanly.")

    # Register signals
    signal.signal(signal.SIGINT, handle_shutdown)
    signal.signal(signal.SIGTERM, handle_shutdown)

    # 1. Start Backend API
    if not web_only:
        if check_port_open(api_port):
            log_warn(f"Port {api_port} is already in use. Ensure no other StoreOS API instance is running.")

        api_proj = repo_root / "backend" / "src" / "StoreOS.Api"
        api_env = os.environ.copy()
        api_env["ASPNETCORE_ENVIRONMENT"] = "Development"
        api_env["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1"

        if use_watch:
            api_cmd = [
                dotnet_bin,
                "watch",
                "--project",
                str(api_proj),
                "run",
                "--urls",
                f"http://localhost:{api_port}",
            ]
            log_sys(f"Starting API with hot reload (dotnet watch) on port {api_port}...")
        else:
            api_cmd = [
                dotnet_bin,
                "run",
                "--project",
                str(api_proj),
                "--urls",
                f"http://localhost:{api_port}",
            ]
            log_sys(f"Starting API on port {api_port}...")

        api_proc = subprocess.Popen(
            api_cmd,
            cwd=str(repo_root),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1,
            env=api_env,
        )
        active_procs.append(api_proc)

        t_api = threading.Thread(
            target=stream_reader,
            args=(api_proc, "api", Colors.CYAN, stop_event),
            daemon=True,
        )
        t_api.start()
        threads.append(t_api)

        # Wait for API ready
        log_sys(f"Waiting for API to become ready at http://localhost:{api_port}/health/ready...")
        if not wait_for_http(f"http://localhost:{api_port}/health/ready", timeout=50.0):
            log_warn("API health check timed out. Backend may still be compiling or starting...")

    # 2. Start Frontend Web
    if not api_only:
        if check_port_open(web_port):
            log_warn(f"Port {web_port} is already in use. Ensure no other Web dev server is running.")

        frontend_dir = repo_root / "frontend"

        if use_spa:
            # SPA mode uses tools/serve-spa.js
            dist_index = frontend_dir / "dist" / "storeos-web" / "browser" / "index.html"
            if not dist_index.exists():
                log_sys("Prebuilt SPA files not found. Building frontend production bundle...")
                npm_cmd = npm_bin or "npm"
                subprocess.run([npm_cmd, "run", "build"], cwd=str(frontend_dir), check=False)

            spa_script = repo_root / "tools" / "serve-spa.js"
            spa_env = os.environ.copy()
            spa_env["PORT"] = str(web_port)
            spa_env["API_PORT"] = str(api_port)

            web_cmd = [node_bin, str(spa_script)]
            log_sys(f"Starting lightweight SPA server on port {web_port} (proxies API to {api_port})...")
            web_proc = subprocess.Popen(
                web_cmd,
                cwd=str(repo_root),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                bufsize=1,
                env=spa_env,
            )
        else:
            # Angular dev server via npm start -- --proxy-config proxy.conf.json --port {web_port}
            npm_cmd = npm_bin or "npm"
            web_cmd = [
                npm_cmd,
                "start",
                "--prefix",
                str(frontend_dir),
                "--",
                "--proxy-config",
                "proxy.conf.json",
                "--port",
                str(web_port),
            ]
            log_sys(f"Starting Angular Dev Server on port {web_port}...")
            web_proc = subprocess.Popen(
                web_cmd,
                cwd=str(frontend_dir),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                bufsize=1,
            )

        active_procs.append(web_proc)
        t_web = threading.Thread(
            target=stream_reader,
            args=(web_proc, "web", Colors.GREEN, stop_event),
            daemon=True,
        )
        t_web.start()
        threads.append(t_web)

    # Print summary banner
    print_banner(api_port, web_port, tenant_info)

    if auto_open and not api_only:
        def open_browser():
            time.sleep(2.0)
            webbrowser.open(f"http://localhost:{web_port}")
        threading.Thread(target=open_browser, daemon=True).start()

    # Supervision loop: monitor running processes
    try:
        while not stop_event.is_set():
            for p in active_procs:
                ret = p.poll()
                if ret is not None and not stop_event.is_set():
                    log_warn(f"Process PID {p.pid} exited with code {ret}.")
                    stop_event.set()
                    break
            time.sleep(0.5)
    except KeyboardInterrupt:
        handle_shutdown()
    finally:
        handle_shutdown()

    return 0


# -----------------------------------------------------------------------------
# Main Entry Point
# -----------------------------------------------------------------------------

def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog="run.py",
        description="StoreOS Local Development Runner — Starts Database, API, and Frontend stack.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""\
examples:
  python run.py                   Start full stack (DB, Migrations, Seed, API, Web)
  python run.py --spa             Start with fast prebuilt SPA frontend server
  python run.py --watch           Start backend with hot reload (dotnet watch)
  python run.py --api-only        Start only DB and Backend API
  python run.py --web-only        Start only Angular frontend
  python run.py --infra-only      Start only Docker backing services (PostgreSQL, MinIO)
  python run.py --migrate         Apply database migrations and post-migrate SQL only
  python run.py --seed            Run demo tenant seeding only
  python run.py --down            Stop all StoreOS Docker containers
  python run.py --status          Check container and service status
""",
    )

    parser.add_argument("--api-only", action="store_true", help="Run only the backend API and database")
    parser.add_argument("--web-only", action="store_true", help="Run only the frontend development server")
    parser.add_argument("--infra-only", action="store_true", help="Start only Docker backing services and exit")
    parser.add_argument("--migrate", action="store_true", help="Apply database migrations and grants, then exit")
    parser.add_argument("--seed", action="store_true", help="Seed demo tenant data, then exit")
    parser.add_argument("--force-seed", action="store_true", help="Force wipe and re-seed demo tenant")
    parser.add_argument("--watch", action="store_true", help="Run backend API with dotnet watch (hot reload)")
    parser.add_argument("--spa", action="store_true", help="Serve prebuilt frontend via tools/serve-spa.js (faster)")
    parser.add_argument("--full-docker", action="store_true", help="Run entire stack in Docker (profile: full)")
    parser.add_argument("--down", action="store_true", help="Stop StoreOS Docker containers and exit")
    parser.add_argument("--status", action="store_true", help="Check status of backing services and exit")
    parser.add_argument("--no-migrate", action="store_true", help="Skip database migrations check on startup")
    parser.add_argument("--no-seed", action="store_true", help="Skip demo tenant seed check on startup")
    parser.add_argument("--open", action="store_true", help="Automatically open browser once ready")
    parser.add_argument("--port-api", type=int, default=5000, help="Port for Backend API (default: 5000)")
    parser.add_argument("--port-web", type=int, default=4200, help="Port for Frontend Web (default: 4200)")

    args = parser.parse_args(argv)
    repo_root = get_repo_root()

    # 1. Action: Down
    if args.down:
        return 0 if stop_infrastructure(repo_root) else 1

    # 2. Action: Status
    if args.status:
        show_infrastructure_status(repo_root)
        print(f"Port {args.port_api} (API):  {'LISTENING' if check_port_open(args.port_api) else 'FREE'}")
        print(f"Port {args.port_web} (Web):  {'LISTENING' if check_port_open(args.port_web) else 'FREE'}")
        print(f"Port 5432 (Postgres): {'LISTENING' if check_port_open(5432) else 'DOWN'}")
        print(f"Port 9000 (MinIO):    {'LISTENING' if check_port_open(9000) else 'DOWN'}")
        return 0

    # 3. Action: Full Docker Profile
    if args.full_docker:
        compose_file = repo_root / "deploy" / "docker-compose.yml"
        docker_bin = check_tool_exists("docker") or "docker"
        log_sys("Starting StoreOS full stack in Docker (profile: full)...")
        res = subprocess.run(
            [docker_bin, "compose", "-f", str(compose_file), "--profile", "full", "up", "-d"],
            cwd=str(repo_root),
        )
        if res.returncode == 0:
            log_success("Full Docker stack running! API on http://localhost:5000, Web on http://localhost:8081")
        return res.returncode

    # 4. Verify system prerequisites
    check_docker = not args.web_only
    if not verify_prerequisites(repo_root, check_docker=check_docker):
        return 1

    # 5. Start Infrastructure (PostgreSQL, MinIO, OTel)
    if not args.web_only:
        if not start_infrastructure(repo_root):
            return 1

    if args.infra_only:
        log_success("Infrastructure is running. Exiting as requested by --infra-only.")
        return 0

    # 6. Database Migrations & Grants
    if not args.web_only and not args.no_migrate:
        if not run_migrations(repo_root):
            return 1

    if args.migrate:
        log_success("Migrations applied. Exiting as requested by --migrate.")
        return 0

    # 7. Seed Demo Tenant
    tenant_info = None
    if not args.web_only and (not args.no_seed or args.force_seed or args.seed):
        tenant_info = seed_demo_tenant(repo_root, force=args.force_seed)

    if args.seed or args.force_seed:
        log_success("Seeding completed. Exiting as requested by --seed / --force-seed.")
        return 0

    # 8. Run stack (API + Web)
    return run_stack(
        repo_root=repo_root,
        api_port=args.port_api,
        web_port=args.port_web,
        api_only=args.api_only,
        web_only=args.web_only,
        use_spa=args.spa,
        use_watch=args.watch,
        auto_open=args.open,
        tenant_info=tenant_info,
    )


if __name__ == "__main__":
    sys.exit(main())
