# P-003 — Dockerfile.api, Dockerfile.web multi-stage

**Phase:** 0 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-003** of the StoreOS Cloud build ("Dockerfile.api, Dockerfile.web multi-stage") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-001 and P-002 are complete and committed. The .NET solution builds; the
  dev docker-compose stack (postgres/minio/otel-collector) starts healthy.
- `frontend/` still only contains a `.gitkeep` (Angular workspace is created in P-006) — this
  prompt must still produce a valid `Dockerfile.web` that will work once `frontend/` is
  populated; it does not need to build successfully yet since there is no Angular app, but
  its syntax must be correct and it must be referenced correctly from docker-compose.
- Do not modify `backend/src/**/*.cs` in this prompt.

## 3. CONTEXT BLOCK

Create `deploy/Dockerfile.api` (multi-stage, SDK build -> ASP.NET runtime,
non-root):

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore StoreOS.sln
RUN dotnet publish src/StoreOS.Api/StoreOS.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN addgroup --system --gid 1000 storeos && \
    adduser --system --uid 1000 --ingroup storeos --shell /bin/false storeos
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER storeos
ENTRYPOINT ["dotnet", "StoreOS.Api.dll"]
```

Note the build context in `docker-compose.yml` (set in P-002) is `../backend`, so
`COPY . .` copies `backend/*` into `/src`, and the solution file used in the build stage must
be the backend-local one. Since P-001 put `StoreOS.sln` at the repo root (`store-os-cloud/StoreOS.sln`),
not inside `backend/`, you must correct this: either (a) move `StoreOS.sln` so a copy/symlink
exists at `backend/StoreOS.sln` referencing the same relative project paths, or (b) change the
Dockerfile's restore/publish paths and the compose build context accordingly. Prefer option
(b) for a single source of truth: set the Dockerfile's `dotnet restore` command to operate
directly against the `.csproj` (no solution file needed for a single-project publish), i.e.
use `dotnet restore src/StoreOS.Api/StoreOS.Api.csproj` instead of restoring the whole
solution, and set `docker-compose.yml`'s `api` service `context: ..` (repo root) with
`dockerfile: deploy/Dockerfile.api`, and change `COPY . .` to `COPY backend/ /src/backend/`
plus `WORKDIR /src/backend`. Use this corrected Dockerfile:

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ ./backend/
WORKDIR /src/backend
RUN dotnet restore src/StoreOS.Api/StoreOS.Api.csproj
RUN dotnet publish src/StoreOS.Api/StoreOS.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN addgroup --system --gid 1000 storeos && \
    adduser --system --uid 1000 --ingroup storeos --shell /bin/false storeos
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER storeos
ENTRYPOINT ["dotnet", "StoreOS.Api.dll"]
```

Create `deploy/Dockerfile.web` (node build -> nginx):

```dockerfile
# syntax=docker/dockerfile:1
FROM node:24-alpine AS build
WORKDIR /src
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build -- --configuration production

FROM nginx:1.27-alpine AS runtime
RUN addgroup -g 1000 storeos && adduser -D -u 1000 -G storeos storeos
COPY --from=build /src/dist/storeos-web/browser /usr/share/nginx/html
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
RUN chown -R storeos:storeos /usr/share/nginx/html /var/cache/nginx /var/run \
    && touch /var/run/nginx.pid && chown storeos:storeos /var/run/nginx.pid
USER storeos
EXPOSE 8080
```

Create `deploy/nginx.conf`:

```nginx
server {
    listen 8080;
    server_name _;
    root /usr/share/nginx/html;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location ~* \.(?:js|css|woff2?|png|jpg|jpeg|svg|webmanifest)$ {
        expires 30d;
        add_header Cache-Control "public, immutable";
    }
}
```

## 4. TASK

1. Update `deploy/docker-compose.yml`'s `api` service to use `context: ..` and
   `dockerfile: deploy/Dockerfile.api` (repo-root build context), matching the corrected
   Dockerfile in the CONTEXT BLOCK.
2. Create `deploy/Dockerfile.api` exactly as the corrected version shown.
3. Create `deploy/Dockerfile.web` and `deploy/nginx.conf` exactly as shown.
4. Add a `web` service to `deploy/docker-compose.yml` under the `full` profile, port
   `8081:8080`, `build.context: ..`, `build.dockerfile: deploy/Dockerfile.web`.
5. Build the API image: `docker build -f deploy/Dockerfile.api -t storeos-api:dev .` from the
   repo root. This must succeed even though the API has no real logic yet (P-001's default
   webapi template is enough to publish).
6. Do NOT attempt to build `Dockerfile.web` yet in verification — `frontend/` has no
   `package.json` until P-006, so record in `docs/BUILD_LOG.md` that `Dockerfile.web` build
   verification is deferred to P-007 (after Angular workspace + package.json exist), and note
   this explicitly.

## 5. HARD CONSTRAINTS

- Both Dockerfiles must run the final process as a non-root user
  (`storeos`, uid 1000) as shown — do not remove the `USER storeos` line.
- Do not add unnecessary build tools or debug packages into the runtime stage images.

Global invariants (apply to all backend code in this repo):

```
- Money: C# decimal ONLY. Never double/float. numeric(18,2) in Postgres.
  Round half-up: MidpointRounding.AwayFromZero, 2 dp, via Money.Round2.
- EMI uses pure decimal exponentiation (DecimalPow loop) — Math.Pow is forbidden.
- Timestamps: timestamptz, stored UTC. Business date computed in tenant timezone
  (Asia/Kolkata default).
- Every tenant table has tenant_id; indexes lead with tenant_id (then branch_id).
- All DB access happens inside transactions with SET LOCAL app.tenant_id / app.branch_ids /
  app.all_branches (RLS session scope). No query outside this scope.
- Validation failures -> 400 with field problems; domain violations -> DomainException -> 422
  with code+message. Never a 500 for business rule violations.
- Authorization runs in MediatR pipeline behaviors, never controllers, never frontend.
- Server recomputes all prices/taxes/totals from DB master data; client-submitted amounts
  are ignored unless an override permission exists.
- Historical snapshots on sale lines are immutable; ledgers and audit_log are append-only.
- tenant_id-scoped RLS must be active on every table containing tenant_id.
- Namespace convention: StoreOS.* everywhere (StoreOS.Domain, StoreOS.Application,
  StoreOS.Infrastructure, StoreOS.Api, StoreOS.Modules). Never PhoneZone.*.
- Layering: StoreOS.Application NEVER references StoreOS.Infrastructure. Application code reaches
  the database only through IAppDbContext and reaches infrastructure services only through
  interfaces declared in StoreOS.Application/Abstractions (see _shared/CONTRACTS.md).
- Transactions: exactly ONE transaction per MediatR request, opened by TransactionBehavior
  (Infrastructure). Handlers never call BeginTransaction/Commit/Rollback. Serialization-failure
  retry wraps the WHOLE transaction (SerializationRetryBehavior is the outermost behavior).
- RLS: policies read session variables ONLY via NULLIF(current_setting('app.x', true), '') and
  never contain an "IS NULL" bypass. Queries implement IReadOnlyRequest, report queries
  IReportingRequest, Serializable commands ISerializableRequest (Application/Abstractions).
```

## 6. FORBIDDEN PATTERNS

```
FORBIDDEN in all code you produce:
- TODO / FIXME / HACK / XXX comments
- NotImplementedException / NotSupportedException / "not implemented" exceptions
- Empty method bodies; methods returning null/0/"" as placeholders for real logic
- "// similar to above", "// rest omitted", "for brevity", ellipses (...) inside code
- Mock/fake/in-memory service implementations in production code (test projects ONLY)
- Hardcoded demo data in production code paths
- Angular: "coming soon" templates, buttons with empty (click) handlers, services
  returning of(mockData) instead of real HTTP calls
If you cannot fully implement something: STOP and write "BLOCKED: <reason>" as the first line below the `---`
separator in docs/BUILD_LOG.md. Never silently stub. Before finishing, grep your created/modified files
for the forbidden patterns above and fix every hit before running verification.
```

## 7. VERIFICATION

```bash
cd store-os-cloud
docker build -f deploy/Dockerfile.api -t storeos-api:dev .
# Expected: "Successfully tagged storeos-api:dev" (or BuildKit "naming to ... done")
docker run --rm storeos-api:dev dotnet StoreOS.Api.dll --version 2>&1 | head -1 || true
# (best-effort smoke; the important assertion is that the image built successfully above)
```
Record explicitly in `docs/BUILD_LOG.md` that `docker build -f deploy/Dockerfile.web` is
DEFERRED until P-007 and must be run then.

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-003 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-003 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-003` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact final `deploy/docker-compose.yml` `api` and `web` service
definitions, and the note that `Dockerfile.web`'s build verification is deferred to P-007.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-003: <one-line summary>"` only after verification is
   fully green.
