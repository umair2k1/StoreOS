# StoreOS Cloud — Build Log

Append-only. One line per completed prompt: `<PROMPT_ID> | <STATUS> | <FILES>`.
If a prompt is blocked, insert a line starting with `BLOCKED:` as the FIRST line below
the `---` separator (never above the `# StoreOS Cloud — Build Log` title). Remove the
BLOCKED line only when the prompt is later completed.

---
O-03 | DECIDED | Option B: upgraded toolchain before P-001 to .NET 10 LTS, Node 24 LTS, Angular 21
P-001 | DONE | StoreOS.sln, backend/src/StoreOS.Domain/StoreOS.Domain.csproj, backend/src/StoreOS.Application/StoreOS.Application.csproj, backend/src/StoreOS.Infrastructure/StoreOS.Infrastructure.csproj, backend/src/StoreOS.Modules/StoreOS.Modules.csproj, backend/src/StoreOS.Api/StoreOS.Api.csproj, backend/src/StoreOS.Api/Program.cs, backend/src/StoreOS.Api/appsettings.json, backend/src/StoreOS.Api/appsettings.Development.json, backend/src/StoreOS.Api/Properties/launchSettings.json, backend/src/StoreOS.Api/StoreOS.Api.http, backend/tests/StoreOS.Domain.Tests/StoreOS.Domain.Tests.csproj, backend/tests/StoreOS.Domain.Tests/UnitTest1.cs, backend/tests/StoreOS.IntegrationTests/StoreOS.IntegrationTests.csproj, backend/tests/StoreOS.IntegrationTests/UnitTest1.cs, backend/tests/StoreOS.ConcurrencyTests/StoreOS.ConcurrencyTests.csproj, backend/tests/StoreOS.ConcurrencyTests/UnitTest1.cs, frontend/.gitkeep, deploy/.gitkeep, tools/pz-migrate/.gitkeep, tools/seed/.gitkeep, .gitignore, docs/BUILD_LOG.md, docs/ARTIFACTS.md
P-002 | DONE | deploy/docker-compose.yml, deploy/otel-collector-config.yaml, deploy/db/init/01-app-role.sh, deploy/db/post-migrate.sql, deploy/db/apply-post-migrate.sh, docs/ARTIFACTS.md, docs/BUILD_LOG.md
P-003 | DONE | deploy/Dockerfile.api, deploy/Dockerfile.web, deploy/nginx.conf, deploy/docker-compose.yml, docs/ARTIFACTS.md, docs/BUILD_LOG.md | note: docker build -f deploy/Dockerfile.web is DEFERRED until P-007 (after frontend Angular workspace + package.json are created)
