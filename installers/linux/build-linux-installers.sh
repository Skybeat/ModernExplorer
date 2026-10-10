#!/usr/bin/env bash
set -euo pipefail

# Builds Linux native installers (.deb package and universal self-extracting .sh installer)
# Usage: ./build-linux-installers.sh <rid> <version> <publish_dir> <icon_png> <output_dir>

RID="${1:-linux-x64}"
RAW_VERSION="${2:-1.0.0}"
VERSION="${RAW_VERSION#v}"
PUBLISH_DIR="${3}"
ICON_PNG="${4}"
OUTPUT_DIR="${5}"

mkdir -p "${OUTPUT_DIR}"

if [[ "${RID}" == "linux-arm64" ]]; then
  DEB_ARCH="arm64"
else
  DEB_ARCH="amd64"
fi

 WORK_DIR="$(mktemp -d)"
 trap 'rm -rf "${WORK_DIR}"' EXIT

# ---------------------------------------------------------------------------
# 1. Build Debian / Ubuntu .deb Installer Package
# ---------------------------------------------------------------------------
DEB_ROOT="${WORK_DIR}/deb_pkg"
mkdir -p "${DEB_ROOT}/DEBIAN"
mkdir -p "${DEB_ROOT}/opt/modernexplorer"
mkdir -p "${DEB_ROOT}/usr/bin"
mkdir -p "${DEB_ROOT}/usr/share/applications"
mkdir -p "${DEB_ROOT}/usr/share/icons/hicolor/256x256/apps"

cp -a "${PUBLISH_DIR}/." "${DEB_ROOT}/opt/modernexplorer/"
chmod 755 "${DEB_ROOT}/opt/modernexplorer/ModernExplorer"
ln -sf /opt/modernexplorer/ModernExplorer "${DEB_ROOT}/usr/bin/modernexplorer"
cp "${ICON_PNG}" "${DEB_ROOT}/usr/share/icons/hicolor/256x256/apps/modernexplorer.png"

cat > "${DEB_ROOT}/usr/share/applications/modernexplorer.desktop" << 'EOF'
[Desktop Entry]
Version=1.0
Type=Application
Name=Modern Explorer
Comment=Fast, keyboard-centric dual-pane file manager
Exec=/opt/modernexplorer/ModernExplorer %F
Icon=modernexplorer
Terminal=false
Categories=Utility;FileTools;FileManager;System;
StartupWMClass=ModernExplorer
EOF

INSTALLED_KB="$(du -sk "${DEB_ROOT}/opt/modernexplorer" | cut -f1)"

cat > "${DEB_ROOT}/DEBIAN/control" << EOF
Package: modernexplorer
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${DEB_ARCH}
Installed-Size: ${INSTALLED_KB}
Maintainer: Skybeat <Skybeat@users.noreply.github.com>
Homepage: https://github.com/Skybeat/ModernExplorer
Description: Fast, keyboard-centric dual-pane file manager for Linux, macOS, and Windows
 Modern Explorer is a keyboard-first dual-pane file manager built with .NET 9
 featuring tabs, intelligent Quick Viewer (F3), internal Editor (F4),
 multi-language support, and automatic GitHub Release updates.
EOF

chmod 755 "${DEB_ROOT}/DEBIAN"
chmod 644 "${DEB_ROOT}/DEBIAN/control"

DEB_OUT="${OUTPUT_DIR}/ModernExplorer-Setup-${RID}.deb"
dpkg-deb --build --root-owner-group "${DEB_ROOT}" "${DEB_OUT}"
echo "Built Debian package: ${DEB_OUT}"

# ---------------------------------------------------------------------------
# 2. Build Universal Self-Extracting .sh Installer (Fedora, Arch, openSUSE, Ubuntu, etc.)
# ---------------------------------------------------------------------------
TAR_PAYLOAD="${WORK_DIR}/payload.tar.gz"
tar -C "${PUBLISH_DIR}" -czf "${TAR_PAYLOAD}" .

SH_OUT="${OUTPUT_DIR}/ModernExplorer-Installer-${RID}.sh"
cat > "${SH_OUT}" << 'HEADER_EOF'
#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${HOME}/.local/opt/ModernExplorer"
BIN_DIR="${HOME}/.local/bin"
DESKTOP_DIR="${HOME}/.local/share/applications"
ICON_DIR="${HOME}/.local/share/icons/hicolor/256x256/apps"

echo "Installing Modern Explorer to ${APP_DIR}..."
mkdir -p "${APP_DIR}" "${BIN_DIR}" "${DESKTOP_DIR}" "${ICON_DIR}"

ARCHIVE_LINE="$(awk '/^__PAYLOAD_BELOW__/ {print NR + 1; exit 0; }' "$0")"
tail -n +"${ARCHIVE_LINE}" "$0" | tar -xz -C "${APP_DIR}"

chmod +x "${APP_DIR}/ModernExplorer"
ln -sf "${APP_DIR}/ModernExplorer" "${BIN_DIR}/modernexplorer"

if [[ -f "${APP_DIR}/Assets/AppIcon.png" ]]; then
  cp "${APP_DIR}/Assets/AppIcon.png" "${ICON_DIR}/modernexplorer.png"
fi

cat > "${DESKTOP_DIR}/modernexplorer.desktop" << EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Modern Explorer
Comment=Fast, keyboard-centric dual-pane file manager
Exec=${APP_DIR}/ModernExplorer %F
Icon=modernexplorer
Terminal=false
Categories=Utility;FileTools;FileManager;System;
EOF

echo "Modern Explorer installed successfully! Run '${BIN_DIR}/modernexplorer' or launch from your application menu."
exit 0
__PAYLOAD_BELOW__
HEADER_EOF

cat "${TAR_PAYLOAD}" >> "${SH_OUT}"
chmod +x "${SH_OUT}"
echo "Built universal Linux installer: ${SH_OUT}"
