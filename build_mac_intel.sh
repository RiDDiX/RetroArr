#!/bin/bash
set -e

echo "=================================="
echo "RetroArr Build (macOS Intel x64)"
echo "=================================="

# 1. Build Frontend
echo "[1/3] Building Frontend..."
npm run build

# 2. Publish Backend for macOS Intel
echo "[2/3] Publishing Backend (OSX-X64)..."
dotnet publish src/RetroArr.Host/RetroArr.Host.csproj \
    -c Release \
    -r osx-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o build_artifacts/osx-x64

# 3. Create .app Bundle
echo "[3/3] Packaging .app..."
APP_NAME="build_artifacts/RetroArr"
rm -rf "${APP_NAME}.app"
mkdir -p "${APP_NAME}.app/Contents/MacOS"
mkdir -p "${APP_NAME}.app/Contents/Resources"

# Create Icon
ICON_SRC="frontend/src/assets/app_logo.png"
if [ -f "$ICON_SRC" ] && command -v iconutil >/dev/null; then
    echo "Generating .icns icon..."
    ICONSET_DIR="MyIcon.iconset"
    mkdir -p "$ICONSET_DIR"
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
    iconutil -c icns "$ICONSET_DIR" -o "${APP_NAME}.app/Contents/Resources/AppIcon.icns"
    rm -rf "$ICONSET_DIR"
fi

# Copy Executable & Core Files
# Since it's SingleFile, we only need the main executable and any native libs that didn't bundle
cp -a build_artifacts/osx-x64/* "${APP_NAME}.app/Contents/MacOS/"
chmod +x "${APP_NAME}.app/Contents/MacOS/RetroArr.Host"

# Copy UI Assets - Photino needs them relative to the exe
mkdir -p "${APP_NAME}.app/Contents/MacOS/_output/UI"
cp -a _output/UI/* "${APP_NAME}.app/Contents/MacOS/_output/UI/" 2>/dev/null || true

# Create Info.plist
cat > "${APP_NAME}.app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>RetroArr</string>
    <key>CFBundleDisplayName</key>
    <string>RetroArr</string>
    <key>CFBundleIdentifier</key>
    <string>app.RetroArr.desktop</string>
    <key>CFBundleVersion</key>
    <string>0.4.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleExecutable</key>
    <string>RetroArr.Host</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# Remove quarantine and sign
if command -v xattr >/dev/null; then
    echo "Removing quarantine attributes..."
    xattr -cr "${APP_NAME}.app"
fi

if command -v codesign >/dev/null; then
    echo "Ad-hoc signing the app..."
    # Sign all dylibs first, then the app
    find "${APP_NAME}.app/Contents/MacOS" -name "*.dylib" -exec codesign --force --sign - {} \;
    codesign --force --deep --sign - "${APP_NAME}.app"
fi

# Create DMG (if hdiutil is available)
if command -v hdiutil >/dev/null; then
    echo "Creating .dmg for Intel..."
    rm -f "build_artifacts/RetroArr-Intel.dmg"
    
    # Create a temporary folder for the DMG structure
    DMG_STAGING="build_artifacts/dmg_staging"
    rm -rf "$DMG_STAGING"
    mkdir -p "$DMG_STAGING"
    
    # Copy the .app and create a symlink to Applications
    cp -Rp "${APP_NAME}.app" "$DMG_STAGING/"
    ln -s /Applications "$DMG_STAGING/Applications"
    
    hdiutil create -volname "RetroArr Intel" -srcfolder "$DMG_STAGING" -ov -format UDZO "build_artifacts/RetroArr-Intel.dmg"
    
    # Cleanup staging
    rm -rf "$DMG_STAGING"
fi

# Create a PKG (Real Installer Wizard)
# Create a PKG (Real Installer Wizard) - DISABLED
# if command -v pkgbuild >/dev/null; then
#     echo "Creating .pkg installer for Intel..."
#     rm -f "build_artifacts/RetroArr-Intel.pkg"
#     pkgbuild --component "${APP_NAME}.app" \
#              --install-location /Applications \
#              --identifier "app.RetroArr.desktop.pkg" \
#              --version "0.1.2-beta" \
#              "build_artifacts/RetroArr-Intel.pkg"
#     
#     if command -v productsign >/dev/null && [ -n "$INSTALLER_CERT_NAME" ]; then
#         echo "Note: PKG is created but not signed with a developer cert (requires Apple Developer ID)."
#     fi
# fi

echo "=================================="
echo "Intel Build Complete!"
echo "Artifact: build_artifacts/RetroArr-Intel.dmg"
echo "=================================="
