# Scanning and Detection Logic

This document describes the architecture of RetroArr's media scanner, focused on precision, speed, and duplicate elimination.

## 1. Hierarchical Discovery Phase

Starting with version **v0.4.2**, RetroArr uses a recursive hierarchical discovery system instead of flat enumeration.

### Branch Skipping
To optimize performance, the scanner ignores **entire branches** of the directory tree if the folder name is in the `_folderBlacklist`.
- [x] **Ignored Folders:** `shadercache`, `compatdata`, `node_modules`, `temp`, `Redist`, `DirectX`, `PPPwnGo`, `GoldHEN`, `Python!+Npcap` etc.
- [x] **Allowed:** `steamapps` and `common` are no longer blocked to allow scanning Steam libraries.
- **Result:** The scanner can dive deep into application system folders like Steam while maintaining optimal speed by ignoring cache and heavy irrelevant data.

## 2. "Nuclear" Title Cleaning (v0.4.6)

To ensure accurate IGDB matches (especially for Switch and PS4), an aggressive cleaning strategy is applied before tokenization:

1.  **Serial Extraction:** Switch identifiers (16-hex) and PlayStation (CUSA, SLPS, etc.) are extracted *before* cleaning, preserving game identity.
2.  **Bracket Removal:** All content within `[]`, `()`, `{}` and `［］` is removed to erase scene tags, versions, and metadata.
3.  **Size Pattern Filtering:** Strings like "2.90GB", "100MB" are removed.
4.  **Regional Noise Filtering:** 2-letter regional tags (`US`, `EU`, `JP`, etc.) and noise words (`repack`, `fitgirl`, `opoisso893`) are explicitly removed.
5.  **Anti-Version Artifacts:** Patterns like `v1.00`, `A0100` and the word "00" are removed to avoid false positives (e.g., "00 Dilly").
6.  **Sequel Preservation:** The numeric rule has been relaxed to allow titles like "Streets of Rage 4".

## 3. Folder Clustering

The scanner applies a **"Winner Takes All"** logic.
1.  All valid files found are grouped by their **Parent Folder**.
2.  All candidates from that folder are evaluated using the scoring system.
3.  **Only one game is added per folder** (Winner Takes All), avoiding duplicate entries.
4.  **EXCEPTION (v0.4.1+):** This logic is disabled (No-Clustering) for console/retro extensions (`.iso`, `.nsp`, `.pkg`, etc.). Each file is treated as unique.

## 4. Scoring System

Each candidate file receives a score to determine if it's the main executable:

| Points | Criteria |
| :--- | :--- |
| **+100** | File name exactly matches folder name. |
| **+90** | Partial match or no spaces/hyphens with folder name. |
| **+50** | Priority names: `AppRun`, `Start.sh`. |
| **+25** | Location in standard folders: `binaries`, `win64`, `shipping`, `retail`. |
| **+20** | Largest file in folder (Size tiebreaker). |
| **-50** | Penalty keywords: `setup`, `install`, `launcher`, `config`, `settings`. |

## 5. Global Blacklists

### Keywords
If the file name contains any of these terms, it's immediately ignored:
`steam_api`, `crashpad`, `unitycrash`, `vcredist`, `bios`, `firmware`, `updater`, `unins000`.

### Hidden/Junk Files
- Files starting with `._` (macOS metadata) are explicitly ignored.
- Known exploit tool folders (`PPPwnGo`, `GoldHEN`) are ignored.

### Forbidden Extensions in Folders
In folder scanning mode (PC), files that are typically libraries or data are ignored:
`.dll`, `.so`, `.lib`, `.a`, `.bin` (except on known retro platforms).

## 6. Linux-Specific Support

*   **Extensionless Binaries:** On Linux systems, files without extensions are considered valid candidates.
    *   **Header Verification (Security):** The first 4 bytes of the file are read to confirm it contains an **ELF** header (`0x7F 'E' 'L' 'F'`) or a shebang (`#!`), thus discarding plain text files like `LICENSE` or `README` that have no extension.
*   **AppRun Priority:** Native support for running extracted AppImages or Linux game dumps that use the `AppRun` standard.

## 7. Platform Detection by Extension and Serial

The system analyzes both the extension and name content (serial) to assign a precise platform:

### Modern Consoles
*   **Nintendo Switch:** `.nsp`, `.xci`, `.nsz`, `.xcz` -> `nintendo_switch`
*   **PlayStation 4/5:** `.pkg` -> `ps4` / `ps5`
    *   **Note:** `.bin` support for PS4 was removed to avoid false positives with exploit payloads.
    *   **Serials:** Detects `CUSA`, `PPSA`, `PLJS`, `ELJS`, etc.

### PlayStation Global (PS1-PS5)
The scanner recognizes serials from **all regions** (USA, EUR, JP, Asia) to identify the correct console:
*   **PS1/PS2:** `SLES`, `SLUS`, `SCES`, `SCUS`, `SLPS`, `SLPM`, `SCCS`, `SLKA`
*   **PS3:** `BLES`, `BLUS`, `BCES`, `BCUS`, `NPEB`, `NPUB`, `BLJM`, `BCAS`, etc.

### macOS
*   **Extensions:** `.dmg`, `.app` -> `macos`

### Retro Emulation (No-Cluster)
*   **Nintendo 64:** `.z64`, `.n64`, `.v64` -> `nintendo_64`
*   **SNES:** `.sfc`, `.smc` -> `snes`
*   **NES:** `.nes` -> `nes`
*   **GameBoy:** `.gb`, `.gbc`, `.gba` -> `gameboy_advance`
*   **Sega:** `.md`, `.gen`, `.smd`, `.sms`, `.gg` -> `sega_genesis`
*   **PC Engine:** `.pce` -> `pc_engine`

### PC / Default
*   **Clustering:** Only active for `.exe`, `.bat`, `.sh`.
*   **ISO:** `.iso` images are treated as individual games (One File = One Game).

## 8. Updates, Patches & DLC

For platforms that support separate update/patch/DLC files (Switch, PS3, PS4, PS5, Vita, PSP, 3DS, Wii U, Xbox 360), the scanner classifies files by TitleID bit flags, serial suffixes, filename keywords, and subfolder context. See the full guide: [Updates & DLC](UPDATES_DLC_GUIDE.md).
