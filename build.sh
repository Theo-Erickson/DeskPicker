#!/bin/sh
# Builds DeskPicker for macOS or Linux into dist/: one file, nothing to install.
# Needs the .NET 10 SDK (dot.net).
#   ./build.sh                 this machine (osx-arm64, osx-x64, linux-x64 or linux-arm64)
#   ./build.sh linux-arm64     another target
# On macOS the result is dist/<target>/DeskPicker.app (ad-hoc signed); elsewhere it's dist/<target>/DeskPicker.
set -e
cd "$(dirname "$0")"

RID="$1"
if [ -z "$RID" ]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) RID=osx-arm64 ;;
    Darwin-*) RID=osx-x64 ;;
    Linux-aarch64|Linux-arm64) RID=linux-arm64 ;;
    *) RID=linux-x64 ;;
  esac
fi

OUT="dist/$RID"
rm -rf "$OUT"
dotnet publish src/DeskPicker.csproj -c Release -r "$RID" -o "$OUT" -nologo
chmod +x "$OUT/DeskPicker"

case "$RID" in
  osx-*)
    # Wrap it in an app bundle so it opens from Finder and the Dock.
    VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/DeskPicker.csproj)
    APP="$OUT/DeskPicker.app"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
    mv "$OUT/DeskPicker" "$APP/Contents/MacOS/DeskPicker"
    cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>DeskPicker</string>
  <key>CFBundleDisplayName</key><string>DeskPicker</string>
  <key>CFBundleIdentifier</key><string>io.github.theo-erickson.deskpicker</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>DeskPicker</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <!-- So DeskPicker can catch GitHub's sign-in link while a profile waits for it. -->
  <key>CFBundleURLTypes</key>
  <array><dict>
    <key>CFBundleURLName</key><string>GitHub Desktop sign-in</string>
    <key>CFBundleURLSchemes</key><array><string>x-github-desktop-auth</string></array>
  </dict></array>
</dict>
</plist>
EOF
    # Ad-hoc signature: unsigned Apple-silicon apps don't run at all.
    command -v codesign >/dev/null && codesign --force --deep --sign - "$APP"
    echo "Built $APP"
    ;;
  *)
    echo "Built $OUT/DeskPicker"
    ;;
esac
