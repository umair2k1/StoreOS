#!/usr/bin/env python3
"""Wrapper to run verify_prompts.py from repository root."""
import os
import runpy
import sys

_tools_dir = os.path.dirname(os.path.abspath(__file__))
_repo_root = os.path.dirname(_tools_dir)
_target = os.path.join(_repo_root, "docs", "tools", "verify_prompts.py")

if not os.path.exists(_target):
    sys.stderr.write(f"Error: {_target} does not exist.\n")
    sys.exit(1)

runpy.run_path(_target, run_name="__main__")
