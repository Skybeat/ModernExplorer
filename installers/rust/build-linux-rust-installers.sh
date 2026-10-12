#!/usr/bin/env bash
set -euo pipefail

# Builds Linux Rust Portable packages (.tar.gz, .zip, .AppImage) and Native Installers
# for the most popular Linux distributions:
#   - Debian / Ubuntu / Linux Mint / Pop!_OS / Kali (.deb)
#   - Fedora / RHEL / CentOS / AlmaLinux / Rocky / openSUSE (.rpm)
#   - Arch Linux / Manjaro / EndeavourOS (.pkg.tar.zst)
#   - Universal Linux Self-Extracting Installer (.sh)
# Usage: ./build-linux-rust-installers.sh <arch_id> <version> <binary_path> <icon_png> <output_dir>

ARCH_ID="${1:-linux-x64}"
RAW_VERSION="${2:-1.9.0}"
VERSION="${RAW_VERSION#v}"
BINARY_PATH="${3}"
ICON_PNG="${4}"
OUTPUT_DIR="${5}"

mkdir -p "${OUTPUT_DIR}"

if [[ "${ARCH_ID}" == "linux-arm64" ]]; then
  DEB_ARCH="arm64"
  RPM_ARCH="aarch64"
  ARCH_PKG_ARCH="aarch64"
else
  DEB_ARCH="amd64"
  RPM_ARCH="x86_64"
  ARCH_PKG_ARCH="x86_64"
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

# ---------------------------------------------------------------------------
# 1. Build Linux Portable Packages (.tar.gz & .zip & Portable .AppImage bundle)
# ---------------------------------------------------------------------------
PORTABLE_DIR="${WORK_DIR}/ModernExplorer-Rust-${ARCH_ID}-Portable"
mkdir -p "${PORTABLE_DIR}/Assets"
cp "${BINARY_PATH}" "${PORTABLE_DIR}/ModernExplorer-Rust"
chmod 755 "${PORTABLE_DIR}/ModernExplorer-Rust"
cp "${ICON_PNG}" "${PORTABLE_DIR}/Assets/AppIcon.png"

cat > "${PORTABLE_DIR}/run-portable.sh" << 'EOF'
#!/usr/bin/env bash
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export GDK_BACKEND="${GDK_BACKEND:-x11}"
exec "${HERE}/ModernExplorer-Rust" "$@"
EOF
chmod 755 "${PORTABLE_DIR}/run-portable.sh"

cat > "${PORTABLE_DIR}/README-Portable.txt" << EOF
ModernExplorer Studio v${VERSION} — Rust Edition (${ARCH_ID} Portable)
======================================================================
- Run directly: ./run-portable.sh  or  ./ModernExplorer-Rust
- Settings & workspaces are stored in ~/.config/ModernExplorer
- Requirements on Linux: WebKitGTK 4.1 (libwebkit2gtk-4.1-0) and GTK 3
EOF

PORTABLE_TAR="${OUTPUT_DIR}/ModernExplorer-Rust-${ARCH_ID}-Portable.tar.gz"
tar -C "${WORK_DIR}" -czf "${PORTABLE_TAR}" "ModernExplorer-Rust-${ARCH_ID}-Portable"
echo "Built Linux Portable TAR.GZ: ${PORTABLE_TAR}"

PORTABLE_ZIP="${OUTPUT_DIR}/ModernExplorer-Rust-${ARCH_ID}-Portable.zip"
(cd "${WORK_DIR}" && zip -r "${PORTABLE_ZIP}" "ModernExplorer-Rust-${ARCH_ID}-Portable")
echo "Built Linux Portable ZIP: ${PORTABLE_ZIP}"

# Build Portable Single-File Self-Mounting .AppImage (runs without FUSE as well)
APPIMAGE_OUT="${OUTPUT_DIR}/ModernExplorer-Rust-${ARCH_ID}-Portable.AppImage"
APPDIR_TAR="${WORK_DIR}/appdir.tar.gz"
tar -C "${PORTABLE_DIR}" -czf "${APPDIR_TAR}" .
cat > "${APPIMAGE_OUT}" << 'APPIMAGE_EOF'
#!/usr/bin/env bash
set -euo pipefail
TMP_RUN="$(mktemp -d /tmp/ModernExplorer-Rust-AppImage.XXXXXX)"
trap 'rm -rf "${TMP_RUN}"' EXIT
ARCHIVE_LINE="$(awk '/^__APPIMAGE_PAYLOAD__/ {print NR + 1; exit 0; }' "$0")"
tail -n +"${ARCHIVE_LINE}" "$0" | tar -xz -C "${TMP_RUN}"
export GDK_BACKEND="${GDK_BACKEND:-x11}"
"${TMP_RUN}/ModernExplorer-Rust" "$@"
exit $?
__APPIMAGE_PAYLOAD__
APPIMAGE_EOF
cat "${APPDIR_TAR}" >> "${APPIMAGE_OUT}"
chmod 755 "${APPIMAGE_OUT}"
echo "Built Linux Portable AppImage: ${APPIMAGE_OUT}"

# ---------------------------------------------------------------------------
# 2. Build Debian / Ubuntu / Mint / Pop!_OS .deb Installer Package
# ---------------------------------------------------------------------------
DEB_ROOT="${WORK_DIR}/deb_pkg"
mkdir -p "${DEB_ROOT}/DEBIAN"
mkdir -p "${DEB_ROOT}/opt/modernexplorer-rust/Assets"
mkdir -p "${DEB_ROOT}/usr/bin"
mkdir -p "${DEB_ROOT}/usr/share/applications"
mkdir -p "${DEB_ROOT}/usr/share/icons/hicolor/256x256/apps"

cp "${BINARY_PATH}" "${DEB_ROOT}/opt/modernexplorer-rust/ModernExplorer-Rust"
chmod 755 "${DEB_ROOT}/opt/modernexplorer-rust/ModernExplorer-Rust"
cp "${ICON_PNG}" "${DEB_ROOT}/opt/modernexplorer-rust/Assets/AppIcon.png"
cp "${ICON_PNG}" "${DEB_ROOT}/usr/share/icons/hicolor/256x256/apps/modernexplorer-rust.png"
ln -sf /opt/modernexplorer-rust/ModernExplorer-Rust "${DEB_ROOT}/usr/bin/modernexplorer-rust"

cat > "${DEB_ROOT}/usr/share/applications/modernexplorer-rust.desktop" << 'EOF'
[Desktop Entry]
Version=1.0
Type=Application
Name=Modern Explorer Studio (Rust)
Comment=Fast, keyboard-centric dual-pane file manager (Rust Studio Edition)
Exec=/opt/modernexplorer-rust/ModernExplorer-Rust %F
Icon=modernexplorer-rust
Terminal=false
Categories=Utility;FileTools;FileManager;System;
StartupWMClass=ModernExplorer-Rust
EOF

INSTALLED_KB="$(du -sk "${DEB_ROOT}/opt/modernexplorer-rust" | cut -f1)"

cat > "${DEB_ROOT}/DEBIAN/control" << EOF
Package: modernexplorer-rust
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: ${DEB_ARCH}
Installed-Size: ${INSTALLED_KB}
Recommends: libwebkit2gtk-4.1-0 | libwebkit2gtk-4.0-37, libgtk-3-0
Maintainer: Skybeat <Skybeat@users.noreply.github.com>
Homepage: https://github.com/Skybeat/ModernExplorer
Description: Modern Explorer Studio (Rust Edition) - Dual-Pane File Manager
 Fast, keyboard-first dual-pane file manager written in Rust with Studio HTML/CSS UI,
 4 themes, 20 power tools (Diff, Checksums, Split/Shred, Treemap, Batch Rename, Git, Sync),
 and automatic GitHub Release update checking.
EOF

chmod 755 "${DEB_ROOT}/DEBIAN"
chmod 644 "${DEB_ROOT}/DEBIAN/control"

DEB_OUT="${OUTPUT_DIR}/ModernExplorer-Rust-Setup-${ARCH_ID}.deb"
dpkg-deb --build --root-owner-group "${DEB_ROOT}" "${DEB_OUT}"
echo "Built Debian/Ubuntu .deb package: ${DEB_OUT}"

# ---------------------------------------------------------------------------
# 3. Build Fedora / RHEL / openSUSE .rpm Installer Package (if rpmbuild available)
# ---------------------------------------------------------------------------
if command -v rpmbuild >/dev/null 2>&1; then
  RPM_TOP="${WORK_DIR}/rpmbuild"
  mkdir -p "${RPM_TOP}"/{BUILD,RPMS,SOURCES,SPECS,SRPMS,BUILDROOT}
  SPEC_FILE="${RPM_TOP}/SPECS/modernexplorer-rust.spec"

  cat > "${SPEC_FILE}" << EOF
Name:           modernexplorer-rust
Version:        ${VERSION}
Release:        1
Summary:        Modern Explorer Studio (Rust Edition) - Dual-Pane File Manager
License:        MIT
URL:            https://github.com/Skybeat/ModernExplorer
BuildArch:      ${RPM_ARCH}
AutoReqProv:    no

%description
Modern Explorer Studio (Rust Edition) is a fast, keyboard-centric dual-pane file manager
featuring tabs, Quick Viewer (F3), internal Editor (F4), Squarified Treemap, Side-by-Side Diff,
Checksums, File Splitter/Shredder, Git integration, and 4 Studio themes.

%install
mkdir -p %{buildroot}/opt/modernexplorer-rust/Assets
mkdir -p %{buildroot}/usr/bin
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons/hicolor/256x256/apps
cp "${BINARY_PATH}" %{buildroot}/opt/modernexplorer-rust/ModernExplorer-Rust
chmod 755 %{buildroot}/opt/modernexplorer-rust/ModernExplorer-Rust
cp "${ICON_PNG}" %{buildroot}/opt/modernexplorer-rust/Assets/AppIcon.png
cp "${ICON_PNG}" %{buildroot}/usr/share/icons/hicolor/256x256/apps/modernexplorer-rust.png
ln -sf /opt/modernexplorer-rust/ModernExplorer-Rust %{buildroot}/usr/bin/modernexplorer-rust
cp "${DEB_ROOT}/usr/share/applications/modernexplorer-rust.desktop" %{buildroot}/usr/share/applications/modernexplorer-rust.desktop

%files
/opt/modernexplorer-rust/ModernExplorer-Rust
/opt/modernexplorer-rust/Assets/AppIcon.png
/usr/bin/modernexplorer-rust
/usr/share/applications/modernexplorer-rust.desktop
/usr/share/icons/hicolor/256x256/apps/modernexplorer-rust.png
EOF

  rpmbuild --define "_topdir ${RPM_TOP}" --target "${RPM_ARCH}" -bb "${SPEC_FILE}"
  FOUND_RPM="$(find "${RPM_TOP}/RPMS" -name "*.rpm" | head -n 1)"
  if [[ -n "${FOUND_RPM}" && -f "${FOUND_RPM}" ]]; then
    RPM_OUT="${OUTPUT_DIR}/ModernExplorer-Rust-Setup-${ARCH_ID}.rpm"
    cp "${FOUND_RPM}" "${RPM_OUT}"
    echo "Built Fedora/RHEL/openSUSE .rpm package: ${RPM_OUT}"
  fi
fi

# ---------------------------------------------------------------------------
# 4. Build Arch Linux / Manjaro / EndeavourOS Package (.pkg.tar.zst or .pkg.tar.gz)
# ---------------------------------------------------------------------------
ARCH_ROOT="${WORK_DIR}/arch_pkg"
mkdir -p "${ARCH_ROOT}/opt/modernexplorer-rust/Assets"
mkdir -p "${ARCH_ROOT}/usr/bin"
mkdir -p "${ARCH_ROOT}/usr/share/applications"
mkdir -p "${ARCH_ROOT}/usr/share/icons/hicolor/256x256/apps"

cp "${BINARY_PATH}" "${ARCH_ROOT}/opt/modernexplorer-rust/ModernExplorer-Rust"
chmod 755 "${ARCH_ROOT}/opt/modernexplorer-rust/ModernExplorer-Rust"
cp "${ICON_PNG}" "${ARCH_ROOT}/opt/modernexplorer-rust/Assets/AppIcon.png"
cp "${ICON_PNG}" "${ARCH_ROOT}/usr/share/icons/hicolor/256x256/apps/modernexplorer-rust.png"
ln -sf /opt/modernexplorer-rust/ModernExplorer-Rust "${ARCH_ROOT}/usr/bin/modernexplorer-rust"
cp "${DEB_ROOT}/usr/share/applications/modernexplorer-rust.desktop" "${ARCH_ROOT}/usr/share/applications/modernexplorer-rust.desktop"

INSTALLED_BYTES="$(( INSTALLED_KB * 1024 ))"
cat > "${ARCH_ROOT}/.PKGINFO" << EOF
pkgname = modernexplorer-rust
pkgbase = modernexplorer-rust
pkgver = ${VERSION}-1
pkgdesc = Modern Explorer Studio (Rust Edition) - Fast Dual-Pane File Manager
url = https://github.com/Skybeat/ModernExplorer
builddate = $(date +%s)
packager = Skybeat <Skybeat@users.noreply.github.com>
size = ${INSTALLED_BYTES}
arch = ${ARCH_PKG_ARCH}
license = MIT
depend = webkit2gtk-4.1
depend = gtk3
EOF

if command -v zstd >/dev/null 2>&1; then
  ARCH_OUT="${OUTPUT_DIR}/ModernExplorer-Rust-Setup-archlinux-${ARCH_ID}.pkg.tar.zst"
  tar -C "${ARCH_ROOT}" --zstd -cf "${ARCH_OUT}" .PKGINFO opt usr
  echo "Built Arch Linux / Manjaro package: ${ARCH_OUT}"
fi

# ---------------------------------------------------------------------------
# 5. Build Universal Linux Self-Extracting .sh Installer (All Distros)
# ---------------------------------------------------------------------------
SH_OUT="${OUTPUT_DIR}/ModernExplorer-Rust-Installer-${ARCH_ID}.sh"
cat > "${SH_OUT}" << 'HEADER_EOF'
#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${HOME}/.local/opt/ModernExplorer-Rust"
BIN_DIR="${HOME}/.local/bin"
DESKTOP_DIR="${HOME}/.local/share/applications"
ICON_DIR="${HOME}/.local/share/icons/hicolor/256x256/apps"

echo "Installing Modern Explorer Studio (Rust Edition) to ${APP_DIR}..."
mkdir -p "${APP_DIR}" "${BIN_DIR}" "${DESKTOP_DIR}" "${ICON_DIR}"

ARCHIVE_LINE="$(awk '/^__PAYLOAD_BELOW__/ {print NR + 1; exit 0; }' "$0")"
tail -n +"${ARCHIVE_LINE}" "$0" | tar -xz -C "${APP_DIR}"

chmod +x "${APP_DIR}/ModernExplorer-Rust"
ln -sf "${APP_DIR}/ModernExplorer-Rust" "${BIN_DIR}/modernexplorer-rust"

if [[ -f "${APP_DIR}/Assets/AppIcon.png" ]]; then
  cp "${APP_DIR}/Assets/AppIcon.png" "${ICON_DIR}/modernexplorer-rust.png"
fi

cat > "${DESKTOP_DIR}/modernexplorer-rust.desktop" << EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Modern Explorer Studio (Rust)
Comment=Fast, keyboard-centric dual-pane file manager (Rust Studio Edition)
Exec=${APP_DIR}/ModernExplorer-Rust %F
Icon=modernexplorer-rust
Terminal=false
Categories=Utility;FileTools;FileManager;System;
EOF

echo "Modern Explorer Studio (Rust) installed successfully!"
echo "Run '${BIN_DIR}/modernexplorer-rust' or launch from your application menu."
exit 0
__PAYLOAD_BELOW__
HEADER_EOF

cat "${APPDIR_TAR}" >> "${SH_OUT}"
chmod +x "${SH_OUT}"
echo "Built Universal Linux .sh Installer: ${SH_OUT}"

