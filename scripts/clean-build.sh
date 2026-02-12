#!/usr/bin/env bash
set -euo pipefail

SOLUTION="${1:-DfsMonitor.sln}"
RUN_TESTS="${RUN_TESTS:-0}"

echo "[clean-build] Restoring..."
dotnet restore "$SOLUTION"

echo "[clean-build] Cleaning..."
dotnet clean "$SOLUTION" -c Release

echo "[clean-build] Building..."
dotnet build "$SOLUTION" -c Release --no-restore

if [[ "$RUN_TESTS" == "1" ]]; then
  echo "[clean-build] Testing..."
  dotnet test "$SOLUTION" -c Release --no-build
fi

echo "[clean-build] Completed successfully."
