#!/usr/bin/env bash
# Builds the AppImage from a published linux-* folder.
#
#   packaging/package-linux.sh <publish-dir> <version> <arch> <out-dir>
#
# arch is x86_64 or aarch64 (appimagetool's names). appimagetool is
# downloaded once and run without FUSE (extract and run).
set -euo pipefail

publish="$1"; version="$2"; arch="$3"; out="$4"
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
appdir="$work/c2flux.AppDir"

mkdir -p "$appdir/usr/bin" "$appdir/usr/share/metainfo" "$appdir/usr/share/icons/hicolor/256x256/apps" "$out"
cp -R "$publish/." "$appdir/usr/bin/"
cp "$here/linux/AppRun" "$appdir/AppRun"
chmod +x "$appdir/AppRun" "$appdir/usr/bin/c2flux"
cp "$here/linux/io.github.samueltm.c2flux.desktop" "$appdir/"
sed "s/@VERSION@/$version/g" "$here/linux/io.github.samueltm.c2flux.metainfo.xml" > "$appdir/usr/share/metainfo/io.github.samueltm.c2flux.metainfo.xml"
# 256 px icon, made from c2flux.png (sips -z 256 256).
cp "$here/linux/c2flux.png" "$appdir/c2flux.png"
cp "$appdir/c2flux.png" "$appdir/usr/share/icons/hicolor/256x256/apps/c2flux.png"
ln -s c2flux.png "$appdir/.DirIcon"

tool="$work/appimagetool"
curl -fsSL -o "$tool" "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$arch.AppImage"
chmod +x "$tool"
APPIMAGE_EXTRACT_AND_RUN=1 ARCH="$arch" "$tool" "$appdir" "$out/c2flux-v$version-linux-$arch.AppImage"
rm -rf "$work"
echo "$out/c2flux-v$version-linux-$arch.AppImage"
