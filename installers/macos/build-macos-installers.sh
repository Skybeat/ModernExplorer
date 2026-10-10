#!/usr/bin/env bash
set -euo pipefail

# Builds macOS native installers (.dmg disk image and .pkg installer with ModernExplorer.app bundle)
# Usage: ./build-macos-installers.sh <rid> <version> <publish_dir> <icon_png> <output_dir>

RID="${1:-osx-arm64}"
RAW_VERSION="${2:-1.0.0}"
VERSION="${RAW_VERSION#v}"
PUBLISH_DIR="${3}"
ICON_PNG="${4}"
OUTPUT_DIR="${5}"

mkdir -p "${OUTPUT_DIR}"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

APP_BUNDLE="${WORK_DIR}/dmg_stage/ModernExplorer.app"
mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

cp -a "${PUBLISH_DIR}/." "${APP_BUNDLE}/Contents/MacOS/"
chmod +x "${APP_BUNDLE}/Contents/MacOS/ModernExplorer"
cp "${ICON_PNG}" "${APP_BUNDLE}/Contents/Resources/AppIcon.png"

cat > "${APP_BUNDLE}/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Modern Explorer</string>
    <key>CFBundleDisplayName</key>
    <string>Modern Explorer</string>
    <key>CFBundleIdentifier</key>
    <string>com.skybeat.modernexplorer</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>ModernExplorer</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon.png</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
</dict>
</plist>
EOF

# 1. Build macOS .pkg Installer
PKG_OUT="${OUTPUT_DIR}/ModernExplorer-Setup-${RID}.pkg"
pkgbuild \
  --component "${APP_BUNDLE}" \
  --install-location "/Applications" \
  --identifier "com.skybeat.modernexplorer.pkg" \
  --version "${VERSION}" \
  "${PKG_OUT}"
echo "Built macOS .pkg installer: ${PKG_OUT}"

# 2. Build macOS .dmg Disk Image Installer (with /Applications drag-and-drop symlink)
ln -s /Applications "${WORK_DIR}/dmg_stage/Applications"
DMG_OUT="${OUTPUT_DIR}/ModernExplorer-Setup-${RID}.dmg"
hdiutil create \
  -volname "Modern Explorer ${VERSION}" \
  -srcfolder "${WORK_DIR}/dmg_stage" \
  -size 220m \
  -ov \
  -format UDZO \
  "${DMG_OUT}"
echo "Built macOS .dmg installer: ${DMG_OUT}"

