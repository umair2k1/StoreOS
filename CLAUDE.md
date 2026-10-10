# StoreOS — Agent Instruction Rules & Architecture Guide

> **STATUS: 157/157 PROMPTS COMPLETED (100% COMPLETE - BUILD GREEN)**
> The initial sequential prompt-pack bootstrap (`docs/prompts/INDEX.md`) is fully implemented and verified.
> The repository is currently in **Feature Development & Production Maintenance Mode**.
> Sequential prompt-pack gating is retired for routine tasks, while core architecture invariants and anti-stub standards remain strictly enforced.

---

## 1. Multi-Device Setup (Windows Workstation & Android Termux)
You may be running on a Windows PC or on an Android phone (Termux in Debian).
- **Environment**:
  - Python 3 is available on both devices.
  - Line endings are normalized to `LF` in `.gitattributes`.
  - Always verify git synchronization before starting: `python3 tools/vibe.py sync`.
  - Before switching devices, always run `git push`. When arriving on the other device, run `git pull`.
  - Repository status check: `python3 tools/vibe.py status`.

---

## 2. Architecture & Invariant Rules
- **Layering**:
  `StoreOS.Domain` $\leftarrow$ `StoreOS.Application` $\leftarrow$ `StoreOS.Infrastructure` $\leftarrow$ `StoreOS.Api`.
  `StoreOS.Application` **NEVER** references `StoreOS.Infrastructure`.
- **Database**:
  - Two roles: `storeos_owner` (migrations/tools) and `storeos_app` (`NOSUPERUSER NOBYPASSRLS`).
  - Mandatory RLS expression: `NULLIF(current_setting('app.tenant_id', true), '')::uuid`.
- **Anti-Stub Policy**:
  - No `TODO`, `FIXME`, `NotImplementedException`, `// rest omitted`, `for brevity`, or `of(mockData)`.
- **Design & Invariants**:
  - Always consult `docs/prompts/_shared/CONTRACTS.md` and `docs/prompts/_shared/DECISIONS.md`.
  - Coding Standards: See [CODING_STANDARDS.md](CODING_STANDARDS.md) for MediatR open-behavior constraints, session cache invalidation, and EF Core composite key conventions.

---

## 3. Development, Verification & Delivery Workflow

### Step 1: Sync & Verify Working Tree
```bash
python3 tools/vibe.py sync
python3 tools/vibe.py status
```

### Step 2: Implementation & Verification
1. Implement the requested fix or feature with no placeholder stubs.
2. Verify Backend:
   ```bash
   dotnet build backend/src/StoreOS.Api/StoreOS.Api.csproj
   dotnet test backend/tests/StoreOS.Tests/StoreOS.Tests.csproj
   ```
3. Verify Frontend:
   ```bash
   cd frontend && npm test -- --watch=false
   ```
4. Verify Quality & Formatting:
   - Ensure no forbidden patterns (`TODO`, `FIXME`, `NotImplementedException`) exist in modified files.

### Step 3: Commit & Hand-off
```bash
git add <files>
git commit -m "feat/fix(<scope>): <description>"
git push
```

---

## 4. Role of `tools/vibe.py` & Historical Artifacts
- `tools/vibe.py`: Preserved for multi-device sync (`vibe.py sync`), baseline status verification (`vibe.py status`), and code quality checking.
- `docs/prompts/`, `docs/BUILD_LOG.md`, and `docs/ARTIFACTS.md`: Preserved as the permanent audit trail and design provenance for the 157-step foundational build.
