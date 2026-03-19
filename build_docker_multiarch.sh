#!/bin/bash
# Script para construir imágenes Docker multi-arquitectura para RetroArr

echo "===================================================="
echo "      RetroArr Multi-Arch Docker Build Tool          "
echo "===================================================="

# Configuración Base
IMAGE_REPO="git.riddix.de/riddix/retroarr"
PACKAGE_FILE="package.json"

# 1. Leer versión del package.json
if [ -f "$PACKAGE_FILE" ]; then
    VERSION=$(grep '"version":' "$PACKAGE_FILE" | head -n 1 | awk -F '"' '{print $4}')
    echo "Detected Version: $VERSION"
else
    echo "Error: $PACKAGE_FILE not found. Cannot detect version."
    exit 1
fi

# Definir Tags
TAG_LATEST="$IMAGE_REPO:latest"
TAG_VER="$IMAGE_REPO:$VERSION"
TAG_V_VER="$IMAGE_REPO:v$VERSION"

echo "Tags to build:"
echo " - $TAG_LATEST"
echo " - $TAG_VER"
echo " - $TAG_V_VER"

# 2. Argumentos
PUSH_FLAG=""
OUTPUT_FLAG="--output type=docker" # Por defecto, intentar cargar localmente (fallará para multi-arch en docker driver standard)

if [[ "$1" == "--push" ]]; then
    echo "Modo: BUILD & PUSH (Subiendo a Docker Hub)"
    PUSH_FLAG="--push"
    OUTPUT_FLAG="" 
else
    echo "Modo: BUILD ONLY (No se subirá a Docker Hub)"
    echo "Para subir, ejecuta: ./build_docker_multiarch.sh --push"
fi

# 3. Verificar Buildx
if ! docker buildx version >/dev/null 2>&1; then
    echo "Error: Docker Buildx no está instalado."
    exit 1
fi

BUILDER_NAME="RetroArr-builder"
if ! docker buildx inspect "$BUILDER_NAME" >/dev/null 2>&1; then
    echo "Creando constructor multi-plataforma..."
    docker buildx create --name "$BUILDER_NAME" --use
    docker buildx inspect --bootstrap
else
    docker buildx use "$BUILDER_NAME"
fi

# 4. Ejecutar Build
echo "Iniciando build para linux/amd64 y linux/arm64..."

docker buildx build \
    --platform linux/amd64,linux/arm64 \
    -t "$TAG_LATEST" \
    -t "$TAG_VER" \
    -t "$TAG_V_VER" \
    $PUSH_FLAG \
    .

if [ $? -eq 0 ]; then
    echo "===================================================="
    echo "          Build finalizado correctamente            "
    if [[ "$1" == "--push" ]]; then
        echo "          Imágenes subidas a Docker Hub 🚀          "
    else
        echo "Nota: Las imágenes están en el caché de buildx."
        echo "Usa --push para subirlas o --load (solo single arch) para usarlas localmente."
    fi
    echo "===================================================="
else
    echo "Error: El build falló."
    exit 1
fi
