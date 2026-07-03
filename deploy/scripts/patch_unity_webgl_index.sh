#!/usr/bin/env bash
# Strip .gz from Unity WebGL index.html asset URLs so nginx gzip_static can serve them.
# Run after copying a Unity WebGL build into www/.
set -euo pipefail

INDEX="${1:-$(cd "$(dirname "$0")/../.." && pwd)/www/index.html}"

if [[ ! -f "$INDEX" ]]; then
  echo "index.html not found: $INDEX" >&2
  exit 1
fi

if grep -qE '(dataUrl|frameworkUrl|workerUrl|codeUrl).*\.gz"' "$INDEX"; then
  sed -i '' \
    -e 's/\(dataUrl: buildUrl + "\/[^"]*\)\.data\.gz"/\1.data"/' \
    -e 's/\(frameworkUrl: buildUrl + "\/[^"]*\)\.framework\.js\.gz"/\1.framework.js"/' \
    -e 's/\(workerUrl: buildUrl + "\/[^"]*\)\.worker\.js\.gz"/\1.worker.js"/' \
    -e 's/\(codeUrl: buildUrl + "\/[^"]*\)\.wasm\.gz"/\1.wasm"/' \
    "$INDEX"
  echo "Patched $INDEX (removed .gz from Unity asset URLs)"
else
  echo "No .gz URLs to patch in $INDEX"
fi
