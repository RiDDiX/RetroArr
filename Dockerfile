# Stage 0: Download EmulatorJS assets
FROM alpine:3.19 AS emulatorjs
WORKDIR /emulatorjs
RUN apk add --no-cache curl unzip

# Pre-warm the EmulatorJS asset cache from the CDN. This is an optimization only:
# the runtime (EmulatorController) fetches any missing file from the same CDN on
# first use and caches it under /app/config/emulatorjs. So a CDN outage at build
# time must NOT fail the whole image build -- retry hard, then warn and continue
# ( -f keeps a 404 from being saved as a bogus asset ).
RUN mkdir -p /emulatorjs/data && \
    for f in loader.js emulator.min.js emulator.min.css version.json GameManager.js \
             gamepad.js nipplejs.js shaders.js storage.js socket.io.min.js; do \
      curl -fsSL --retry 5 --retry-delay 3 --retry-connrefused --retry-all-errors \
        -o "/emulatorjs/data/$f" "https://cdn.emulatorjs.org/latest/data/$f" \
      || echo "[emulatorjs] WARN: could not fetch $f at build time; runtime will cache it on first use"; \
    done && \
    echo "stable" > /emulatorjs/data/version.txt

# Pre-download ALL supported EmulatorJS cores (matches PlatformIdToCore in EmulatorController)
# Each core needs a .js loader and a -wasm.data binary; use || true per-line so missing CDN files don't break the build.
RUN mkdir -p /emulatorjs/data/cores && \
    for CORE in nes snes n64 gb gbc gba nds vb \
                segaMS segaMD segaGG segaSaturn sega32x segaCD \
                psx psp \
                atari2600 atari5200 atari7800 lynx jaguar \
                arcade mame2003 3do pce; do \
      curl -fsSL --retry 3 --retry-delay 2 --retry-connrefused --retry-all-errors -o "/emulatorjs/data/cores/${CORE}-wasm.data" \
        "https://cdn.emulatorjs.org/latest/data/cores/${CORE}-wasm.data" 2>/dev/null || true; \
      curl -fsSL --retry 3 --retry-delay 2 --retry-connrefused --retry-all-errors -o "/emulatorjs/data/cores/${CORE}.js" \
        "https://cdn.emulatorjs.org/latest/data/cores/${CORE}.js" 2>/dev/null || true; \
    done && \
    echo "Core pre-download complete: $(ls /emulatorjs/data/cores/ | wc -l) files"

# Stage: Download SteamPrefill (LanCache prefill tool, orchestrated at runtime).
# Non-fatal: a GitHub-release outage must not break the image build; the LanCache
# prefill feature simply reports "unavailable" until the binary is present.
FROM alpine:3.19 AS steamprefill
ARG TARGETARCH
ARG STEAMPREFILL_VERSION=3.7.1
WORKDIR /steamprefill
RUN apk add --no-cache curl unzip
RUN case "$TARGETARCH" in \
        amd64) SP_ARCH=linux-x64 ;; \
        arm64) SP_ARCH=linux-arm64 ;; \
        *)     SP_ARCH=linux-x64 ;; \
    esac && \
    ( curl -fsSL --retry 5 --retry-delay 3 --retry-connrefused --retry-all-errors \
        -o /tmp/sp.zip "https://github.com/tpill90/steam-lancache-prefill/releases/download/v${STEAMPREFILL_VERSION}/SteamPrefill-${STEAMPREFILL_VERSION}-${SP_ARCH}.zip" \
      && unzip -oq /tmp/sp.zip -d /steamprefill \
      && rm -f /tmp/sp.zip \
      && ( [ -f /steamprefill/SteamPrefill ] || find /steamprefill -maxdepth 2 -type f -name 'SteamPrefill' -exec cp {} /steamprefill/SteamPrefill \; ) \
      && chmod +x /steamprefill/SteamPrefill \
    ) || echo "[steamprefill] WARN: download failed at build time; LanCache prefill will be unavailable until the image is rebuilt"

# Stage: Download BattleNetPrefill (same author/shape as SteamPrefill, non-fatal).
FROM alpine:3.19 AS battlenetprefill
ARG TARGETARCH
ARG BATTLENETPREFILL_VERSION=2.3.0
WORKDIR /battlenetprefill
RUN apk add --no-cache curl unzip
RUN case "$TARGETARCH" in \
        amd64) A=linux-x64 ;; arm64) A=linux-arm64 ;; *) A=linux-x64 ;; \
    esac && \
    ( curl -fsSL --retry 5 --retry-delay 3 --retry-connrefused --retry-all-errors \
        -o /tmp/bn.zip "https://github.com/tpill90/battlenet-lancache-prefill/releases/download/v${BATTLENETPREFILL_VERSION}/BattleNetPrefill-${BATTLENETPREFILL_VERSION}-${A}.zip" \
      && unzip -oq /tmp/bn.zip -d /battlenetprefill && rm -f /tmp/bn.zip \
      && ( [ -f /battlenetprefill/BattleNetPrefill ] || find /battlenetprefill -maxdepth 2 -type f -name 'BattleNetPrefill' -exec cp {} /battlenetprefill/BattleNetPrefill \; ) \
      && chmod +x /battlenetprefill/BattleNetPrefill \
    ) || echo "[battlenetprefill] WARN: download failed at build time; Battle.net prefill will be unavailable"

# Stage: Download EpicPrefill (same author/shape, non-fatal).
FROM alpine:3.19 AS epicprefill
ARG TARGETARCH
ARG EPICPREFILL_VERSION=2.1.0
WORKDIR /epicprefill
RUN apk add --no-cache curl unzip
RUN case "$TARGETARCH" in \
        amd64) A=linux-x64 ;; arm64) A=linux-arm64 ;; *) A=linux-x64 ;; \
    esac && \
    ( curl -fsSL --retry 5 --retry-delay 3 --retry-connrefused --retry-all-errors \
        -o /tmp/ep.zip "https://github.com/tpill90/epic-lancache-prefill/releases/download/v${EPICPREFILL_VERSION}/EpicPrefill-${EPICPREFILL_VERSION}-${A}.zip" \
      && unzip -oq /tmp/ep.zip -d /epicprefill && rm -f /tmp/ep.zip \
      && ( [ -f /epicprefill/EpicPrefill ] || find /epicprefill -maxdepth 2 -type f -name 'EpicPrefill' -exec cp {} /epicprefill/EpicPrefill \; ) \
      && chmod +x /epicprefill/EpicPrefill \
    ) || echo "[epicprefill] WARN: download failed at build time; Epic prefill will be unavailable"

# Stage 1: Build the Frontend (React)
FROM node:22 AS frontend
WORKDIR /src
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci --prefer-offline
COPY frontend/ ./frontend/
COPY tsconfig.json ./
COPY frontend/build/webpack.config.js ./frontend/build/
RUN npm run build

# Stage 2: Build the Backend (.NET)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend
WORKDIR /source
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Copy solution and build configuration files to root of source
COPY src/RetroArr.sln ./
COPY src/Directory.Build.props ./
COPY src/Directory.Build.targets ./
COPY src/NuGet.config ./

# Copy each project file explicitly into its own folder (relative to WORKDIR)
COPY src/RetroArr.Api.V3/*.csproj RetroArr.Api.V3/
COPY src/RetroArr.Common/*.csproj RetroArr.Common/
COPY src/RetroArr.Console/*.csproj RetroArr.Console/
COPY src/RetroArr.Core/*.csproj RetroArr.Core/
COPY src/RetroArr.Host/*.csproj RetroArr.Host/
COPY src/RetroArr.Http/*.csproj RetroArr.Http/
COPY src/RetroArr.SignalR/*.csproj RetroArr.SignalR/
COPY src/RetroArr.UsbHelper/*.csproj RetroArr.UsbHelper/

# Restore dependencies (Host project pulls in all runtime deps)
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore RetroArr.Host/RetroArr.Host.csproj

# Copy everything else
COPY src/ ./

# Inject ScreenScraper dev credentials at build time (replaced via GitHub Secrets)
ARG SCREENSCRAPER_DEVID=""
ARG SCREENSCRAPER_DEVPASSWORD=""
RUN if [ -n "$SCREENSCRAPER_DEVID" ]; then \
      sed -i "s/%%SCREENSCRAPER_DEVID%%/${SCREENSCRAPER_DEVID}/g" RetroArr.Core/MetadataSource/ScreenScraper/ScreenScraperClient.cs; \
    fi && \
    if [ -n "$SCREENSCRAPER_DEVPASSWORD" ]; then \
      sed -i "s/%%SCREENSCRAPER_DEVPASSWORD%%/${SCREENSCRAPER_DEVPASSWORD}/g" RetroArr.Core/MetadataSource/ScreenScraper/ScreenScraperClient.cs; \
    fi

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish RetroArr.Host/RetroArr.Host.csproj -c Release -o /app/publish

# Stage 3: Final Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Install runtime dependencies for Switch USB support (Python + libusb),
# healthcheck (curl), and self-signed cert generation (openssl)
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    libusb-1.0-0 \
    curl \
    openssl \
    && rm -rf /var/lib/apt/lists/*

# Install pyusb
RUN pip3 install --break-system-packages pyusb

COPY --from=backend /app/publish .

# Ensure no personal configs are included in the image
RUN rm -f /app/config/*.json && rm -f /app/settings/*.json && rm -f /app/appsettings.Development.json

# Copy frontend artifacts to where the backend expects them
COPY --from=frontend /src/_output/UI ./_output/UI

# Copy CHANGELOG.md for the system/changelog API endpoint
COPY CHANGELOG.md /app/CHANGELOG.md

# Copy EmulatorJS assets (pre-downloaded during build)
COPY --from=emulatorjs /emulatorjs/data /app/config/emulatorjs

# Bundle the LanCache prefill tools (Steam / Battle.net / Epic). Each keeps its
# session/state in ./Config next to the binary; the entrypoint symlinks those to
# the persistent config volume.
COPY --from=steamprefill /steamprefill /opt/steamprefill
COPY --from=battlenetprefill /battlenetprefill /opt/battlenetprefill
COPY --from=epicprefill /epicprefill /opt/epicprefill

# Entrypoint script handles self-signed cert generation + dual-listener config
COPY scripts/docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

# Create config, media and savestate directories; non-root user
RUN mkdir -p /app/config /app/savestates /media && \
    groupadd -g 1000 retroarr && \
    useradd -u 1000 -g retroarr -s /usr/sbin/nologin -M retroarr && \
    chown -R retroarr:retroarr /app /media && \
    (chown -R retroarr:retroarr /opt/steamprefill /opt/battlenetprefill /opt/epicprefill 2>/dev/null || true)

USER retroarr

# 2727 = HTTP, 2728 = HTTPS (optional; only enabled when RETROARR_HTTPS_PORT is set)
EXPOSE 2727 2728
ENV DOTNET_RUNNING_IN_CONTAINER=true

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:2727/api/v3/system/status > /dev/null || exit 1

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
