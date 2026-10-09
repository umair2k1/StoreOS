#!/usr/bin/env bash
# StoreOS Local Development Runner (Bash / POSIX / Termux)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Find Python 3
PYTHON_BIN=""
if command -v python3 >/dev/null 2>&1; then
    PYTHON_BIN="python3"
elif command -v python >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi

if [ -z "$PYTHON_BIN" ]; then
    echo "[error] Python 3 is required to run StoreOS locally." >&2
    echo "Please install Python 3 and ensure it is available in your PATH." >&2
    exit 1
fi

exec "$PYTHON_BIN" "$SCRIPT_DIR/run.py" "$@"
