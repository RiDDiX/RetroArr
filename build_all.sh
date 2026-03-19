#!/bin/bash
set -e

# Define version
VERSION="0.4.5"
OUTPUT_DIR="build_artifacts"

echo "=================================="
echo "RetroArr Build Script v$VERSION"
echo "=================================="

# 1. Build Frontend
echo "[1/2] Building Frontend..."
npm install
npm run build

# 2. Build Backend for multiple targets
echo "[2/2] Building Backend executables..."

mkdir -p $OUTPUT_DIR

# Windows x64
echo " -> Building for Windows (x64)..."
dotnet publish src/RetroArr.Host/RetroArr.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OUTPUT_DIR/win-x64
echo "    Copying assets..."
mkdir -p $OUTPUT_DIR/win-x64/_output/UI
cp -R _output/UI/* $OUTPUT_DIR/win-x64/_output/UI/
cp src/RetroArr.Host/appsettings.json $OUTPUT_DIR/win-x64/
# Ensure no personal configs are included
rm -f $OUTPUT_DIR/win-x64/config/*.json
rm -f $OUTPUT_DIR/win-x64/settings/*.json


echo "    Packaging Windows..."
cd $OUTPUT_DIR
zip -r -q "RetroArr-Windows-x64.zip" "win-x64"
cd ..

# Linux x64
echo " -> Building for Linux (x64)..."
dotnet publish src/RetroArr.Host/RetroArr.Host.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o $OUTPUT_DIR/linux-x64
echo "    Copying assets..."
mkdir -p $OUTPUT_DIR/linux-x64/_output/UI
cp -R _output/UI/* $OUTPUT_DIR/linux-x64/_output/UI/
cp src/RetroArr.Host/appsettings.json $OUTPUT_DIR/linux-x64/
# Ensure no personal configs are included
rm -f $OUTPUT_DIR/linux-x64/config/*.json
rm -f $OUTPUT_DIR/linux-x64/settings/*.json

echo "    Packaging Linux..."
# Create Launcher for Linux
cat > $OUTPUT_DIR/linux-x64/RetroArr <<EOF
#!/bin/bash
DIR="\$( cd "\$( dirname "\${BASH_SOURCE[0]}" )" && pwd )"
cd "\$DIR"
./RetroArr.Host "\$@"
EOF
chmod +x $OUTPUT_DIR/linux-x64/RetroArr

cd $OUTPUT_DIR
tar -czf "RetroArr-Linux-x64.tar.gz" "linux-x64"
cd ..

# MacOS x64 (Intel)
echo " -> Building for MacOS (Intel)..."
dotnet publish src/RetroArr.Host/RetroArr.Host.csproj -c Release -r osx-x64 --self-contained true -o $OUTPUT_DIR/osx-x64
echo "    Copying assets..."
mkdir -p $OUTPUT_DIR/osx-x64/_output/UI
cp -R _output/UI/* $OUTPUT_DIR/osx-x64/_output/UI/
cp src/RetroArr.Host/appsettings.json $OUTPUT_DIR/osx-x64/
# Ensure no personal configs are included
rm -f $OUTPUT_DIR/osx-x64/config/*.json
rm -f $OUTPUT_DIR/osx-x64/settings/*.json

echo "Packaging MacOS Intel..."
./create_mac_app.sh osx-x64

# MacOS arm64 (Apple Silicon)
echo " -> Building for MacOS (Apple Silicon)..."
dotnet publish src/RetroArr.Host/RetroArr.Host.csproj -c Release -r osx-arm64 --self-contained true -o $OUTPUT_DIR/osx-arm64
echo "    Copying assets..."
mkdir -p $OUTPUT_DIR/osx-arm64/_output/UI
cp -R _output/UI/* $OUTPUT_DIR/osx-arm64/_output/UI/
cp src/RetroArr.Host/appsettings.json $OUTPUT_DIR/osx-arm64/
# Ensure no personal configs are included
rm -f $OUTPUT_DIR/osx-arm64/config/*.json
rm -f $OUTPUT_DIR/osx-arm64/settings/*.json

echo "=================================="
echo "Packaging MacOS Silicon..."
./create_mac_app.sh osx-arm64

echo "=================================="
echo "Build Complete!"
echo "Artifacts available in: $OUTPUT_DIR"
echo "=================================="
