# Linux Gaming Integration

RetroArr supports Linux desktop environments, Steam Deck, and immutable distributions like Bazzite with built-in export and launch features.

## Runner Selection

When launching a Windows `.exe` on Linux, RetroArr automatically detects the best available runner:

1. **Proton** — Preferred if Steam is installed and a Proton version is found
2. **Wine** — Fallback if Proton is not available
3. **Native** — For Linux-native binaries (ELF)

You can override the runner per game via the **Export** dropdown on the game details page, or via the API:

```
PUT /api/v3/game/{id}
{ "preferredRunner": "proton" }
```

Valid values: `auto`, `proton`, `wine`, `native`.

### Proton Detection Paths

RetroArr scans these directories for Proton installations:

- `~/.steam/steam/steamapps/common/Proton*`
- `~/.local/share/Steam/steamapps/common/Proton*`
- `~/.steam/root/steamapps/common/Proton*`
- `/usr/share/steam/steamapps/common/` (Flatpak/system installs)

The newest version is selected automatically.

## Export Features

### Lutris Installer YAML

Generate a [Lutris](https://lutris.net/) installer file for any game in your library:

```
GET /api/v3/linux/export/lutris/{gameId}?runner=wine
```

The generated YAML follows the [Lutris installer format](https://github.com/lutris/lutris/blob/master/docs/installers.rst) and can be imported directly into Lutris.

### Steam Shortcut (Steam Deck Game Mode)

Export a game as a Steam shortcut for use in Steam Deck's Game Mode:

```
GET /api/v3/linux/export/steam-shortcut/{gameId}
```

Returns JSON with all fields needed for a Steam shortcut entry. For bulk export:

```
GET /api/v3/linux/export/steam-shortcuts
```

### XDG .desktop Launcher

Generate a standard Linux `.desktop` file for any game:

```
GET /api/v3/linux/export/desktop-entry/{gameId}?runner=proton
```

Place the generated file in `~/.local/share/applications/` to have it appear in your application menu.

### Runner Info

Check which runners are available on the current system:

```
GET /api/v3/linux/export/runners
```

## Steam Deck Tips

- RetroArr's web UI works in Steam Deck's desktop browser at `http://localhost:2727`
- Use the **Steam Shortcut** export to add non-Steam games to Game Mode
- For Flatpak Steam installations, Proton is typically at `~/.var/app/com.valvesoftware.Steam/data/Steam/steamapps/common/`
- The UI uses standard web controls that work with Steam Deck's trackpads and touchscreen

## Bazzite / Immutable Distros

RetroArr runs as a Docker container, making it fully compatible with immutable Linux distributions like Bazzite, SteamOS, and Fedora Silverblue. No host-level package installation is required.

```yaml
# docker-compose.yml for Bazzite / SteamOS
services:
  retroarr:
    image: ghcr.io/riddix/retroarr:latest
    container_name: retroarr
    ports:
      - "2727:2727"
    volumes:
      - ~/.config/retroarr:/app/config
      - ~/Games:/media
    restart: unless-stopped
```
