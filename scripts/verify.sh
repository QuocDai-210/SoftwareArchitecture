#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/swiftride-dotnet}"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export PYTHONDONTWRITEBYTECODE=1
export NEXT_TELEMETRY_DISABLED=1
export COMPOSE_PARALLEL_LIMIT=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
export NODE_OPTIONS="${NODE_OPTIONS:---max-old-space-size=1536}"

python3 -B scripts/setup.py --check
python3 -B scripts/check-architecture.py
if [[ -f .env ]]; then
  docker compose config --quiet
  python3 -B scripts/check-resources.py
  docker compose --profile "*" stop
fi
dotnet test backend/SwiftRide.slnx -c Release -m:1 --disable-build-servers \
  -p:BuildInParallel=false -p:UseSharedCompilation=false --filter 'Category!=Integration'
(
  cd frontend
  npm ci
  npm run check
  npm run build
)
node scripts/check-frontend.mjs
