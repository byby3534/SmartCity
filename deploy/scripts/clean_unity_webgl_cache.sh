#!/usr/bin/env bash
# Remove stale WebGL/IL2CPP artifacts after changing WebGL Player Settings
# (threads, WebAssembly 2023, exceptions). Run from repo root.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
UNITY_DIR="$REPO_ROOT/Unity/ElninoEnergyRiskSimulation"

for path in \
  "$UNITY_DIR/Library/Bee/artifacts/WebGL" \
  "$UNITY_DIR/Library/Il2cppBuildCache" \
  "$UNITY_DIR/Library/PlayerDataCache" \
  "$UNITY_DIR/Temp"; do
  if [[ -d "$path" ]]; then
    echo "Removing $path"
    rm -rf "$path"
  fi
done

echo "WebGL build cache cleared. Rebuild SeoulBuildingProcessor.a, then Build in Unity."
