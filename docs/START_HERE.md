# StoreOS prompt pack — START HERE (review v2)

This is the corrected version of `storeos-prompts.zip`. It still has the same shape (157 self-contained prompts, run in the order of
`prompts/INDEX.md`), but the prompts have been checked against each other and against the product requirements / implementation plan,
and about a dozen defects that would have stopped or silently broken the build were fixed (see `REVIEW_REPORT.md`).

## Before you run anything
1. Read `TOOLCHAIN_UPGRADE.md` and decide open question **O-03** (runtime versions). It is the only thing that has to be decided before P-001.
2. Skim `prompts/_shared/CONTRACTS.md` (interfaces, layering, pipeline order, DB roles). Every prompt tells the executor to read it and
   `prompts/_shared/DECISIONS.md` first; if a prompt's code sample ever disagrees with CONTRACTS, CONTRACTS wins.
3. Run the pack checker once: `python3 tools/verify_prompts.py` (must print `OK`). Run it again after you edit any prompt.

## How to run
* One prompt = one message to the executor (Oh My Pi IDE), strictly in `INDEX.md` order. Do not skip or batch.
* Each prompt ends with a REPORT step that appends to `docs/BUILD_LOG.md` and `docs/ARTIFACTS.md` (created by P-001); later prompts quote those files.
* If a prompt writes `BLOCKED:` in `docs/BUILD_LOG.md`, stop and read it. Fix the cause (or the prompt), do not let the executor stub.
* Prompts marked `high` reasoning should go to your strongest model; `low`/`med` are boilerplate/CRUD.

## What changed in the file layout
* New: `TOOLCHAIN_UPGRADE.md`, `REVIEW_REPORT.md`, `prompts/_shared/CONTRACTS.md`, `prompts/_shared/DECISIONS.md`, `tools/verify_prompts.py`.
* New prompts: `P-313` (change/reset password), `P-1206` (tenant provisioning CLI).
* Rewritten (IDs unchanged, files renamed): `P-109-transaction-behavior-rls-session.md`, `P-117-idempotency-behavior.md`, `P-511-serialization-retry-behavior.md`.
* Reordered: P-112 and P-113 now run before P-109.
* Everything else received targeted "REVISION NOTE (review v2)" blocks at the top of its CONTEXT BLOCK; search for that string to see every change.

## Open items that need a human (not blockers for starting)
O-01 invoice-series sign-off by the shop's CA, O-02 GSTR-1 mapping review, O-03 toolchain versions. Details in `prompts/_shared/DECISIONS.md`.
