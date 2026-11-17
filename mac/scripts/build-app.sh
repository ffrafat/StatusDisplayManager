#!/bin/bash
# Builds "AX206 Display.app" into mac/dist/.
#   mac/scripts/build-app.sh            build only
#   mac/scripts/build-app.sh --install  also copy it to /Applications
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="1.0.0"
APP="dist/AX206 Display.app"

swift build -c release --product AX206Display
BIN="$(swift build -c release --show-bin-path)/AX206Display"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/AX206Display"
sed "s/__VERSION__/$VERSION/g" Resources/Info.plist > "$APP/Contents/Info.plist"

# App icon from the shared 1024px PNG.
ICONSET="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size Resources/AppIcon.png --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) Resources/AppIcon.png --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

codesign --force --sign - "$APP"
echo "Built $APP"

if [[ "${1:-}" == "--install" ]]; then
  pkill -x AX206Display 2>/dev/null || true
  rm -rf "/Applications/AX206 Display.app"
  cp -R "$APP" /Applications/
  echo "Installed to /Applications/AX206 Display.app"
fi
