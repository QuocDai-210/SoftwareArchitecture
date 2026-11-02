#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/swiftride-dotnet}"
export DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 NEXT_TELEMETRY_DISABLED=1
if [[ "${1:-}" == "--core" ]]; then
  exec dotnet run --project backend/tests/SwiftRide.Checks -c Release -- --core
fi
if [[ ! -f .env ]]; then echo 'Run python3 scripts/setup.py first.' >&2; exit 1; fi
set -a
source .env
set +a
if [[ ! -f frontend/package-lock.json ]]; then ./scripts/lock.sh; fi
dotnet build backend/SwiftRide.slnx -c Release -p:RestoreLockedMode=true
(cd frontend && npm ci && npm run check && npm run build)
docker compose config --quiet
docker compose up --build -d --wait --wait-timeout 300
dotnet run --project backend/tests/SwiftRide.Checks -c Release --no-build -- --integration
