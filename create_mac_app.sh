#!/bin/bash
set -e

APP_NAME="RetroArr"
ARCH="${1:-osx-arm64}" # Default to osx-arm64 if not provided
BUILD_DIR="build_artifacts/$ARCH"
OUTPUT_DIR="build_artifacts"
APP_BUNDLE="$OUTPUT_DIR/RetroArr-$ARCH.app"

# Ensure build exists
if [ ! -d "$BUILD_DIR" ]; then
    echo "Error: Build directory $BUILD_DIR does not exist. Run ./build_all.sh first."
    exit 1
fi

echo "Creating macOS Bundle: $APP_BUNDLE..."

# Create structure
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# Create Icon
ICON_SRC="frontend/src/assets/app_logo.png"
if [ -f "$ICON_SRC" ] && command -v iconutil >/dev/null; then
    echo "Generating .icns icon..."
    ICONSET_DIR="MyIcon.iconset"
    mkdir -p "$ICONSET_DIR"
    
    # Create required sizes from source image (now square and transparent)
    sips -z 16 16     "$ICON_SRC" --out "$ICONSET_DIR/icon_16x16.png" > /dev/null
    sips -z 32 32     "$ICON_SRC" --out "$ICONSET_DIR/icon_16x16@2x.png" > /dev/null
    sips -z 32 32     "$ICON_SRC" --out "$ICONSET_DIR/icon_32x32.png" > /dev/null
    sips -z 64 64     "$ICON_SRC" --out "$ICONSET_DIR/icon_32x32@2x.png" > /dev/null
    sips -z 128 128   "$ICON_SRC" --out "$ICONSET_DIR/icon_128x128.png" > /dev/null
    sips -z 256 256   "$ICON_SRC" --out "$ICONSET_DIR/icon_128x128@2x.png" > /dev/null
    sips -z 256 256   "$ICON_SRC" --out "$ICONSET_DIR/icon_256x256.png" > /dev/null
    sips -z 512 512   "$ICON_SRC" --out "$ICONSET_DIR/icon_256x256@2x.png" > /dev/null
    sips -z 512 512   "$ICON_SRC" --out "$ICONSET_DIR/icon_512x512.png" > /dev/null
    sips -z 1024 1024 "$ICON_SRC" --out "$ICONSET_DIR/icon_512x512@2x.png" > /dev/null
    
    iconutil -c icns "$ICONSET_DIR" -o "$APP_BUNDLE/Contents/Resources/AppIcon.icns"
    rm -rf "$ICONSET_DIR"
else
    echo "Warning: Icon source not found or iconutil missing. Skipping icon generation."
fi

# Create Info.plist
cat > "$APP_BUNDLE/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>app.RetroArr.desktop</string>
    <key>CFBundleVersion</key>
    <string>0.4.5</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>Launcher</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSUIElement</key>
    <false/>
</dict>
</plist>
EOF

# Create Launcher script
# This script ensures the binary runs with the correct working directory
cat > "$APP_BUNDLE/Contents/MacOS/Launcher" <<EOF
#!/bin/bash
DIR="\$( cd "\$( dirname "\${BASH_SOURCE[0]}" )" && pwd )"
# Execute RetroArr.Host which is located next to this script (we will copy files there)
cd "\$DIR"
./RetroArr.Host
EOF

chmod +x "$APP_BUNDLE/Contents/MacOS/Launcher"

# Copy all build artifacts into MacOS folder
echo "Copying application files..."
cp -R "$BUILD_DIR/"* "$APP_BUNDLE/Contents/MacOS/"

# Copy UI assets
echo "Copying UI assets..."
mkdir -p "$APP_BUNDLE/Contents/MacOS/_output/UI"
cp -a _output/UI/* "$APP_BUNDLE/Contents/MacOS/_output/UI/" 2>/dev/null || true

echo "Applying permissions and signing..."
chmod +x "$APP_BUNDLE/Contents/MacOS/Launcher"
chmod +x "$APP_BUNDLE/Contents/MacOS/RetroArr.Host"
chmod +x "$APP_BUNDLE/Contents/MacOS/Photino.Native.dylib"

# Remove quarantine attributes to avoid "App is damaged" errors
xattr -cr "$APP_BUNDLE"

# Ad-hoc signing to satisfy Gatekeeper requirements
if command -v codesign >/dev/null; then
    echo "Ad-hoc signing the app bundle..."
    codesign --force --deep --sign - "$APP_BUNDLE"
fi

echo "=================================="
echo "Bundle Created Successfully!"
echo "Location: $APP_BUNDLE"
echo "=================================="

# Create DMG
DMG_NAME="$OUTPUT_DIR/RetroArr-$ARCH.dmg"
VOL_NAME="RetroArr Installer ($ARCH)"

echo "Creating DMG Installer: $DMG_NAME..."
rm -f "$DMG_NAME"

# Create a temporary folder for DMG content
DMG_TMP="_dmg_tmp"
rm -rf "$DMG_TMP"
mkdir -p "$DMG_TMP"

# Copy App to tmp
cp -R "$APP_BUNDLE" "$DMG_TMP/"

# Create Link to Applications
ln -s /Applications "$DMG_TMP/Applications"

# Create DMG
hdiutil create -volname "$VOL_NAME" -srcfolder "$DMG_TMP" -ov -format UDZO "$DMG_NAME"

# Clean up
rm -rf "$DMG_TMP"

echo "=================================="
echo "DMG Created Successfully!"
echo "File: $DMG_NAME"
echo "=================================="
