SPK_NAME = retroarr
SPK_VERS = 1.0.2
SPK_REV = 1
SPK_ICON = src/app_logo.png

DEPENDS = cross/dotnet-runtime

MAINTAINER = RiDDiX
DESCRIPTION = RetroArr is a self-hosted game library manager and PVR inspired by Radarr/Sonarr. Supports 55+ platforms with IGDB/Steam metadata.
DISPLAY_NAME = RetroArr
CHANGELOG = "Linux gaming support, plugin system, updated app store manifests"

HOMEPAGE = https://github.com/RiDDiX/RetroArr
LICENSE  = MIT

# Service setup
SERVICE_USER = auto
SERVICE_SETUP = src/service-setup.sh

# Port configuration
SERVICE_PORT = 2727
SERVICE_PORT_TITLE = RetroArr Web UI

# Data configuration (creates /var/packages/retroarr/var/config)
USE_ALTERNATE_TMPDIR = 1

include ../../mk/spksrc.spk.mk
