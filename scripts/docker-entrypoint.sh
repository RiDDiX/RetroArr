#!/bin/sh
set -e

CERT_DIR="/app/config/certs"
CERT_PFX="$CERT_DIR/retroarr.pfx"
CERT_PASS="retroarr"

# Generate a self-signed cert on first start if none exists. Only used when
# HTTPS is enabled (RETROARR_HTTPS_PORT set). Regenerate by deleting the file.
if [ -n "$RETROARR_HTTPS_PORT" ] && [ ! -f "$CERT_PFX" ]; then
    mkdir -p "$CERT_DIR"
    echo "[entrypoint] generating self-signed certificate at $CERT_PFX"
    openssl req -x509 -newkey rsa:2048 -sha256 -days 3650 -nodes \
        -keyout "$CERT_DIR/retroarr.key" \
        -out "$CERT_DIR/retroarr.crt" \
        -subj "/CN=retroarr" \
        -addext "subjectAltName=DNS:localhost,DNS:retroarr,IP:127.0.0.1" \
        >/dev/null 2>&1
    openssl pkcs12 -export \
        -out "$CERT_PFX" \
        -inkey "$CERT_DIR/retroarr.key" \
        -in "$CERT_DIR/retroarr.crt" \
        -password "pass:$CERT_PASS" \
        >/dev/null 2>&1
    rm -f "$CERT_DIR/retroarr.key" "$CERT_DIR/retroarr.crt"
fi

HTTP_PORT="${RETROARR_HTTP_PORT:-2727}"
URLS="http://+:$HTTP_PORT"

if [ -n "$RETROARR_HTTPS_PORT" ]; then
    URLS="$URLS;https://+:$RETROARR_HTTPS_PORT"
    export ASPNETCORE_Kestrel__Certificates__Default__Path="$CERT_PFX"
    export ASPNETCORE_Kestrel__Certificates__Default__Password="$CERT_PASS"
fi

export ASPNETCORE_URLS="$URLS"
echo "[entrypoint] listening on $URLS"

exec dotnet RetroArr.Host.dll "$@"
