# StoreOS — Agent Instruction Rules & Strict Vibe-Coding Protocol

> **CRITICAL RULE FOR ALL ASSISTANTS & EXECUTORS (omp on Windows, Android Termux/Debian, or remote):**
> This repository is built strictly using a verified 157-step sequential prompt pack (`docs/prompts/INDEX.md`).
> Out-of-order execution, skipped prompts, freelanced architectures, or stubs are strictly forbidden.

---

## 1. Multi-Device Setup (Windows Workstation & Android Termux)
You may be running on a Windows PC or on an Android phone (Termux in Debian).
- **Environment**:
  - Python 3 is available on both devices.
  - Line endings are normalized to `LF` in `.gitattributes`.
  - Always verify git synchronization before starting: `python3 tools/vibe.py sync`.
  - Before switching devices, always run `git push`. When arriving on the other device, run `git pull`.

---

## 2. Strict Vibe-Coding Workflow

### Step 1: Gate Check
Before touching any code or reading an arbitrary prompt, run:
```bash
python3 tools/vibe.py status
python3 tools/vibe.py gate [PROMPT_ID]
```
If the gatekeeper exits with code 1 (due to out-of-order prompt, dirty working tree, or active block), **STOP IMMEDIATELY**. Do not proceed.

### Step 2: Retrieve Next Prompt
To retrieve the next authorized prompt:
```bash
python3 tools/vibe.py next
# To view the complete prompt text:
python3 tools/vibe.py next --print
```

### Step 3: Architecture & Invariant Rules
- Always consult `docs/prompts/_shared/CONTRACTS.md` and `docs/prompts/_shared/DECISIONS.md`. If a prompt code sample conflicts with `CONTRACTS.md`, **CONTRACTS.md wins**.
- **Layering**:
  `StoreOS.Domain` $\leftarrow$ `StoreOS.Application` $\leftarrow$ `StoreOS.Infrastructure` $\leftarrow$ `StoreOS.Api`.
  `StoreOS.Application` **NEVER** references `StoreOS.Infrastructure`.
- **Database**:
  - Two roles: `storeos_owner` (migrations/tools) and `storeos_app` (`NOSUPERUSER NOBYPASSRLS`).
  - Mandatory RLS expression: `NULLIF(current_setting('app.tenant_id', true), '')::uuid`.
- **Anti-Stub Policy**:
  - No `TODO`, `FIXME`, `NotImplementedException`, `// rest omitted`, `for brevity`, or `of(mockData)`.

### Step 4: Verification
After implementing the prompt:
1. Append to `docs/BUILD_LOG.md`:
   `<PROMPT_ID> | DONE | <list of modified/created files>`
2. Append to `docs/ARTIFACTS.md` under `## <PROMPT_ID>`:
   Exact signatures of all created entities, interfaces, tables, endpoints, or components.
3. Run verification:
   ```bash
   python3 tools/vibe.py verify <PROMPT_ID>
   ```
   All checks (forbidden pattern scan, prompt pack consistency, log entries, build) must PASS.

### Step 5: Commit & Hand-off
Once verification passes:
```bash
python3 tools/vibe.py commit <PROMPT_ID>
```
If you are finishing your turn or switching devices, push your commit:
```bash
git push
```

### Step 6: Failure Protocol
If a prompt cannot be implemented after 3 attempts:
```bash
python3 tools/vibe.py block <PROMPT_ID> "<error message>"
```
Do not commit broken code or stubs. End your turn.
