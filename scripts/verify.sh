#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export PYTHONDONTWRITEBYTECODE=1
export COMPOSE_PARALLEL_LIMIT=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1

python3 -B scripts/setup.py --check
python3 -B scripts/check-architecture.py
if [[ -f .env ]]; then
  docker compose config --quiet
  python3 -B scripts/check-resources.py
  docker compose --profile "*" stop
fi
dotnet build backend/SwiftRide.slnx -c Release -m:1 --disable-build-servers \
  -p:BuildInParallel=false -p:UseSharedCompilation=false -p:RestoreLockedMode=true
dotnet test backend/tests/SwiftRide.Checks -c Release --no-build --no-restore \
  --filter 'Category!=Integration'
