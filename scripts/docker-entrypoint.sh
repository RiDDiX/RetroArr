#!/bin/sh
set -e

# Started as root (the default): hand the app's own folders to PUID/PGID, then run
# this script again as that user, so the cert, the prefill links and the app itself
# are all created and run as PUID/PGID. That keeps a config folder docker created as
# root working, and it also works on root-squashed NFS. The library and downloads are
# never touched. Extra groups from group_add are kept, root's are not. PUID=0 stays root.
if [ "$(id -u)" = "0" ]; then
    PUID="${PUID:-1000}"
    PGID="${PGID:-1000}"
    case "$PUID$PGID" in
        *[!0-9]*) echo "[entrypoint] error: PUID and PGID must be numbers (got PUID=$PUID PGID=$PGID)"; exit 1 ;;
    esac
    # Drop leading zeros (00 means root) and refuse numbers too long to be an id
    PUID=$(echo "$PUID" | sed 's/^0*\([0-9]\)/\1/')
    PGID=$(echo "$PGID" | sed 's/^0*\([0-9]\)/\1/')
    if [ ${#PUID} -gt 10 ] || [ ${#PGID} -gt 10 ]; then
        echo "[entrypoint] error: PUID=$PUID PGID=$PGID is not a usable user/group id"
        exit 1
    fi
fi
if [ "$(id -u)" = "0" ] && [ "$PUID" != "0" ]; then
    if ! landed=$(setpriv --reuid="$PUID" --regid="$PGID" --clear-groups /bin/sh -c 'echo "$(id -u):$(id -g)"' 2>/dev/null); then
        echo "[entrypoint] error: started as root but can't switch to $PUID:$PGID (no SETUID/SETGID capability, e.g. cap_drop: ALL). Run the container with user: \"$PUID:$PGID\" instead, or add cap_add: CHOWN, SETUID, SETGID."
        exit 1
    fi
    if [ "$landed" != "$PUID:$PGID" ]; then
        echo "[entrypoint] error: PUID=$PUID PGID=$PGID is not a usable user/group id. Use PUID=0 to stay root."
        exit 1
    fi
    # The prefill tools write app.log next to the binary and the in-app updater swaps
    # the binary there, so their folders are included (a one-time copy for PUID != 1000).
    # The walk never follows links: a folder reached through a symlink is skipped, files
    # with more than one hard link are left alone, and -execdir keeps chown from looking
    # up the parent folders again. So nothing planted in these folders can hand some
    # other file in the container to PUID.
    for dir in /app/config /app/savestates /home/retroarr \
               /opt/steamprefill /opt/battlenetprefill /opt/epicprefill \
               "${RETROARR_KEYS_DIR:-}" "${RetroArr_SAVESTATES_PATH:-}" "${RetroArr_EMULATORJS_PATH:-}"; do
        [ -n "$dir" ] || continue
        dir=$(realpath -s -m -- "$dir")
        [ -d "$dir" ] && [ "$dir" != "/" ] || continue
        if [ "$(readlink -f -- "$dir")" != "$dir" ]; then
            echo "[entrypoint] warning: not touching $dir, its path goes through a symlink; fix its ownership on the host"
            continue
        fi
        # find refuses -execdir when PATH holds "." or an empty entry, so give it a fixed one
        PATH=/usr/sbin:/usr/bin:/sbin:/bin find "$dir" -xdev \( -type d -o -links 1 \) \( ! -user "$PUID" -o ! -group "$PGID" \) \
            -execdir chown -h "$PUID:$PGID" {} + \
            || echo "[entrypoint] warning: could not chown $dir to $PUID:$PGID; fix its ownership on the host"
    done
    extra_groups=$(id -G | tr ' ' '\n' | grep -vx 0 | paste -sd, -)
    echo "[entrypoint] running as $PUID:$PGID"
    exec setpriv --reuid="$PUID" --regid="$PGID" --groups="$PGID${extra_groups:+,$extra_groups}" /bin/sh "$0" "$@"
fi

CERT_DIR="/app/config/certs"
CERT_PFX="$CERT_DIR/retroarr.pfx"
CERT_PASS="retroarr"

# The app user's home. Set here, not as an image ENV, so a root docker exec keeps /root.
# An explicitly configured HOME is left alone.
# Then say up front what will fail. With user:/--user the root phase above never ran,
# so ownership can only be fixed on the host or by dropping user: for PUID/PGID.
if [ "$(id -u)" != "0" ]; then
    case "${HOME:-}" in
        ""|/|/root) export HOME=/home/retroarr ;;
    esac
    for dir in /app/config "$HOME" /opt/steamprefill /opt/battlenetprefill /opt/epicprefill; do
        if [ -d "$dir" ] && [ ! -w "$dir" ]; then
            echo "[entrypoint] warning: $dir is not writable by uid $(id -u):$(id -g); chown it to $(id -u):$(id -g), or if you use user:, drop it and set PUID/PGID instead."
        fi
    done
fi

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
    # The tools append to app.log in their working directory, which is this folder for
    # the app's own runs. Having it in place means a run as root from here (a plain
    # docker exec -w) appends to the app's file instead of creating a root-owned one.
    touch "$dir/app.log" 2>/dev/null || true
done

exec dotnet RetroArr.Host.dll "$@"
