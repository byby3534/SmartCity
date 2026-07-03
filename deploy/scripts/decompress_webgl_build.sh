#!/usr/bin/env bash
# Decompress Unity WebGL .gz build artifacts for local nginx (no Content-Encoding issues).
# Run after copying a gzip-compressed Unity build into www/Build/.
set -euo pipefail

BUILD_DIR="${1:-$(cd "$(dirname "$0")/../.." && pwd)/www/Build}"

if [[ ! -d "$BUILD_DIR" ]]; then
  echo "Build directory not found: $BUILD_DIR" >&2
  exit 1
fi

shopt -s nullglob
gz_files=("$BUILD_DIR"/*.gz)
if [[ ${#gz_files[@]} -eq 0 ]]; then
  echo "No .gz files in $BUILD_DIR (already decompressed?)"
  exit 0
fi

for f in "${gz_files[@]}"; do
  out="${f%.gz}"
  if [[ -f "$out" ]]; then
    echo "Skip $(basename "$f") — $(basename "$out") already exists"
    continue
  fi
  echo "Decompressing $(basename "$f") → $(basename "$out")"
  gunzip -c "$f" > "$out"
done

echo "Done. www/Build/ now has uncompressed assets for local serving."
