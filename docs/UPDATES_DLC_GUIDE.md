# Updates, Patches & DLC — Folder Structure Guide

This document covers how RetroArr detects and classifies game updates, patches, and DLC for every supported platform that uses them. It explains the ID systems, expected folder structures, filename detection rules, and public databases available for each platform.

For general scanning logic (blacklists, scoring, clustering), see [Scanner Logic](SCANNING_LOGIC.md).

---

## Table of Contents

- [Supported Platforms](#supported-platforms)
- [Nintendo Switch](#nintendo-switch)
- [PlayStation 4](#playstation-4)
- [PlayStation 5](#playstation-5)
- [PlayStation 3](#playstation-3)
- [PlayStation Vita](#playstation-vita)
- [PlayStation Portable](#playstation-portable)
- [Nintendo 3DS](#nintendo-3ds)
- [Nintendo Wii U](#nintendo-wii-u)
- [Xbox 360](#xbox-360)
- [PlayStation 1 / 2 (Serial Detection)](#playstation-1--2-serial-detection)
- [Detection Priority](#detection-priority)
- [Supplementary Folder Names](#supplementary-folder-names)
- [Metadata Providers](#metadata-providers)
- [Public Databases](#public-databases)

---

## Supported Platforms

| Platform | Folder | Extensions | Updates | DLC | ID-based detection |
|----------|--------|------------|---------|-----|--------------------|
| Nintendo Switch | `switch` | `.nsp`, `.xci`, `.nsz`, `.xcz` | Yes | Yes | TitleID (16-hex) |
| PlayStation 4 | `ps4` | `.pkg`, `.ps4` (folder) | Yes | Yes | Serial (`CUSAXXXXX`) |
| PlayStation 5 | `ps5` | `.pkg` | Yes | Yes | Serial (`PPSAXXXXX`) |
| PlayStation 3 | `ps3` | `.iso`, `.pkg`, `.bin`, `.psn`, `.squashfs`, `.m3u`, `.ps3` | Yes | Yes | Serial (`BLES`/`BLUS`/`NPUB`/etc.) |
| PlayStation Vita | `vita` | `.vpk`, `.mai`, `.psvita`, `.zip` | Yes | Yes | Serial (`PCSA`/`PCSE`/`PCSG`/`PCSB`) |
| PlayStation Portable | `psp` | `.iso`, `.cso`, `.pbp`, `.chd` | Some | Some | Serial (`ULES`/`ULUS`/`UCES`/`UCUS`) |
| Nintendo 3DS | `3ds` | `.3ds`, `.cia` | Yes | Yes | TitleID prefix-based |
| Nintendo Wii U | `wiiu` | `.wud`, `.wux`, `.rpx`, `.wua` | Yes | Yes | TitleID prefix-based |
| Xbox 360 | `xbox360` | `.iso`, `.xex`, `.god` | Yes | Yes | TitleID (8-hex) + content type folder |

Platforms not listed here (NES, SNES, N64, GB/GBC/GBA, NDS, all Sega, all Atari, Arcade, etc.) are cartridge/disc-based systems without separate update or DLC file concepts.

---

## Nintendo Switch

### ID System

Every Switch title has a **Title ID** — a 16-character hexadecimal string embedded in NCA metadata and commonly included in filenames.

The relationship between base game, update, and DLC Title IDs is **deterministic** based on bit flags in the last 4 hex digits:

| Content Type | Bit Flag | Last 4 hex pattern | Example (Bayonetta 2) |
|-------------|----------|--------------------|-----------------------|
| Base game | Neither bit 11 nor 12 | `X000` (X even) | `01004A4000B3A000` |
| Update / Patch | Bit 11 set (0x800) | `X800` (X even) | `01004A4000B3A800` |
| DLC | Bit 12 set (0x1000) | `Y001`–`YFFF` (Y = X+1, odd) | `01004A4000B3B001` |

**Detection algorithm used by RetroArr** (`TitleCleanerService.ClassifySupplementaryContent`):
```
last4 = parse last 4 hex digits as integer
if (last4 & 0x800) != 0 AND (last4 & 0x1000) == 0  → Patch
if (last4 & 0x1000) != 0                             → DLC
otherwise                                             → Base game
```

**Version numbers** in Switch filenames use Nintendo's internal uint32 format:

| Display version | Raw uint32 | Hex |
|----------------|-----------|-----|
| v0 (base, no update) | `0` | `0x0` |
| v1.0.0 | `65536` | `0x10000` |
| v2.0.0 | `131072` | `0x20000` |
| v3.0.0 | `196608` | `0x30000` |

Each minor increment is typically `+4096` (0x1000).

### Folder Structures

RetroArr supports three layout variants. Files can be placed at the platform root or inside per-game subfolders.

```
# Recommended: ID-tagged filenames (highest detection confidence)

/media/switch/
├── Bayonetta 2 [01004A4000B3A000][v0].nsp              ← base
├── Bayonetta 2 [01004A4000B3A800][v65536].nsp           ← update (v1.0.0)
├── Bayonetta 2 - DLC Pack 1 [01004A4000B3B001][v0].nsp  ← DLC 1
└── Bayonetta 2 - DLC Pack 2 [01004A4000B3B002][v0].nsp  ← DLC 2
```

```
# Per-game folder with supplementary subfolders

/media/switch/
└── Bayonetta 2/
    ├── Bayonetta 2 [01004A4000B3A000][v0].nsp            ← base
    ├── Updates/
    │   └── Bayonetta 2 [01004A4000B3A800][v65536].nsp    ← update
    └── DLC/
        ├── DLC Pack 1 [01004A4000B3B001][v0].nsp         ← DLC 1
        └── DLC Pack 2 [01004A4000B3B002][v0].nsp         ← DLC 2
```

```
# Real-world flat layout (no IDs — uses keyword + version detection)

/media/switch/
├── bayonetta_2_v0.nsp                       ← base (v0)
├── bayonetta_2_v65536.nsp                   ← update (version > 0)
├── bayonetta_2_dlc_pack1_v0.nsp             ← DLC (keyword: dlc)
└── Bayonetta 2 Update v1.0.5.nsp            ← update (keyword: Update)
```

```
# Scene release flat layout (group tags stripped automatically)

/media/switch/
├── SpongeBob_SquarePants_The_Cosmic_Shake_NSW-LiGHTFORCE.nsp
│                           ← base (no Update/DLC keyword)
├── SpongeBob_SquarePants_The_Cosmic_Shake_Update_v1 0 3_NSW-LiGHTFORCE.nsp
│                           ← update (keyword: Update; "v1 0 3" normalized to v1.0.3)
├── sxs-persona_5_strikers_persona_legacy_bgm_dlc.nsp
│                           ← DLC (prefix "sxs-" stripped; keyword: dlc)
└── Persona_5_Strikers_DLC_NSW-SXS.nsp
                            ← DLC (trailing tag stripped; keyword: DLC)
```

### Scene Release Handling

RetroArr automatically handles scene release naming conventions:

1. **Trailing group tags** like `_NSW-LiGHTFORCE`, `_NSW-VENOM`, `_PS4-DUPLEX` are stripped before keyword matching
2. **Leading group prefixes** like `sxs-`, `venom-` are stripped
3. **Underscores** are normalized to spaces for keyword matching
4. **Space-delimited versions** are normalized: `v1 0 3` → `v1.0.3`, `v1 01` → `v1.01`
5. The platform tag (`NSW`) alone is not a content-type signal — it only confirms the platform

### Naming Convention Summary

| Content type | Filename patterns |
|-------------|-------------------|
| Base game | `Game [TitleID][v0].nsp`, `game_v0.nsp`, `Game.xci` |
| Update | `Game [TitleID(800)][v65536].nsp`, `game_update_v1.0.nsp`, `game_v65536.nsp` |
| DLC | `Game [TitleID(DLC)][v0].nsp`, `game_dlc.nsp`, `game_dlc_pack1.nsp` |

---

## PlayStation 4

### ID System

Every PS4 title has a **Title ID** in the format `CUSAXXXXX` (5 digits). Region prefixes vary:

| Prefix | Region |
|--------|--------|
| `CUSA` | USA / International |
| `PLAS` | Asia (Korea) |
| `PLJS`, `PLJM` | Japan |
| `PCJS` | Japan (digital) |

Base games, updates, and DLC all share the same `CUSAXXXXX` Title ID. Content type is distinguished by a **suffix** in the filename or the internal PKG metadata:

| Suffix | Content Type |
|--------|-------------|
| `-app` | Base game |
| `-patch` | Update / Patch |
| `-ac` | Additional Content (DLC) |

### Folder Structures

PS4 uses folder mode in RetroArr (each game is a directory).

```
# Standard PS4 layout with PKG files

/media/ps4/
└── Resident Evil 2/
    ├── CUSA09193-app.pkg                     ← base game
    ├── Patches/
    │   └── CUSA09193-patch_v1.02.pkg         ← update
    └── DLC/
        └── CUSA09193-ac_RE2_ClothesSet.pkg   ← DLC
```

```
# Flat layout with keyword detection

/media/ps4/
└── Resident Evil 2/
    ├── Resident_Evil_2.pkg                   ← base (no keyword)
    ├── Resident_Evil_2_Update_v1.02.pkg      ← update (keyword: Update)
    └── RE2_DLC_ClothesSet.pkg                ← DLC (keyword: DLC)
```

### Detection Signals (priority order)

1. **`-patch` suffix** in filename → Update
2. **`-ac` suffix** in filename → DLC
3. **`-app` suffix** in filename → Base
4. **Keyword `update` / `patch`** → Update
5. **Keyword `dlc` / `addon` / `season pass`** → DLC
6. **No signal** → Base game

---

## PlayStation 5

### ID System

PS5 uses `PPSAXXXXX` as the primary serial prefix. The content type suffix system is the same as PS4 (`-app`, `-patch`, `-ac`).

| Prefix | Region |
|--------|--------|
| `PPSA` | USA / International |
| `ELJS`, `ELJM` | Japan |

### Folder Structure

```
/media/ps5/
├── Game [PPSA01234]-app.pkg                  ← base
├── Game [PPSA01234]-patch_v1.05.pkg          ← update
└── Game [PPSA01234]-ac_DLC1.pkg              ← DLC
```

Detection works identically to PS4.

---

## PlayStation 3

### ID System

PS3 games use region-specific serial prefixes with 5 digits:

| Prefix | Type | Region |
|--------|------|--------|
| `BLES`, `BCES` | Blu-ray | Europe |
| `BLUS`, `BCUS` | Blu-ray | USA |
| `BLJM`, `BCJM`, `BLJS`, `BCJS` | Blu-ray | Japan |
| `BLAS`, `BCAS` | Blu-ray | Asia |
| `NPEB`, `NPEA` | PSN (digital) | Europe |
| `NPUB`, `NPUA` | PSN (digital) | USA |

On the PS3 filesystem, games are organized as:
- Base: `/dev_hdd0/game/[SERIAL]/`
- Updates: Installed into the same directory, overwriting files
- DLC: `/dev_hdd0/game/[SERIAL]/USRDIR/` or separate PKG

### Folder Structure

PS3 uses folder mode in RetroArr (games as directories, often `.ps3` or `.ps3dir` container extensions).

```
/media/ps3/
└── The Last of Us [BCES01584]/
    ├── PS3_GAME/
    │   └── USRDIR/
    │       └── ...
    ├── Patches/
    │   └── BCES01584-patch.pkg               ← update
    └── DLC/
        └── BCES01584-dlc-left_behind.pkg     ← DLC
```

```
# PKG-based flat layout

/media/ps3/
├── BCES01584.pkg                             ← base (serial in filename)
├── BCES01584_update_v1.02.pkg                ← update (keyword: update)
└── BCES01584_DLC_Left_Behind.pkg             ← DLC (keyword: DLC)
```

---

## PlayStation Vita

### ID System

PS Vita uses region-specific serial prefixes:

| Prefix | Region |
|--------|--------|
| `PCSA` | USA |
| `PCSE` | USA / English |
| `PCSG` | Japan / Asia |
| `PCSB` | Europe |
| `PCSH` | Asia |
| `PCSD` | Various |

### Folder Structure

```
/media/vita/
├── Persona 4 Golden [PCSE00120].vpk          ← base
├── Persona 4 Golden Update [PCSE00120].vpk    ← update (keyword: Update)
└── Persona 4 Golden DLC [PCSE00120].vpk       ← DLC (keyword: DLC)
```

DLC and updates are typically distributed as separate VPK or MAI files with the same serial as the base game.

---

## PlayStation Portable

### ID System

PSP uses region-specific serial prefixes:

| Prefix | Region |
|--------|--------|
| `ULES`, `UCES` | Europe |
| `ULUS`, `UCUS` | USA |
| `UCAS` | Asia |
| `ULJM`, `ULJS`, `UCJS` | Japan |
| `NPJH`, `NPEH`, `NPUH` | PSN (digital, Japan/Europe/USA) |

PSP DLC is less common as separate files. When present, DLC is typically bundled in EBOOT.PBP format within game-specific directories.

### Folder Structure

```
/media/psp/
├── Crisis Core - Final Fantasy VII [ULES01040].iso   ← base
└── Crisis Core - Final Fantasy VII [ULES01040] DLC.pbp ← DLC (keyword)
```

---

## Nintendo 3DS

### ID System

3DS titles use a 16-character hex Title ID. Content type is determined by the **high half** (first 8 hex digits):

| High half prefix | Content Type |
|-----------------|-------------|
| `00040000` | Application (base game) |
| `0004000E` | Update / Patch |
| `0004008C` | DLC (Add-on content) |

The low half (last 8 hex digits) is the unique game identifier shared across base, update, and DLC.

### Folder Structure

```
/media/3ds/
├── Game [00040000001B5000].cia                ← base (00040000 prefix)
├── Game Update [0004000E001B5000].cia         ← update (0004000E prefix)
└── Game DLC [0004008C001B5000].cia            ← DLC (0004008C prefix)
```

RetroArr currently classifies 3DS content primarily via filename keywords (`update`, `dlc`, `patch`) rather than parsing the Title ID prefix. The Title ID is extracted for matching purposes.

---

## Nintendo Wii U

### ID System

Wii U titles use a 16-character hex Title ID. Content type is determined by the high half:

| High half prefix | Content Type |
|-----------------|-------------|
| `00050000` | Application (base game) |
| `0005000E` | Update / Patch |
| `0005000C` | DLC |

### Folder Structure

Wii U uses folder mode in RetroArr.

```
/media/wiiu/
└── Mario Kart 8/
    ├── code/
    │   └── ...
    ├── Updates/
    │   └── Mario Kart 8 Update.wua           ← update
    └── DLC/
        └── Mario Kart 8 DLC.wua              ← DLC
```

RetroArr classifies Wii U content via filename keywords and subfolder names.

---

## Xbox 360

### ID System

Xbox 360 games use an **8-character hex Title ID** (e.g. `4D5307E6` for Halo 3). The content type is determined by the **Content Type folder** in the standard Xbox 360 content directory structure:

| Content Type Folder | Content |
|--------------------|---------|
| `00070000` | Games on Demand (GOD) |
| `000D0000` | Xbox Live Arcade (XBLA) |
| `000B0000` | Title Updates |
| `00000002` | DLC |

Standard Xbox 360 content path: `Content/0000000000000000/[TitleID]/[ContentTypeFolder]/`

### Folder Structure

```
# Standard Xbox 360 content layout

/media/xbox360/
└── Content/
    └── 0000000000000000/
        └── 4D5307E6/                         ← Title ID (Halo 3)
            ├── 00070000/                      ← GOD (base game)
            │   └── ...
            ├── 000B0000/                      ← Title Updates
            │   └── tu_00000005               ← update
            └── 00000002/                      ← DLC
                └── dlc_mappack1              ← DLC
```

```
# Simplified flat layout (keyword detection)

/media/xbox360/
├── Halo 3/
│   ├── default.xex                           ← base
│   └── DLC/
│       └── Halo 3 Mythic Map Pack.god        ← DLC (keyword + subfolder)
```

RetroArr classifies Xbox 360 content primarily via subfolder names (`Updates/`, `DLC/`) and filename keywords.

---

## PlayStation 1 / 2 (Serial Detection)

### ID System

PS1 and PS2 games share the same serial prefix families. RetroArr extracts serials from filenames and uses them for platform identification and parent-game matching.

| Prefix | Platform | Region / Type |
|--------|----------|---------------|
| `SLES`, `SCES` | PS1 / PS2 | Europe (retail) |
| `SLUS`, `SCUS` | PS1 / PS2 | USA (retail) |
| `SLPS`, `SLPM` | PS1 / PS2 | Japan (retail) |
| `SCCS`, `SLKA` | PS2 | Asia |
| `SCED`, `SCUD` | PS1 / PS2 | Development / demo discs |
| `PAPX`, `PCPX` | PS1 / PS2 | Promotional / promo discs |

**Dotted serial format**: Some PS1 demo/development discs use a `NNN.NN` serial format (e.g., `SCED_002.73`). RetroArr normalises this to `SCED00273` by stripping hyphens, underscores, and dots from the extracted serial.

### Folder Structure

PS1/PS2 games do not have a formal Update/DLC system. However, RetroArr recognises serials in filenames for accurate metadata matching.

```
/media/psx/
├── Crash Bandicoot (SCUS-94900).bin
├── Crash Bandicoot (SCUS-94900).cue
└── SCED_002.73.Autumn-Christmas Releases 96 (EU).bin   ← demo disc (SCED serial)
```

```
/media/ps2/
├── Final Fantasy X [SLUS-20312].iso
└── Gran Turismo 4 [SCUS-97328].iso
```

---

## Detection Priority

When multiple detection signals are present in a filename and they conflict, RetroArr applies this priority order:

| Priority | Signal | Confidence |
|----------|--------|-----------|
| 1 (highest) | **TitleID-based** (Switch bit flags, 3DS/WiiU prefix) | Very high |
| 2 | **PS4/PS5 content suffix** (`-patch`, `-ac`, `-app`) | High |
| 3 | **PlayStation serial** in filename | High (confirms platform, not content type) |
| 4 | **Subfolder name** (`Patches/`, `Updates/`, `DLC/`) | High |
| 5 | **Filename keywords** (`update`, `patch`, `dlc`, `addon`, `season pass`, `expansion`) | Medium |
| 6 | **Version > 0 without other signals** | Low (flagged for review) |
| 7 | **No signal** | Base game (default) |

If a TitleID-based classification disagrees with a keyword (e.g., TitleID says DLC but filename contains "update"), the **TitleID always wins** because it is embedded in the file metadata and is authoritative.

---

## Supplementary Folder Names

RetroArr recognizes these folder names as containing supplementary content. They are scanned separately and their files are linked to parent games:

`Updates+DLCs`, `Patches+DLCs`, `Updates`, `Patches`, `DLC`, `DLCs`, `Addons`, `Add-ons`, `Updates+DLC`, `Patches+DLC`

Files inside these folders are **never** treated as separate games. Instead, they are classified and linked to the parent game by serial, TitleID, or fuzzy title matching.

Within a game's own folder, these subfolder names also trigger classification:
- `Patches/`, `Updates/` → files classified as Patch
- `DLC/`, `DLCs/` → files classified as DLC

---

## Metadata Providers

RetroArr uses two metadata providers for game information, covers, screenshots, and descriptions. **IGDB** is the primary source; **ScreenScraper** is the fallback for retro and arcade platforms.

### IGDB (via Twitch API)

- **Endpoint**: `api.igdb.com/v4`
- **Auth**: OAuth2 via Twitch (`id.twitch.tv/oauth2/token`), requires Client ID + Secret
- **Config**: `config/igdb.json` or env vars `IGDB_CLIENT_ID` / `IGDB_CLIENT_SECRET`
- **Coverage**: All modern and retro platforms — games, covers, screenshots, artworks, genres, companies, release dates, alternative names, external IDs
- **Platform mapping**: Each platform in `PlatformDefinitions.cs` has an `IgdbPlatformId` used to filter search results

### ScreenScraper (screenscraper.fr)

- **Endpoint**: `api.screenscraper.fr/api2/jeuInfos.php`
- **Auth**: Dev credentials + optional user credentials
- **Config**: `config/screenscraper.json` or env vars
- **Coverage**: Strong for retro/arcade (NES, SNES, Mega Drive, PS1, PS2, Arcade, etc.)
- **Search modes**: By filename + system ID, or by file hash (SHA1/MD5/CRC)
- **Platform mapping**: Each platform has a `ScreenScraperSystemId` (see table below)

### ScreenScraper System ID Reference

ScreenScraper system IDs are sourced from the [ScreenScraper API](https://www.screenscraper.fr/webapi2.php) and verified against [Skyscraper](https://github.com/muldjord/skyscraper/blob/master/src/screenscraper.cpp) (`getPlatformId()`).

| Platform | SS ID | Platform | SS ID |
|----------|-------|----------|-------|
| NES | 3 | Mega Drive / Genesis | 1 |
| SNES | 4 | Master System | 2 |
| N64 | 14 | Game Gear | 21 |
| GameCube | 13 | Saturn | 22 |
| Wii | 16 | Dreamcast | 23 |
| Wii U | 18 | Sega CD | 20 |
| Switch | 225 | 32X | 19 |
| Game Boy | 9 | SG-1000 | 109 |
| GBC | 10 | PS1 | 57 |
| GBA | 12 | PS2 | 58 |
| NDS | 15 | PSP | 61 |
| 3DS | 17 | PC Engine | 31 |
| FDS | 106 | Neo Geo | 142 |
| Virtual Boy | 11 | Atari 2600 | 26 |
| Arcade (MAME/FBNeo) | 75 | Atari Lynx | 28 |
| 3DO | 29 | Amiga | 64 |
| C64 | 66 | ZX Spectrum | 76 |
| MSX | 113 | Amstrad CPC | 65 |

Platforms without a ScreenScraper system ID (PS3, PS4, PS5, Vita, Xbox, Xbox 360) rely exclusively on IGDB for metadata. PS3/PS4/PS5/Vita/Xbox are modern enough that IGDB has comprehensive coverage.

### Fallback behaviour

`GameMetadataService.SearchGamesAsync` queries IGDB first. If IGDB returns zero results **and** the platform has a `ScreenScraperSystemId`, it falls back to ScreenScraper using the filename and system ID.

---

## Public Databases

| Platform | Database | URL | Machine-readable | Coverage |
|----------|----------|-----|-----------------|----------|
| Nintendo Switch | blawar/titledb | `github.com/blawar/titledb` | Yes (JSON per region) | Comprehensive, community-maintained |
| Nintendo Switch | Switchbrew Title List | `switchbrew.org/wiki/Title_list/Games` | No (wiki) | Official reference |
| PlayStation 4 | OrbisPatches | `orbispatches.com` | Yes (API) | PS4 updates by Title ID |
| PlayStation 5 | ProsperoPatches | `prosperopatches.com` | Yes (API) | PS5 updates by Title ID |
| PlayStation 3 | PSDevWiki (PS3) | `psdevwiki.com/ps3` | No (wiki) | Technical reference |
| PlayStation 1/2 | redump.org | `redump.org` | No (web) | Disc serial database |
| Xbox 360 | Xbox Unity | `xboxunity.net` | Yes (database) | Title Updates archive |
| Nintendo 3DS | 3DSDB | `3dsdb.com` | No (web) | Community title database |
| Nintendo Wii U | WiiUBrew | `wiiubrew.org` | No (wiki) | Technical reference |

The platform-specific databases listed above are useful references for Title ID lookups but are not directly queried by RetroArr at this time. RetroArr queries IGDB and ScreenScraper only.
