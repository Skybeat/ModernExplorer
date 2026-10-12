#!/usr/bin/env bash
set -euo pipefail

# Builds macOS Rust Portable bundles (.zip, .tar.gz) and Installers (.dmg, .pkg)
# for Intel (macos-x64 / osx-x64) and Apple Silicon (macos-arm64 / osx-arm64)
# Usage: ./build-macos-rust-installers.sh <arch_id> <version> <binary_path> <icon_png> <output_dir>

ARCH_ID="${1:-macos-arm64}"
RAW_VERSION="${2:-1.9.0}"
VERSION="${RAW_VERSION#v}"
BINARY_PATH="${3}"
ICON_PNG="${4}"
OUTPUT_DIR="${5}"

mkdir -p "${OUTPUT_DIR}"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

STAGE_DIR="${WORK_DIR}/stage"
APP_BUNDLE="${STAGE_DIR}/ModernExplorer-Rust.app"
mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

cp "${BINARY_PATH}" "${APP_BUNDLE}/Contents/MacOS/ModernExplorer-Rust"
chmod +x "${APP_BUNDLE}/Contents/MacOS/ModernExplorer-Rust"
cp "${ICON_PNG}" "${APP_BUNDLE}/Contents/Resources/AppIcon.png"

cat > "${APP_BUNDLE}/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>ModernExplorer Studio (Rust)</string>
    <key>CFBundleDisplayName</key>
    <string>ModernExplorer Studio (Rust)</string>
    <key>CFBundleIdentifier</key>
    <string>com.skybeat.modernexplorer.rust</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>ModernExplorer-Rust</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon.png</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
</dict>
</plist>
EOF

# Ad-hoc code sign the .app bundle so macOS Gatekeeper / Apple Silicon executes it cleanly
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "${APP_BUNDLE}" || true
fi

# ---------------------------------------------------------------------------
# 1. Build macOS Portable Packages (.zip and .tar.gz)
# ---------------------------------------------------------------------------
PORTABLE_DIR="${WORK_DIR}/portable"
mkdir -p "${PORTABLE_DIR}"
cp -a "${APP_BUNDLE}" "${PORTABLE_DIR}/"
cp "${BINARY_PATH}" "${PORTABLE_DIR}/ModernExplorer-Rust"
chmod +x "${PORTABLE_DIR}/ModernExplorer-Rust"

cat > "${PORTABLE_DIR}/README-Portable.txt" << EOF
ModernExplorer Studio v${VERSION} — Rust Edition (${ARCH_ID} Portable)
======================================================================
- Start via Finder: Double-click "ModernExplorer-Rust.app"
  (Or drag ModernExplorer-Rust.app anywhere you like — no installation required)
- Start via Terminal: ./ModernExplorer-Rust
- If macOS shows an unverified developer prompt on first launch:
  Run: xattr -cr ModernExplorer-Rust.app
EOF

PORTABLE_ZIP="${OUTPUT_DIR}/ModernExplorer-Rust-${ARCH_ID}-Portable.zip"
(cd "${PORTABLE_DIR}" && zip -r -y "${PORTABLE_ZIP}" .)
echo "Built macOS Portable ZIP: ${PORTABLE_ZIP}"

PORTABLE_TAR="${OUTPUT_DIR}/ModernExplorer-Rust-${ARCH_ID}-Portable.tar.gz"
tar -C "${PORTABLE_DIR}" -czf "${PORTABLE_TAR}" .
echo "Built macOS Portable TAR.GZ: ${PORTABLE_TAR}"

# ---------------------------------------------------------------------------
# 2. Build macOS Native .pkg Installer
# ---------------------------------------------------------------------------
PKG_OUT="${OUTPUT_DIR}/ModernExplorer-Rust-Setup-${ARCH_ID}.pkg"
pkgbuild \
  --component "${APP_BUNDLE}" \
  --install-location "/Applications" \
  --identifier "com.skybeat.modernexplorer.rust.pkg" \
  --version "${VERSION}" \
  "${PKG_OUT}"
echo "Built macOS .pkg Installer: ${PKG_OUT}"

# ---------------------------------------------------------------------------
# 3. Build macOS Native .dmg Disk Image Installer
# ---------------------------------------------------------------------------
DMG_STAGE="${WORK_DIR}/dmg_stage"
mkdir -p "${DMG_STAGE}"
cp -a "${APP_BUNDLE}" "${DMG_STAGE}/"
ln -s /Applications "${DMG_STAGE}/Applications"

DMG_OUT="${OUTPUT_DIR}/ModernExplorer-Rust-Setup-${ARCH_ID}.dmg"
hdiutil create \
  -volname "ModernExplorer Studio Rust ${VERSION}" \
  -srcfolder "${DMG_STAGE}" \
  -size 64m \
  -ov \
  -format UDZO \
  "${DMG_OUT}"
echo "Built macOS .dmg Installer: ${DMG_OUT}"

