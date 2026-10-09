#!/usr/bin/env python3
"""StoreOS Local Runner entrypoint.

Runs the complete StoreOS local development stack:
- Docker services (PostgreSQL 16, MinIO S3, OTel Collector)
- Database schema migrations & post-migrate permissions
- Demo tenant verification & seeding
- Backend API (.NET 10) & Frontend Web (Angular 21)

Usage:
  python run.py                   # Run full stack
  python run.py --help            # See all options
"""

import sys
from pathlib import Path

# Add tools directory to Python path
tools_dir = Path(__file__).resolve().parent / "tools"
sys.path.insert(0, str(tools_dir))

import run_local

if __name__ == "__main__":
    sys.exit(run_local.main())
