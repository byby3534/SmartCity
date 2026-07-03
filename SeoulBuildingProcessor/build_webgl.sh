#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PLUGIN_DIR="$REPO_ROOT/Unity/ElninoEnergyRiskSimulation/Assets/Plugins/WebGL"
SOURCE="$SCRIPT_DIR/SeoulBuildingProcessor.cpp"
OBJECT="$PLUGIN_DIR/SeoulBuildingProcessor.o"
ARCHIVE="$PLUGIN_DIR/SeoulBuildingProcessor.a"

UNITY_VERSION="${UNITY_VERSION:-6000.3.18f1}"
EMSDK_ROOT="/Applications/Unity/Hub/Editor/${UNITY_VERSION}/PlaybackEngines/WebGLSupport/BuildTools/Emscripten"
export EM_CONFIG="$EMSDK_ROOT/.emscripten"
EMCC="$EMSDK_ROOT/emscripten/emcc"
EMAR="$EMSDK_ROOT/emscripten/emar"

if [[ ! -x "$EMCC" ]]; then
    echo "Unity Emscripten not found at: $EMCC" >&2
    echo "Set UNITY_VERSION or install WebGL Build Support." >&2
    exit 1
fi

mkdir -p "$PLUGIN_DIR"

# Unity Player Settings → WebGL → Publishing: Multithreading ON (기본)
#   ON  → USE_PTHREADS=1 SHARED_MEMORY=1  (localhost:8080, HTTPS 배포)
#   OFF → USE_PTHREADS=0 ./build_webgl.sh   (LAN http://192.168.x.x 공유 시)
USE_PTHREADS="${USE_PTHREADS:-1}"

EMCC_FLAGS=(-std=c++17 -O2 -I "$SCRIPT_DIR")
if [[ "$USE_PTHREADS" == "1" ]]; then
  EMCC_FLAGS+=(-s USE_PTHREADS=1 -s SHARED_MEMORY=1)
  echo "Building with pthreads (matches Unity Multithreading ON)"
else
  echo "Building without pthreads (Unity Multithreading OFF)"
fi

"$EMCC" "${EMCC_FLAGS[@]}" -c "$SOURCE" -o "$OBJECT"

"$EMAR" rcs "$ARCHIVE" "$OBJECT"

echo "Built: $OBJECT"
echo "Built: $ARCHIVE"
