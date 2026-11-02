#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/swiftride-dotnet}"
dotnet restore backend/SwiftRide.slnx --use-lock-file
(
  cd frontend &&
    npm install --package-lock-only --ignore-scripts --no-audit --no-fund
)
