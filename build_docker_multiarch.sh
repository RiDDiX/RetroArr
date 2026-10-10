#!/bin/bash
# Builds multi-arch Docker images for RetroArr

echo "===================================================="
echo "      RetroArr Multi-Arch Docker Build Tool          "
echo "===================================================="

# Base configuration
IMAGE_REPO="git.riddix.de/riddix/retroarr"
PACKAGE_FILE="package.json"

# 1. Read version from package.json
if [ -f "$PACKAGE_FILE" ]; then
    VERSION=$(grep '"version":' "$PACKAGE_FILE" | head -n 1 | awk -F '"' '{print $4}')
    echo "Detected Version: $VERSION"
else
    echo "Error: $PACKAGE_FILE not found. Cannot detect version."
    exit 1
fi

# Define tags
TAG_LATEST="$IMAGE_REPO:latest"
TAG_VER="$IMAGE_REPO:$VERSION"
TAG_V_VER="$IMAGE_REPO:v$VERSION"

echo "Tags to build:"
echo " - $TAG_LATEST"
echo " - $TAG_VER"
echo " - $TAG_V_VER"

# 2. Arguments
PUSH_FLAG=""
OUTPUT_FLAG="--output type=docker" # By default try to load locally (fails for multi-arch with the standard docker driver)

if [[ "$1" == "--push" ]]; then
    echo "Mode: BUILD & PUSH (pushing to the registry)"
    PUSH_FLAG="--push"
    OUTPUT_FLAG="" 
else
    echo "Mode: BUILD ONLY (nothing is pushed to the registry)"
    echo "To push, run: ./build_docker_multiarch.sh --push"
fi

# 3. Check Buildx
if ! docker buildx version >/dev/null 2>&1; then
    echo "Error: Docker Buildx is not installed."
    exit 1
fi

BUILDER_NAME="RetroArr-builder"
if ! docker buildx inspect "$BUILDER_NAME" >/dev/null 2>&1; then
    echo "Creating multi-platform builder..."
    docker buildx create --name "$BUILDER_NAME" --use
    docker buildx inspect --bootstrap
else
    docker buildx use "$BUILDER_NAME"
fi

# 4. Run build
echo "Starting build for linux/amd64 and linux/arm64..."

docker buildx build \
    --platform linux/amd64,linux/arm64 \
    -t "$TAG_LATEST" \
    -t "$TAG_VER" \
    -t "$TAG_V_VER" \
    $PUSH_FLAG \
    .

if [ $? -eq 0 ]; then
    echo "===================================================="
    echo "          Build finished successfully             "
    if [[ "$1" == "--push" ]]; then
        echo "          Images pushed to the registry           "
    else
        echo "Note: the images are in the buildx cache."
        echo "Use --push to push them or --load (single arch only) to use them locally."
    fi
    echo "===================================================="
else
    echo "Error: the build failed."
    exit 1
fi
