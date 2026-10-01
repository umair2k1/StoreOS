# Toolchain decision (open question O-03) — read before running P-001

The prompt pack pins **.NET 8**, **Angular 18** and **Node 20**. Support status as of 30 Sep 2026:

| Component | Pinned in pack | Status |
|---|---|---|
| .NET | 8.0 (LTS) | end of support **10 Nov 2026** (Microsoft announcement, June 2026). .NET 10 LTS is supported until Nov 2028. |
| Angular | 18 | out of support since Nov 2025. Angular 20 ends 28 Nov 2026; 21 and 22 are supported. |
| Node.js | 20 | end of life April 2026. Use an active LTS (22 or 24). |

Nothing breaks on day one if you keep the pins: everything in the pack is consistent and reproducible. The risk is shipping a production
POS that handles financial data on runtimes that stop receiving security fixes about six weeks from now.

## Option A — keep the pins (fastest start)
Do nothing. Record "O-03: kept .NET 8 / Angular 18 / Node 20, upgrade planned before <date>" in `docs/BUILD_LOG.md`. Plan the LTS-to-LTS upgrade
(.NET 8 -> 10 is a normal upgrade) right after Phase 2, not after go-live.

## Option B — upgrade before P-001 (recommended)
Only the prompts below contain pins; every other prompt just says "the pinned version".

| Where | What to change |
|---|---|
| P-001, P-003, P-004, P-005, P-903, P-1201, P-1203, P-1204, P-1206 | `net8.0` -> `net10.0`; `mcr.microsoft.com/dotnet/sdk:8.0` / `aspnet:8.0` -> `10.0`; `actions/setup-dotnet` `dotnet-version: "10.0.x"` |
| P-004 | Re-resolve `Directory.Packages.props`: EF Core, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Extensions.*`, health checks to the 10.0.x line; `dotnet-ef` tool version = EF Core version |
| P-003, P-005, P-006, P-007, P-1203 | Node image / `node-version` -> 22 or 24; Angular CLI/core/material to the chosen major (`ng new` at that version), re-pin `package.json` |

Rules for re-pinning (keep these from the original pack, they exist for licensing/stability reasons):
1. **MediatR stays on the 12.x line** and **FluentAssertions on 6.x** (later majors changed to commercial licences).
2. `QuestPDF.Settings.License = LicenseType.Community` stays (P-806); check the community-licence revenue limit for the shop.
3. Pin exact versions (no floating `*`), one central `Directory.Packages.props`, and let the executor run `dotnet list package --vulnerable --include-transitive`
   after P-004; any High/Critical finding blocks the prompt (SixLabors.ImageSharp in particular has had several advisories — take the latest patched 3.1.x or newer).
4. Do the upgrade as ONE change before P-001 and record the resulting versions in `docs/BUILD_LOG.md`; do not mix versions mid-build.
5. The exact package versions in P-004/P-007 were not re-verified here (no package-feed access from the review environment), so let the executor confirm every
   version exists on the feed (`dotnet restore` fails fast) and adjust within the same minor line.
