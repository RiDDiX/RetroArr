#!/bin/sh

# Package Configuration
PYTHON_DIR="/usr/local/python3"
PATH="${SYNOPKG_PKGDEST}/bin:${PYTHON_DIR}/bin:${PATH}"

# Service command
SERVICE_COMMAND="${SYNOPKG_PKGDEST}/bin/RetroArr.Host"
SVC_BACKGROUND=y
SVC_WRITE_PID=y

# Environment
# Force config to be in the "var" directory which persists across updates
HOME="${SYNOPKG_PKGVAR}"
export HOME
export XDG_CONFIG_HOME="${SYNOPKG_PKGVAR}/config"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

service_postinst ()
{
    # Create config directory if it doesn't exist
    mkdir -p "${SYNOPKG_PKGVAR}/config"
    
    # Set permissions
    set_unix_permissions "${SYNOPKG_PKGVAR}"
}
