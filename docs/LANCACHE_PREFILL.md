# LanCache & Prefill

RetroArr can point at a [LanCache](https://lancache.net/) and warm it with your
game libraries so LAN clients download at local speed. It orchestrates
[tpill90](https://github.com/tpill90)'s prefill tools, which are bundled in the
Docker image:

- **Steam** — [steam-lancache-prefill](https://github.com/tpill90/steam-lancache-prefill)
- **Battle.net** — [battlenet-lancache-prefill](https://github.com/tpill90/battlenet-lancache-prefill)
- **Epic** — [epic-lancache-prefill](https://github.com/tpill90/epic-lancache-prefill)

Everything lives under **Settings → LanCache**.

## How it works

A LanCache is a transparent caching proxy. The first time any client downloads a
game it is stored in the cache; every later download of the same content is
served from the LAN. "Prefilling" downloads the game data through the cache
**without installing anything** (nothing is written to disk on the RetroArr host),
so the cache is warm before anyone actually needs the game.

RetroArr does not replace the cache — it drives the prefill tools and shows their
status. The cache only fills if your **network routes each store's CDN traffic
through the LanCache** (normally via DNS). That part is your LanCache setup, not
RetroArr's; see the [LanCache docs](https://lancache.net/docs/).

## Settings

| Field | Meaning |
|-------|---------|
| Enable | Master toggle for the integration |
| Host / Port | Your LanCache address (IP or DNS, default port 80) — no `http://`, just the host |
| Prefill all owned | Pass `--all` to the prefill tools |
| Also prefill recent | Steam only: also include games played in the last 2 weeks (`--recent`) |
| Retry skipped apps | After a run that skipped apps, retry them once right away |
| Operating system(s) | Steam only: which OS depots to download (`--os`) |

**Check status** probes the LanCache heartbeat (`/lancache-heartbeat`) and reports
whether it is reachable and confirmed as a LanCache. **Load Steam library** lists
your owned Steam games (via the Steam Web API configured under Settings → Steam)
and marks the ones already prefilled.

## One-time login

The prefill tools log in to the store directly — this is **separate** from any
Web API key you set elsewhere in RetroArr. Steam and Epic need an interactive
login (Steam Guard / Epic account); Battle.net uses public CDN data and needs no
login.

Run the login once from a shell (the exact command is also shown per provider in
the tab):

```bash
docker exec -it retroarr /opt/steamprefill/SteamPrefill select-apps
docker exec -it retroarr /opt/epicprefill/EpicPrefill    select-apps
```

`select-apps` logs you in and lets you choose which titles to prefill. The
session and your selection are stored next to each binary in `Config/`, which the
container entrypoint symlinks onto the persistent config volume
(`/app/config/steamprefill`, `/app/config/battlenetprefill`,
`/app/config/epicprefill`). So the login survives restarts and image updates.

## Running a prefill

Once logged in (where required), each provider in the tab shows **bundled /
logged in / prefilled / last run** and a **Run … prefill** button. The run is
non-interactive (`prefill --all --no-ansi …`), streams a live log into the tab,
and updates the prefilled count from the tool's own state file when it finishes.

**Skipped apps:** the tools give up on an app after three failed manifest requests
and move on (`Skipping app...`). That usually means the Steam session dropped —
often near the end of a long run, which then takes the whole rest of the list with
it. Those apps are *not* recorded as downloaded, so with **Retry skipped apps**
enabled RetroArr immediately runs one more pass, which fetches only what is still
missing. The retry is never forced and happens at most once per run; it shows up in
the history as its own entry with a `…-retry` trigger.

**Stopping a run:** while a provider is prefilling, a **Stop** button appears next
to it. It kills that tool's process tree; already-cached data stays cached and the
next run resumes from there.

## Updating the tools

The prefill binaries are pinned when the Docker image is built, so a new upstream
release normally only arrives with a new RetroArr image. **Update tool** next to
each provider pulls that provider's newest release from GitHub and swaps the
binary in place — your login, selection and prefill state are untouched. It is
refused while that provider is prefilling.

The updated binary lives in the image layer, not on the config volume, so
recreating the container falls back to the bundled version (which by then is
usually the newer one anyway).

## Scheduling

Each provider has its own schedule in the LanCache tab: tick *Run … prefill on a
schedule*, pick a time (24h, **local server time**) and optionally the weekdays —
no weekday selected means every day. Schedules are stored with the other LanCache
settings (`lancache.json`) and applied by a background service that checks every
minute, so edits take effect without a restart. The provider's next planned run is
shown in its status line.

A scheduled run is skipped (and logged) if that provider is already running; the
providers are independent, so Steam, Battle.net and Epic can run at different times.

The tools can of course still be driven externally via `cron`/`systemd`:

```bash
docker exec retroarr /opt/steamprefill/SteamPrefill prefill --no-ansi
```

## Persistence

- LanCache and prefill **settings** are saved in `/app/config/lancache.json`.
- Each tool's **session + selected apps + prefill state** live in
  `/app/config/<tool>/` via the entrypoint symlink.

Both are on the mounted config volume, so nothing is lost on restart or update.

## Requirements & caveats

- **DNS routing is on you.** If Steam/Battle.net/Epic CDN hostnames do not resolve
  to your LanCache, prefill downloads bypass the cache and nothing is cached.
- **Family sharing** (Steam) is not guaranteed: depot download access is
  license-based, so games shared with you may not be prefillable by your account.
- The bundled binaries are downloaded at image build time. If that download failed
  (CDN outage), a provider shows **not bundled** until the image is rebuilt/pulled —
  **Update tool** cannot help there, it replaces an existing binary.
- Prefilling large libraries moves a lot of data. Use the per-provider options and
  the tools' own `select-apps` to scope what gets pulled.
