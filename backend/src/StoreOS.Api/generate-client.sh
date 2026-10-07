#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"

export DOTNET_ROLL_FORWARD=Major
cd "$REPO_ROOT/frontend"
npx nswag aspnetcore2openapi /project:../backend/src/StoreOS.Api/StoreOS.Api.csproj /noBuild:true /output:src/app/core/api/openapi.json
npx nswag run nswag.json
