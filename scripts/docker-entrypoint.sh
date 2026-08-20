#!/bin/sh
set -e

CERT_DIR="/app/config/certs"
CERT_PFX="$CERT_DIR/retroarr.pfx"
CERT_PASS="retroarr"

# Make a self-signed cert on first start when https is on. Delete the pfx
# to force a regen. RETROARR_CERT_SAN adds extra entries if you need the
# cert to match a specific lan ip or dns name.
if [ -n "$RETROARR_HTTPS_PORT" ] && [ ! -f "$CERT_PFX" ]; then
    mkdir -p "$CERT_DIR"

    SAN="DNS:localhost,DNS:retroarr,IP:127.0.0.1,IP:::1"

    # Pull the container's non-loopback ips into the cert too, so the lan
    # bridge ip is covered out of the box.
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

# The LanCache prefill tools keep their session + prefill state in <binaryDir>/Config.
# Redirect each to the persistent config volume so a one-time login survives restarts.
# Best-effort: never block startup on it.
for tool in steamprefill:SteamPrefill battlenetprefill:BattleNetPrefill epicprefill:EpicPrefill; do
    dir="/opt/${tool%%:*}"
    bin="$dir/${tool##*:}"
    [ -x "$bin" ] || continue
    mkdir -p "/app/config/${tool%%:*}" 2>/dev/null || true
    # If a real Config dir shipped in the zip, drop it so the symlink takes effect.
    [ -d "$dir/Config" ] && [ ! -L "$dir/Config" ] && rm -rf "$dir/Config" 2>/dev/null || true
    ln -sfn "/app/config/${tool%%:*}" "$dir/Config" 2>/dev/null \
        && echo "[entrypoint] ${tool##*:} bundled; session dir -> /app/config/${tool%%:*}" \
        || echo "[entrypoint] warning: could not link ${tool##*:} Config dir"
done

exec dotnet RetroArr.Host.dll "$@"
