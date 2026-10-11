#!/usr/bin/env bash
# Builds c2flux.app and a .dmg from a published osx-* folder.
#
#   packaging/package-macos.sh <publish-dir> <version> <arch> <out-dir>
#
# The bundle is signed ad hoc, not notarized (ROADMAP, section 9): the first
# start needs right click > Open, as the README explains.
set -euo pipefail

publish="$1"; version="$2"; arch="$3"; out="$4"
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
app="$work/c2flux.app"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$out"
cp -R "$publish/." "$app/Contents/MacOS/"
sed "s/@VERSION@/$version/g" "$here/macos/Info.plist" > "$app/Contents/Info.plist"

# The icon set from the 502 px PNG.
iconset="$work/c2flux.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z $size $size "$here/../src/c2flux.WinForms/Ressources/c2flux.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z $double $double "$here/../src/c2flux.WinForms/Ressources/c2flux.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/c2flux.icns"

codesign --force --deep --sign - "$app"

# The disk image: the app and a link to Applications.
staging="$work/dmg"
mkdir -p "$staging"
mv "$app" "$staging/"
ln -s /Applications "$staging/Applications"
hdiutil create -volname "c2flux" -srcfolder "$staging" -ov -format UDZO "$out/c2flux-v$version-macos-$arch.dmg" >/dev/null
rm -rf "$work"
echo "$out/c2flux-v$version-macos-$arch.dmg"
