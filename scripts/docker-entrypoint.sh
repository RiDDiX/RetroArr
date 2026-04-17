#!/bin/sh
set -e

CERT_DIR="/app/config/certs"
CERT_PFX="$CERT_DIR/retroarr.pfx"
CERT_PASS="retroarr"

# Generate a self-signed cert on first start if none exists. Only runs when
# HTTPS is enabled (RETROARR_HTTPS_PORT set). Regenerate by deleting the file.
# RETROARR_CERT_SAN lets the user pass extra SAN entries (comma-separated),
# e.g. "IP:192.168.1.10,DNS:retroarr.lan" so the cert matches their LAN IP.
if [ -n "$RETROARR_HTTPS_PORT" ] && [ ! -f "$CERT_PFX" ]; then
    mkdir -p "$CERT_DIR"

    SAN="DNS:localhost,DNS:retroarr,IP:127.0.0.1,IP:::1"

    # Pick up all non-loopback IPv4 addresses of the container and add them
    # as SANs. Helps when the container runs on a LAN with a static bridge IP.
    if command -v hostname >/dev/null 2>&1; then
        for ip in $(hostname -I 2>/dev/null || true); do
            case "$ip" in
                127.*|::1) ;;
                *) SAN="$SAN,IP:$ip" ;;
            esac
        done
    fi

    if [ -n "$RETROARR_CERT_SAN" ]; then
        SAN="$SAN,$RETROARR_CERT_SAN"
    fi

    echo "[entrypoint] generating self-signed certificate (SAN: $SAN)"
    openssl req -x509 -newkey rsa:2048 -sha256 -days 3650 -nodes \
        -keyout "$CERT_DIR/retroarr.key" \
        -out "$CERT_DIR/retroarr.crt" \
        -subj "/CN=retroarr" \
        -addext "subjectAltName=$SAN" \
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
