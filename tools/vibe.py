#!/usr/bin/env python3
"""StoreOS Vibe-Coding Orchestrator & Strict Gatekeeper.

Enforces strict sequential execution, state validation, and artifact tracking
across development devices (Windows workstation, Android Termux/Debian, etc.).
Zero external dependencies (pure Python 3 standard library).
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple


def get_repo_root() -> Path:
    """Resolve the repository root directory."""
    script_dir = Path(__file__).resolve().parent
    # Check parent directory (standard: tools/ -> repo root)
    if (script_dir.parent / "docs" / "prompts" / "INDEX.md").exists():
        return script_dir.parent
    if (script_dir.parent / "prompts" / "INDEX.md").exists():
        return script_dir.parent
    # Fallback to current working directory
    cwd = Path.cwd()
    if (cwd / "docs" / "prompts" / "INDEX.md").exists():
        return cwd
    if (cwd / "prompts" / "INDEX.md").exists():
        return cwd
    return script_dir.parent


ROOT = get_repo_root()
PROMPTS_DIR = (
    ROOT / "docs" / "prompts"
    if (ROOT / "docs" / "prompts").exists()
    else ROOT / "prompts"
)
INDEX_PATH = PROMPTS_DIR / "INDEX.md"
BUILD_LOG_PATH = ROOT / "docs" / "BUILD_LOG.md"
ARTIFACTS_PATH = ROOT / "docs" / "ARTIFACTS.md"
VERIFY_SCRIPT_PATH = (
    ROOT / "docs" / "tools" / "verify_prompts.py"
    if (ROOT / "docs" / "tools" / "verify_prompts.py").exists()
    else ROOT / "tools" / "verify_prompts.py"
)


def load_index() -> List[Dict[str, Any]]:
    """Parse INDEX.md into ordered prompt records."""
    if not INDEX_PATH.exists():
        sys.stderr.write(f"Error: {INDEX_PATH} not found.\n")
        sys.exit(1)

    text = INDEX_PATH.read_text(encoding="utf-8")
    prompts = []
    # Pattern: | 1 | P-001 | `P-001-repo-bootstrap.md` | repo bootstrap |
    pattern = re.compile(
        r"^\|\s*(\d+)\s*\|\s*(P-\d+)\s*\|\s*`([^`]+)`\s*\|\s*(.*?)\s*\|$", re.M
    )
    for match in pattern.finditer(text):
        seq = int(match.group(1))
        pid = match.group(2)
        filename = match.group(3)
        title = match.group(4).strip()
        file_path = PROMPTS_DIR / filename
        prompts.append(
            {
                "seq": seq,
                "id": pid,
                "file": filename,
                "title": title,
                "path": file_path,
            }
        )
    return prompts


def load_prompt_meta(prompt_path: Path) -> Dict[str, str]:
    """Extract metadata (Phase, Reasoning level) from a prompt markdown file."""
    if not prompt_path.exists():
        return {"phase": "unknown", "reasoning": "unknown"}
    text = prompt_path.read_text(encoding="utf-8")
    m = re.search(
        r"\*\*Phase:\*\*\s*(\S+)\s*&nbsp;\|&nbsp;\s*\*\*Minimum executor reasoning level:\*\*\s*(\w+)",
        text,
    )
    if m:
        return {"phase": m.group(1), "reasoning": m.group(2)}
    return {"phase": "unknown", "reasoning": "unknown"}


def parse_build_log() -> Tuple[List[str], Optional[str]]:
    """Parse docs/BUILD_LOG.md.

    Returns:
        (completed_prompt_ids, blocked_reason)
    """
    if not BUILD_LOG_PATH.exists():
        return [], None

    content = BUILD_LOG_PATH.read_text(encoding="utf-8")
    parts = content.split("---", 1)
    if len(parts) < 2:
        return [], None

    body = parts[1]
    completed: List[str] = []
    blocked_reason: Optional[str] = None

    for line in body.splitlines():
        line = line.strip()
        if not line:
            continue
        if line.startswith("BLOCKED:"):
            blocked_reason = line
            continue

        # Match <PID> | DONE/COMPLETED | ...
        m = re.match(r"^(P-\d+)\s*\|\s*(DONE|COMPLETED|DECIDED)\s*\|", line)
        if m and m.group(2) in ("DONE", "COMPLETED"):
            completed.append(m.group(1))

    return completed, blocked_reason


def get_git_status() -> Dict[str, Any]:
    """Retrieve git branch, dirtiness, and unpushed commits."""
    try:
        branch = subprocess.check_output(
            ["git", "rev-parse", "--abbrev-ref", "HEAD"],
            cwd=ROOT,
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
    except Exception:
        branch = "unknown"

    try:
        # Check porcelain status
        porcelain = subprocess.check_output(
            ["git", "status", "--porcelain"],
            cwd=ROOT,
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
        is_clean = len(porcelain) == 0
    except Exception:
        porcelain = ""
        is_clean = True

    try:
        last_commit = subprocess.check_output(
            ["git", "log", "-1", "--oneline"],
            cwd=ROOT,
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
    except Exception:
        last_commit = "(no commits)"

    try:
        unpushed = subprocess.check_output(
            ["git", "log", "@{u}..HEAD", "--oneline"],
            cwd=ROOT,
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
        unpushed_count = len(unpushed.splitlines()) if unpushed else 0
    except Exception:
        unpushed_count = 0

    return {
        "branch": branch,
        "is_clean": is_clean,
        "porcelain": porcelain,
        "last_commit": last_commit,
        "unpushed_count": unpushed_count,
    }


def get_state() -> Dict[str, Any]:
    """Calculate overall orchestrator state."""
    index = load_index()
    completed, blocked = parse_build_log()
    git = get_git_status()

    completed_set = set(completed)
    next_prompt = None
    for p in index:
        if p["id"] not in completed_set:
            next_prompt = p
            break

    return {
        "index": index,
        "completed": completed,
        "completed_set": completed_set,
        "blocked": blocked,
        "next_prompt": next_prompt,
        "git": git,
        "total": len(index),
    }


def cmd_status() -> int:
    """Print ASCII progress dashboard and current gate status."""
    st = get_state()
    total = st["total"]
    done_count = len(st["completed"])
    pct = (done_count / total * 100) if total else 0

    # Build ASCII progress bar (25 blocks wide)
    bar_width = 25
    filled = int(round(bar_width * done_count / total)) if total else 0
    bar = "=" * filled + "-" * (bar_width - filled)

    print("=================================================================")
    print("                StoreOS Vibe-Coding Orchestrator                ")
    print("=================================================================")
    print(f"  Progress:     [{bar}] {done_count}/{total} prompts ({pct:.1f}%)")
    print(f"  Git Branch:   {st['git']['branch']} (clean: {st['git']['is_clean']})")
    print(f"  Last Commit:  {st['git']['last_commit']}")
    if st["git"]["unpushed_count"] > 0:
        print(f"  Sync Alert:   {st['git']['unpushed_count']} unpushed commit(s) -> run 'git push' before switching devices!")

    print("-----------------------------------------------------------------")
    if st["blocked"]:
        print(f"  GATE STATUS:  [BLOCKED]\n    -> {st['blocked']}")
    elif not st["git"]["is_clean"]:
        print(f"  GATE STATUS:  [DIRTY WORKING TREE]\n    -> Commit or stash changes before proceeding.")
    elif st["next_prompt"] is None:
        print("  GATE STATUS:  [ALL 157 PROMPTS COMPLETED! BUILD GREEN!]")
    else:
        np = st["next_prompt"]
        meta = load_prompt_meta(np["path"])
        print("  GATE STATUS:  [READY FOR NEXT PROMPT]")
        print(f"  Next Prompt:  {np['id']} (Step #{np['seq']}/{total})")
        print(f"  Title:        {np['title']}")
        print(f"  Phase:        {meta['phase']} | Reasoning: {meta['reasoning']}")
        print(f"  File:         {np['file']}")

    if st["completed"]:
        print("-----------------------------------------------------------------")
        last_id = st["completed"][-1]
        print(f"  Last Done:    {last_id}")

    print("=================================================================")
    return 0


def cmd_gate(prompt_id: Optional[str]) -> int:
    """Strict Gatekeeper: Validate that the requested prompt is authorized to execute.

    Exits with code 0 if approved, code 1 if rejected.
    """
    st = get_state()

    # 1. Check for active BLOCK in build log
    if st["blocked"]:
        sys.stderr.write(
            f"\n[GATEKEEPER REJECTED] Build is in a BLOCKED state in docs/BUILD_LOG.md:\n"
            f"  {st['blocked']}\n"
            f"You cannot proceed until the block is resolved.\n"
            f"To clear the block after fixing, run: python3 tools/vibe.py unblock\n\n"
        )
        return 1

    # 2. Check for all prompts completed
    if st["next_prompt"] is None:
        print("\n[GATEKEEPER] All 157 prompts in INDEX.md have been completed!\n")
        return 0

    next_p = st["next_prompt"]

    # 3. If a specific prompt_id was requested, strictly validate sequence
    if prompt_id:
        req_id = prompt_id.upper().strip()
        # Validate existence
        matching = [p for p in st["index"] if p["id"] == req_id]
        if not matching:
            sys.stderr.write(
                f"\n[GATEKEEPER REJECTED] Unknown prompt ID '{prompt_id}'.\n"
                f"Valid prompts are listed in docs/prompts/INDEX.md (P-001 through P-1206).\n\n"
            )
            return 1

        if req_id != next_p["id"]:
            sys.stderr.write(
                f"\n[GATEKEEPER REJECTED] OUT-OF-ORDER PROMPT REQUESTED!\n"
                f"  Requested:       {req_id}\n"
                f"  Expected Next:   {next_p['id']} (Step #{next_p['seq']}: {next_p['title']})\n\n"
                f"StoreOS prompt pack MUST be executed strictly in INDEX.md order.\n"
                f"Skipping, reordering, or freelancing prompts is strictly forbidden.\n\n"
            )
            return 1

    # 4. Check git cleanliness: cannot start next prompt with unstaged / uncommitted work
    if not st["git"]["is_clean"]:
        sys.stderr.write(
            f"\n[GATEKEEPER REJECTED] Working tree has uncommitted changes.\n"
            f"Uncommitted files:\n"
        )
        for line in st["git"]["porcelain"].splitlines()[:10]:
            sys.stderr.write(f"  {line}\n")
        sys.stderr.write(
            f"\nPlease commit or clean your working tree before proceeding to {next_p['id']}.\n\n"
        )
        return 1

    # Approved!
    meta = load_prompt_meta(next_p["path"])
    print(
        f"[GATEKEEPER APPROVED] Prompt {next_p['id']} (Step #{next_p['seq']}: {next_p['title']}) is authorized."
    )
    print(f"  Phase: {meta['phase']} | Minimum Reasoning Level: {meta['reasoning']}")
    print(f"  Path:  {next_p['path'].relative_to(ROOT)}")
    return 0


def cmd_next(print_content: bool = False) -> int:
    """Retrieve and display the next authorized prompt."""
    st = get_state()
    # Check gate first
    ret = cmd_gate(None)
    if ret != 0:
        return ret

    next_p = st["next_prompt"]
    if next_p is None:
        return 0

    if print_content:
        content = next_p["path"].read_text(encoding="utf-8")
        print("\n" + content)
        return 0

    meta = load_prompt_meta(next_p["path"])
    print("-----------------------------------------------------------------")
    print(f"READY TO EXECUTE: {next_p['id']} - {next_p['title']}")
    print(f"  File:      {next_p['path'].relative_to(ROOT)}")
    print(f"  Phase:     {meta['phase']}")
    print(f"  Reasoning: {meta['reasoning']}")
    print("\nMandatory reading before implementing:")
    print("  1. docs/prompts/_shared/CONTRACTS.md (interfaces, layering, DB roles)")
    print("  2. docs/prompts/_shared/DECISIONS.md (architectural decisions)")
    print("\nOptions to view prompt content:")
    print(f"  - View summary: python3 tools/vibe.py next")
    print(f"  - Print full markdown: python3 tools/vibe.py next --print")
    print(f"  - Or read directly in editor: {next_p['path'].relative_to(ROOT)}")
    print("\nWhen finished:")
    print(f"  - Run verification: python3 tools/vibe.py verify {next_p['id']}")
    print(f"  - Commit progress:  python3 tools/vibe.py commit {next_p['id']}")
    print("-----------------------------------------------------------------")
    return 0


def check_forbidden_patterns() -> List[str]:
    """Scan source directories for forbidden stubbing patterns."""
    violations: List[str] = []
    forbidden = [
        r"\bTODO\b",
        r"\bFIXME\b",
        r"\bNotImplementedException\b",
        r"// similar to above",
        r"// rest omitted",
        r"for brevity",
        r"of\(mockData\)",
    ]
    compiled = [re.compile(p, re.IGNORECASE) for p in forbidden]

    scan_dirs = [ROOT / "backend", ROOT / "frontend", ROOT / "tools"]
    ignore_dirs = {
        "bin",
        "obj",
        "node_modules",
        ".git",
        ".angular",
        "dist",
        ".idea",
        ".vscode",
    }
    ignore_exts = {".dll", ".exe", ".pdb", ".png", ".jpg", ".ico", ".zip", ".db"}

    for base_dir in scan_dirs:
        if not base_dir.exists():
            continue
        for root, dirs, files in os.walk(base_dir):
            dirs[:] = [d for d in dirs if d not in ignore_dirs]
            for file in files:
                ext = os.path.splitext(file)[1].lower()
                if ext in ignore_exts or file.endswith(".trx"):
                    continue
                file_path = Path(root) / file
                # Skip the vibe tool itself and prompt markdown files
                if file_path == Path(__file__).resolve() or ext == ".md":
                    continue
                try:
                    text = file_path.read_text(encoding="utf-8", errors="ignore")
                    for line_idx, line in enumerate(text.splitlines(), start=1):
                        for pattern in compiled:
                            if pattern.search(line):
                                violations.append(
                                    f"{file_path.relative_to(ROOT)}:{line_idx}: '{line.strip()}' matches /{pattern.pattern}/"
                                )
                except Exception:
                    pass

    return violations


def cmd_verify(prompt_id: Optional[str] = None) -> int:
    """Run comprehensive verification for a completed prompt."""
    st = get_state()
    pid = prompt_id.upper().strip() if prompt_id else None

    if not pid:
        if st["completed"]:
            pid = st["completed"][-1]
        elif st["next_prompt"]:
            pid = st["next_prompt"]["id"]
        else:
            pid = "P-001"

    print(f"\n[VERIFY] Running verification suite for prompt {pid}...")
    errors: List[str] = []

    # 1. Prompt Pack Consistency Checker
    print("  1. Checking prompt pack consistency (verify_prompts.py)...", end=" ")
    try:
        res = subprocess.run(
            [sys.executable, str(VERIFY_SCRIPT_PATH)],
            cwd=ROOT,
            capture_output=True,
            text=True,
        )
        if res.returncode == 0:
            print("PASS")
        else:
            print("FAIL")
            errors.append(f"verify_prompts.py failed:\n{res.stdout}\n{res.stderr}")
    except Exception as e:
        print("FAIL")
        errors.append(f"Could not run verify_prompts.py: {e}")

    # 2. Check for Forbidden Patterns
    print("  2. Scanning source files for forbidden patterns...", end=" ")
    violations = check_forbidden_patterns()
    if not violations:
        print("PASS (clean, no stubs/TODOs found)")
    else:
        print("FAIL")
        err_msg = "Forbidden stub/pattern violations detected:\n" + "\n".join(
            f"     - {v}" for v in violations[:10]
        )
        if len(violations) > 10:
            err_msg += f"\n     ... and {len(violations) - 10} more"
        errors.append(err_msg)

    # 3. Check docs/BUILD_LOG.md
    print(f"  3. Verifying docs/BUILD_LOG.md entry for {pid}...", end=" ")
    build_log = BUILD_LOG_PATH.read_text(encoding="utf-8") if BUILD_LOG_PATH.exists() else ""
    log_pattern = re.compile(rf"^{re.escape(pid)}\s*\|\s*(DONE|COMPLETED)\s*\|", re.M)
    if log_pattern.search(build_log):
        print("PASS")
    else:
        print("FAIL")
        errors.append(
            f"docs/BUILD_LOG.md is missing completion entry for {pid}.\n"
            f"     Required format below '---': '{pid} | DONE | <files>'"
        )

    # 4. Check docs/ARTIFACTS.md
    print(f"  4. Verifying docs/ARTIFACTS.md registry section for {pid}...", end=" ")
    artifacts = ARTIFACTS_PATH.read_text(encoding="utf-8") if ARTIFACTS_PATH.exists() else ""
    art_pattern = re.compile(rf"^##\s*{re.escape(pid)}\b", re.M)
    if art_pattern.search(artifacts):
        print("PASS")
    else:
        print("FAIL")
        errors.append(
            f"docs/ARTIFACTS.md is missing registry section for {pid}.\n"
            f"     Required heading: '## {pid}' with created classes/interfaces/tables/endpoints."
        )

    # 5. Check dotnet build if dotnet is available and solution exists
    sln_path = ROOT / "StoreOS.sln"
    if sln_path.exists():
        dotnet_bin = shutil_which("dotnet")
        if dotnet_bin:
            print("  5. Running 'dotnet build StoreOS.sln'...", end=" ")
            try:
                b_res = subprocess.run(
                    [dotnet_bin, "build", "StoreOS.sln", "--nologo", "-v", "q"],
                    cwd=ROOT,
                    capture_output=True,
                    text=True,
                )
                if b_res.returncode == 0:
                    print("PASS")
                else:
                    print("FAIL")
                    errors.append(f"dotnet build failed:\n{b_res.stdout}\n{b_res.stderr}")
            except Exception as e:
                print("FAIL")
                errors.append(f"dotnet build execution error: {e}")

            # 6. Run fast Domain unit tests
            print("  6. Running fast domain tests ('dotnet test StoreOS.Domain.Tests')...", end=" ")
            try:
                t_res = subprocess.run(
                    [dotnet_bin, "test", "backend/tests/StoreOS.Domain.Tests", "--no-build", "--nologo", "-v", "q"],
                    cwd=ROOT,
                    capture_output=True,
                    text=True,
                )
                if t_res.returncode == 0:
                    print("PASS")
                else:
                    print("FAIL")
                    errors.append(f"Domain unit tests failed:\n{t_res.stdout}\n{t_res.stderr}")
            except Exception as e:
                print("FAIL")
                errors.append(f"Domain unit test execution error: {e}")
    # Summary
    if errors:
        print(f"\n[VERIFY FAILED] Found {len(errors)} failure(s):")
        for idx, err in enumerate(errors, start=1):
            print(f"\n  [{idx}] {err}")
        print("\nPlease resolve all issues before committing.\n")
        return 1

    print(f"\n[VERIFY PASSED] All checks passed for {pid}! Ready to commit.\n")
    return 0


def cmd_commit(prompt_id: Optional[str] = None) -> int:
    """Strict Commit: verify first, then create standard git commit."""
    st = get_state()
    pid = prompt_id.upper().strip() if prompt_id else None

    if not pid:
        # Determine current prompt from build log
        if st["completed"]:
            pid = st["completed"][-1]
        elif st["next_prompt"]:
            pid = st["next_prompt"]["id"]
        else:
            pid = "P-001"

    # Find prompt title
    matching = [p for p in st["index"] if p["id"] == pid]
    title = matching[0]["title"] if matching else "update"

    # Run verification first
    v_ret = cmd_verify(pid)
    if v_ret != 0:
        sys.stderr.write(
            f"\n[COMMIT REJECTED] Verification failed. You cannot commit until verification passes.\n\n"
        )
        return 1

    # Stage and commit
    try:
        subprocess.check_call(["git", "add", "-A"], cwd=ROOT)
        commit_msg = f"{pid}: {title}"
        subprocess.check_call(["git", "commit", "-m", commit_msg], cwd=ROOT)
        print(f"\n[COMMIT SUCCESS] Committed: '{commit_msg}'")
    except subprocess.CalledProcessError as e:
        sys.stderr.write(f"\n[COMMIT FAILED] Git commit failed: {e}\n\n")
        return 1

    # Refresh state and display progress
    new_st = get_state()
    done = len(new_st["completed"])
    total = new_st["total"]
    pct = (done / total * 100) if total else 0
    print(f"Overall Progress: {done}/{total} prompts completed ({pct:.1f}%).\n")

    # Sync reminder
    print("CROSS-DEVICE SYNC NOTE:")
    print("  Run 'git push' now so your changes are available when switching to your phone/other device!")
    return 0


def cmd_block(prompt_id: str, reason: str) -> int:
    """Write BLOCKED line to docs/BUILD_LOG.md below --- separator."""
    if not BUILD_LOG_PATH.exists():
        sys.stderr.write(f"Error: {BUILD_LOG_PATH} not found.\n")
        return 1

    content = BUILD_LOG_PATH.read_text(encoding="utf-8")
    parts = content.split("---", 1)
    if len(parts) < 2:
        sys.stderr.write(f"Error: docs/BUILD_LOG.md missing '---' separator.\n")
        return 1

    header = parts[0] + "---\n"
    body = parts[1]

    # Remove any existing BLOCKED line
    body_lines = [l for l in body.splitlines() if not l.strip().startswith("BLOCKED:")]
    block_line = f"BLOCKED: {prompt_id.upper()} — {reason}"
    new_body = block_line + "\n" + "\n".join(body_lines).lstrip("\n")

    BUILD_LOG_PATH.write_text(header + new_body + "\n", encoding="utf-8")
    print(f"[BLOCKED] Recorded in docs/BUILD_LOG.md:\n  {block_line}")
    return 0


def cmd_unblock() -> int:
    """Clear active BLOCKED line from docs/BUILD_LOG.md."""
    if not BUILD_LOG_PATH.exists():
        sys.stderr.write(f"Error: {BUILD_LOG_PATH} not found.\n")
        return 1

    content = BUILD_LOG_PATH.read_text(encoding="utf-8")
    parts = content.split("---", 1)
    if len(parts) < 2:
        return 0

    header = parts[0] + "---\n"
    body = parts[1]
    cleaned = [l for l in body.splitlines() if not l.strip().startswith("BLOCKED:")]
    BUILD_LOG_PATH.write_text(header + "\n".join(cleaned) + "\n", encoding="utf-8")
    print("[UNBLOCKED] Cleared active BLOCKED line in docs/BUILD_LOG.md.")
    return 0


def cmd_sync() -> int:
    """Check git sync status across remote and devices."""
    print("=================================================================")
    print("                  Cross-Device Sync Checker                      ")
    print("=================================================================")
    git = get_git_status()
    print(f"Current Branch: {git['branch']}")
    print(f"Working Tree:   {'Clean' if git['is_clean'] else 'DIRTY'}")

    try:
        remotes = subprocess.check_output(
            ["git", "remote", "-v"], cwd=ROOT, text=True
        ).strip()
        if not remotes:
            print("Remote:         None configured.")
            print("\nTip: To sync between this device and Android Termux:")
            print("  1. Create a private GitHub/GitLab repo: git remote add origin <url>")
            print("  2. Push your main branch: git push -u origin main")
            print("  3. On your Android phone (Termux Debian): git clone <url> StoreOS")
            print("  4. Always 'git push' before leaving, and 'git pull' upon arrival.")
        else:
            print(f"Remotes:\n{remotes}")
            if git["unpushed_count"] > 0:
                print(f"\n>>> ALERT: You have {git['unpushed_count']} unpushed commit(s)! Run 'git push' now! <<<")
            else:
                print("\nGit status is synchronized with upstream tracking branch.")
    except Exception as e:
        print(f"Remote check error: {e}")

    print("=================================================================")
    return 0


def shutil_which(cmd: str) -> Optional[str]:
    """Find binary in PATH."""
    import shutil
    return shutil.which(cmd)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="StoreOS Vibe-Coding Orchestrator & Gatekeeper",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Commands:
  status               Display progress dashboard, current gate status, and next prompt
  next [--print]       Display next prompt summary (or full markdown with --print)
  gate [PROMPT_ID]     Strict gatekeeper check: exits 0 if authorized, 1 if rejected
  verify [PROMPT_ID]   Run verification suite (patterns, consistency, logs, artifacts)
  commit [PROMPT_ID]   Verify and git commit with standard message
  block <ID> <REASON>  Record a block in docs/BUILD_LOG.md
  unblock              Remove active block from docs/BUILD_LOG.md
  sync                 Check git sync status for cross-device switching
""",
    )
    sub = parser.add_subparsers(dest="command")

    # status
    sub.add_parser("status", help="Show current state and next prompt")

    # next
    next_p = sub.add_parser("next", help="Get next authorized prompt")
    next_p.add_argument(
        "-p", "--print", action="store_true", help="Print full prompt markdown"
    )

    # gate
    gate_p = sub.add_parser("gate", help="Gate check prompt authorization")
    gate_p.add_argument("prompt_id", nargs="?", help="Prompt ID to validate (e.g. P-001)")

    # verify
    ver_p = sub.add_parser("verify", help="Verify prompt deliverables")
    ver_p.add_argument("prompt_id", nargs="?", help="Prompt ID being verified")

    # commit
    com_p = sub.add_parser("commit", help="Verify and commit prompt")
    com_p.add_argument("prompt_id", nargs="?", help="Prompt ID being committed")

    # block
    blk_p = sub.add_parser("block", help="Record BLOCKED status in build log")
    blk_p.add_argument("prompt_id", help="Prompt ID that is blocked")
    blk_p.add_argument("reason", help="Failure reason or error message")

    # unblock
    sub.add_parser("unblock", help="Remove active block from build log")

    # sync
    sub.add_parser("sync", help="Check cross-device git synchronization")

    args = parser.parse_args()

    if not args.command or args.command == "status":
        return cmd_status()
    elif args.command == "next":
        return cmd_next(print_content=args.print)
    elif args.command == "gate":
        return cmd_gate(args.prompt_id)
    elif args.command == "verify":
        return cmd_verify(args.prompt_id)
    elif args.command == "commit":
        return cmd_commit(args.prompt_id)
    elif args.command == "block":
        return cmd_block(args.prompt_id, args.reason)
    elif args.command == "unblock":
        return cmd_unblock()
    elif args.command == "sync":
        return cmd_sync()
    else:
        parser.print_help()
        return 1


if __name__ == "__main__":
    sys.exit(main())
