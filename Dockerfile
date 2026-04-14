# Stage 0: Download EmulatorJS assets
FROM alpine:3.19 AS emulatorjs
WORKDIR /emulatorjs
RUN apk add --no-cache curl unzip

# Download EmulatorJS stable release from CDN
RUN mkdir -p /emulatorjs/data && \
    curl -L -o /emulatorjs/data/loader.js "https://cdn.emulatorjs.org/stable/data/loader.js" && \
    curl -L -o /emulatorjs/data/emulator.min.js "https://cdn.emulatorjs.org/stable/data/emulator.min.js" && \
    curl -L -o /emulatorjs/data/emulator.min.css "https://cdn.emulatorjs.org/stable/data/emulator.min.css" && \
    curl -L -o /emulatorjs/data/version.json "https://cdn.emulatorjs.org/stable/data/version.json" && \
    curl -L -o /emulatorjs/data/GameManager.js "https://cdn.emulatorjs.org/stable/data/GameManager.js" && \
    curl -L -o /emulatorjs/data/gamepad.js "https://cdn.emulatorjs.org/stable/data/gamepad.js" && \
    curl -L -o /emulatorjs/data/nipplejs.js "https://cdn.emulatorjs.org/stable/data/nipplejs.js" && \
    curl -L -o /emulatorjs/data/shaders.js "https://cdn.emulatorjs.org/stable/data/shaders.js" && \
    curl -L -o /emulatorjs/data/storage.js "https://cdn.emulatorjs.org/stable/data/storage.js" && \
    curl -L -o /emulatorjs/data/socket.io.min.js "https://cdn.emulatorjs.org/stable/data/socket.io.min.js" && \
    echo "stable" > /emulatorjs/data/version.txt

# Pre-download ALL supported EmulatorJS cores (matches PlatformIdToCore in EmulatorController)
# Each core needs a .js loader and a -wasm.data binary; use || true per-line so missing CDN files don't break the build.
RUN mkdir -p /emulatorjs/data/cores && \
    for CORE in nes snes n64 gb gbc gba nds vb \
                segaMS segaMD segaGG segaSaturn sega32x segaCD \
                psx psp \
                atari2600 atari5200 atari7800 lynx jaguar \
                arcade mame2003 3do pce; do \
      curl -fsSL -o "/emulatorjs/data/cores/${CORE}-wasm.data" \
        "https://cdn.emulatorjs.org/stable/data/cores/${CORE}-wasm.data" 2>/dev/null || true; \
      curl -fsSL -o "/emulatorjs/data/cores/${CORE}.js" \
        "https://cdn.emulatorjs.org/stable/data/cores/${CORE}.js" 2>/dev/null || true; \
    done && \
    echo "Core pre-download complete: $(ls /emulatorjs/data/cores/ | wc -l) files"

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

# Install runtime dependencies for Switch USB support (Python + libusb) and healthcheck (curl)
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    libusb-1.0-0 \
    curl \
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

# Create config, media and savestate directories; non-root user
RUN mkdir -p /app/config /app/savestates /media && \
    groupadd -g 1000 retroarr && \
    useradd -u 1000 -g retroarr -s /usr/sbin/nologin -M retroarr && \
    chown -R retroarr:retroarr /app /media

USER retroarr

# Expose port 2727
EXPOSE 2727
ENV ASPNETCORE_URLS=http://+:2727
ENV DOTNET_RUNNING_IN_CONTAINER=true

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:2727/api/v3/system/status > /dev/null || exit 1

ENTRYPOINT ["dotnet", "RetroArr.Host.dll"]
