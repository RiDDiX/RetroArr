<p align="center">
  <img src="frontend/src/assets/RetroArr-Logo.png" alt="RetroArr" width="600" />
</p>

<p align="center">
  <strong>Self-hosted game library manager & PVR for video games and retro consoles.</strong>
</p>

<p align="center">
  <a href="https://RetroArr.app"><img src="https://img.shields.io/badge/Website-RetroArr.app-6366f1?style=flat-square" alt="Website" /></a>
  <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square" alt="License" /></a>
  <a href="https://github.com/RiDDiX/RetroArr/pkgs/container/retroarr"><img src="https://img.shields.io/badge/Docker-amd64%20%2F%20arm64-2496ed?style=flat-square&logo=docker&logoColor=white" alt="Docker" /></a>
  <a href="https://github.com/RiDDiX/RetroArr/releases"><img src="https://img.shields.io/badge/Downloads-Releases-green?style=flat-square&logo=github" alt="Releases" /></a>
</p>

---

Inspired by the *arr stack (Radarr, Sonarr), RetroArr manages your game library the same way — scan local files, fetch metadata, organize, search indexers, and automate downloads. It covers PC games, retro consoles, handhelds, and arcade platforms under one roof.

## Quick Start

```yaml
# docker-compose.yml
services:
  retroarr:
    image: ghcr.io/riddix/retroarr:latest
    container_name: retroarr
    ports:
      - "2727:2727"
    volumes:
      - ./config:/app/config
      - /your/games/path:/media
      - ./savestates:/app/savestates
    environment:
      - PUID=1000
      - PGID=1000
    restart: unless-stopped
```

```bash
docker compose up -d
```

Open `http://your-ip:2727`, then go to **Settings → Metadata** and add your [IGDB credentials](https://api-docs.igdb.com/#getting-started) (free Twitch dev account).

## Features

**Library & Scanning**
- Scans and identifies files across **80+ platforms** — PC, PlayStation 1–5, Xbox, Nintendo NES through Switch 2, Sega, Atari, NEC, Arcade (MAME, FBNeo, CPS, Neo Geo), ScummVM, DOSBox, and more
- Metadata from **IGDB**, **Steam**, and **ScreenScraper** (retro/arcade fallback): covers, descriptions, ratings, release dates
- Region & language detection from filenames with flag display (USA, Europe, Japan, PAL, multi-region)
- Revision/variant tagging: Beta, Rev A, Disc 2, etc.
- Automatic folder renaming based on cleaned metadata titles

**Download Automation**
- Indexer search via **Prowlarr** or **Jackett** (torrent + Usenet)
- Download clients: qBittorrent, Transmission, Deluge, SABnzbd, NZBGet
- Post-download processing with automatic file handling

**Launching & Platforms**
- Steam and GOG direct launch with library sync
- Executable discovery for installed PC games (Windows, macOS, Linux)
- Linux: automatic Proton/Wine detection, per-game runner selection, Lutris export, Steam Deck shortcuts, XDG `.desktop` generation
- macOS: Whisky / CrossOver delegation
- Built-in browser-based retro emulator via [EmulatorJS](https://emulatorjs.org/) for supported platforms — play directly from the web UI with save state support (Docker: `/app/savestates`)

**More**
- Multi-language UI (EN, DE, ES, FR, RU, ZH, JA)
- Review import gate for unidentified files
- Plugin system (process-isolated, language-agnostic, circuit breaker)
- Webhook notifications for library events
- Nintendo Switch USB transfer (DBI protocol)
- Database: SQLite (default), PostgreSQL, MariaDB with built-in migration
- Optional Redis cache layer
- Structured logging with per-feature files, rotation, and diagnostics export
- Real-time scan/download progress via SignalR

See also: [Linux Gaming](docs/LINUX_GAMING.md) · [Plugins](docs/PLUGIN_GUIDE.md) · [Scanner Logic](docs/SCANNING_LOGIC.md) · [Updates & DLC](docs/UPDATES_DLC_GUIDE.md) · [Launcher Specs](docs/LAUNCHER_SPECS.md) · [Installer Logic](docs/INSTALLER_LOGIC.md)

## Installation

### Docker (recommended)

See [Quick Start](#quick-start). Web UI runs on port **2727**.

<details>
<summary><strong>CasaOS</strong></summary>

Go to **App Store → Custom Install → Import** and paste:

```yaml
services:
  retroarr:
    image: ghcr.io/riddix/retroarr:latest
    container_name: retroarr
    ports:
      - "2727:2727"
    volumes:
      - /DATA/AppData/RetroArr/config:/app/config
      - /DATA/Media/Games:/media
      - /DATA/AppData/RetroArr/savestates:/app/savestates
    environment:
      - PUID=1000
      - PGID=1000
    restart: unless-stopped

x-casaos:
  architectures:
    - amd64
    - arm64
  main: retroarr
  icon: https://raw.githubusercontent.com/RiDDiX/RetroArr/main/frontend/src/assets/app_logo.png
  title:
    en_us: RetroArr
```

</details>

<details>
<summary><strong>Synology / NAS</strong></summary>

Open **Container Manager → Project → Create**, paste the compose template from `_synology/docker-compose.yml`, adjust your paths, and click **Done**.

</details>

<details>
<summary><strong>Unraid</strong></summary>

Use the Community Applications template in `_unraid/retroarr.xml`, or add the container manually with image `ghcr.io/riddix/retroarr:latest`.

</details>

### Desktop

Download from [GitHub Releases](https://github.com/RiDDiX/RetroArr/releases):

| Platform | File |
|----------|------|
| Windows | `RetroArr-Setup.exe` (installer) or portable `.exe` |
| macOS | `RetroArr.app` — Apple Silicon and Intel |
| Linux | Generic x64 binary |

Desktop mode uses ports 5002–5005 with config in `{AppData}/RetroArr/config`.

### Build from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Node.js 20+](https://nodejs.org/).

```bash
git clone https://github.com/RiDDiX/RetroArr.git
cd RetroArr
docker build -t retroarr:local .
```

Or individually:

```bash
cd frontend && npm install && npm run build   # Frontend
cd src && dotnet build RetroArr.sln           # Backend
```

## Configuration

All config lives in `config/` (Docker: `/app/config`) as JSON files.

| Setting | Where | What |
|---------|-------|------|
| IGDB | Settings → Metadata | Twitch Client ID + Secret (required) |
| Prowlarr / Jackett | Settings → Connections | Indexer URL + API Key |
| Download Clients | Settings → Download Clients | qBit, Transmission, Deluge, SABnzbd, NZBGet |
| Steam | Settings → Steam | API Key + Steam ID for library sync |
| GOG | Settings → GOG | OAuth for GOG library access |
| Media Library | Settings → Media | Root path to your game folders |
| ScreenScraper | Settings → Metadata | Retro/arcade metadata fallback |
| Webhooks | Settings → Notifications | Event notification endpoints |
| Database | Settings → Database | SQLite / PostgreSQL / MariaDB + migration |
| Cache | Settings → Cache | Redis connection + TTL config |
| Logging | Settings → Logging | Log levels, rotation, redaction |

**Environment variables:**

| Variable | Default | Description |
|----------|---------|-------------|
| `PUID` | `1000` | User ID for file permissions |
| `PGID` | `1000` | Group ID for file permissions |
| `ASPNETCORE_URLS` | `http://+:2727` | Listen address |

**Docker volumes:**

| Path | Description |
|------|-------------|
| `/app/config` | Configuration files (JSON) |
| `/media` | Game library root folder |
| `/app/savestates` | EmulatorJS save states |

## Development

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Node.js 20+](https://nodejs.org/).

```bash
npm run lint                                          # Lint frontend
npm run build                                         # Build frontend
cd src && dotnet build RetroArr.sln -c Release        # Build backend
cd src && dotnet test RetroArr.Core.Test -c Release   # Run tests
docker build -t retroarr:local .                      # Docker image
```

### Project Layout

```
src/
├── RetroArr.Host         # Kestrel server, Photino desktop wrapper
├── RetroArr.Api.V3       # REST controllers
├── RetroArr.Core         # Business logic, services, models
├── RetroArr.Core.Test    # NUnit tests
├── RetroArr.Common       # Shared utilities
├── RetroArr.Http         # HTTP client helpers
├── RetroArr.SignalR       # Real-time hub
└── RetroArr.UsbHelper    # Switch USB transfer
frontend/
├── src/pages/            # React pages
├── src/components/       # Reusable components
└── src/i18n/             # Translations (7 languages)
```

## Contributing

Found a bug or have an idea? [Open an issue](https://github.com/RiDDiX/RetroArr/issues). Pull requests are welcome.

## License

MIT — see [LICENSE](LICENSE) for details.

## Disclaimer

RetroArr is for personal library management and educational purposes. Not affiliated with any game platform or metadata provider. Users are responsible for compliance with local copyright laws. See [DISCLAIMER.md](DISCLAIMER.md).

---

<p align="center"><sub>Made by <a href="https://github.com/RiDDiX">RiDDiX</a></sub></p>
