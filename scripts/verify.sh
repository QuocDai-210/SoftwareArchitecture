#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/swiftride-dotnet}"
export \
  DOTNET_NOLOGO=1 \
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  PYTHONDONTWRITEBYTECODE=1 \
  NEXT_TELEMETRY_DISABLED=1 \
  COMPOSE_PARALLEL_LIMIT=1 \
  DOTNET_CLI_USE_MSBUILD_SERVER=0 \
  MSBUILDDISABLENODEREUSE=1 \
  NODE_OPTIONS="${NODE_OPTIONS:---max-old-space-size=1536}"
python3 scripts/check-architecture.py
if [[ "${1:-}" == "--core" ]]; then
  if [[ -f .env ]] && command -v docker >/dev/null && docker info >/dev/null 2>&1; then
    docker compose --profile observability stop
  fi
  exec dotnet run --project backend/tests/SwiftRide.Checks -c Release --disable-build-servers -p:BuildInParallel=false -p:UseSharedCompilation=false -- --core
fi
if [[ ! -f .env ]]; then
  echo 'Run python3 scripts/setup.py first.' >&2
  exit 1
fi
# Release project services before compiling; retain containers and persistent data.
docker compose --profile observability stop
set -a
source .env
set +a
if [[ ! -f frontend/package-lock.json ]]; then
  ./scripts/lock.sh
fi
python3 scripts/check-resources.py
dotnet build backend/SwiftRide.slnx -c Release -m:1 --disable-build-servers -p:UseSharedCompilation=false -p:RestoreLockedMode=true
(
  cd frontend &&
    npm ci &&
    npm run check &&
    npm run build
)
node scripts/check-frontend.mjs
docker compose config --quiet
# Stop test services on success, failure, or interruption; preserve volumes.
trap 'docker compose --profile observability stop' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
for service in rabbitmq trip matching payment gateway web; do
  docker compose build "$service"
done
docker compose up --no-build -d --wait --wait-timeout 300
python3 -B scripts/sync-identity.py
dotnet run --project backend/tests/SwiftRide.Checks -c Release --no-build -- --core
node scripts/browser-check.mjs
