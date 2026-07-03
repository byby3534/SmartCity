#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PLUGIN_DIR="$REPO_ROOT/Unity/ElninoEnergyRiskSimulation/Assets/Plugins/macOS"
BUNDLE_DIR="$PLUGIN_DIR/SeoulBuildingProcessor.bundle"
MACOS_DIR="$BUNDLE_DIR/Contents/MacOS"
SOURCE="$SCRIPT_DIR/SeoulBuildingProcessor.cpp"

ARCH="${ARCH:-arm64}"

mkdir -p "$MACOS_DIR"

cat > "$BUNDLE_DIR/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>SeoulBuildingProcessor</string>
    <key>CFBundleIdentifier</key>
    <string>com.smartcity.SeoulBuildingProcessor</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>SeoulBuildingProcessor</string>
    <key>CFBundlePackageType</key>
    <string>BNDL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>CFBundleSupportedPlatforms</key>
    <array>
        <string>MacOSX</string>
    </array>
</dict>
</plist>
EOF

clang++ -std=c++17 -fPIC -shared -O2 -arch "$ARCH" \
    -I "$SCRIPT_DIR" \
    -o "$MACOS_DIR/SeoulBuildingProcessor" \
    "$SOURCE"

echo "Built: $MACOS_DIR/SeoulBuildingProcessor ($ARCH)"
nm -gU "$MACOS_DIR/SeoulBuildingProcessor" | grep -E "LoadDistrict|BuildDistrict|ClearAll" || true
