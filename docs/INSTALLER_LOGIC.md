# RetroArr Installer Logic

> **Note:** This is a design document describing the intended handling logic for various installer and game container types. The scanner currently detects file types and marks installer candidates (`GameStatus.InstallerDetected`), but does not execute ISO mounting, silent installs, or RAR extraction automatically.

This document defines the logic for handling different types of games and installers in RetroArr.

## General Scanning Rules (Discovery Rules)
For all priorities described below, the `MediaScannerService` must apply these search rules:
1.  **Flexible Patterns (Fuzzy Name):** Don't search for exact strings. Use patterns: `setup*.exe`, `install*.exe`, `installer.exe`.
2.  **Depth (Limited Recursion):** Scan the download root folder AND the first level of subdirectories (`Depth = 1`). This is vital for downloads that come inside a container folder (e.g., `/Downloads/Game/Setup/setup.exe`).

---

## 1. Disk Images (Classic "Scene")

1:1 copies of original physical or digital discs. Standard from groups like CODEX, RUNE, PLAZA.

* **Container File:** `.iso` (99%), sometimes `.bin` + `.cue`, `.mdf`, `.nrg`.
* **Internal Structure:**
    ```text
    [Game.iso]
    ├── setup.exe        <-- (Or setup_game.exe)
    ├── autorun.inf
    ├── data.bin
    └── CODEX/           <-- Crack folder
    ```

### How RetroArr Handles It
1.  **Mount:** Execute native mount command.
2.  **Install:** Search for executable matching pattern `setup*.exe` or `install*.exe` on mounted drive.
3.  **Patch:** Copy `CODEX`/`RUNE`/`PLAZA` folder if it exists.

---

## 2. Repacks and Native Installers

Popular (FitGirl, DODI, GOG Offline Installers).

* **Container File:** Folder with loose files.
* **Internal Structure:**
    ```text
    [Downloaded Folder]
    ├── setup.exe                     <-- Scene standard
    ├── setup_example_game_v1.5.exe  <-- GOG standard
    ├── installer.exe                 <-- Generic standard
    ├── data-01.bin
    └── MD5/
    ```

### How RetroArr Handles It
1.  **Search:** Scan Root and Subfolders (Level 1) looking for `setup*.exe` or `install*.exe`.
2.  **Validation:** If there are multiple, prioritize the one containing the game name or the largest one.
3.  **Install:** Execute with automation arguments (`/SILENT`, `/VERYSILENT`, `/SP-`, `/SUPPRESSMSGBOXES`, `/NOCANCEL`).

---

## 3. Portables or Pre-installed (SteamRIP)

The easiest. Already installed and compressed.

* **Container File:** `.zip`, `.7z`, `.rar` (single archive).
* **Internal Structure:**
    ```text
    [Game.zip]
    └── GameName/
        ├── Game.exe
        └── Data/
    ```

### How RetroArr Handles It
1.  **Extract:** Extract to `/Library`.
2.  **Play:** Execute "Executable Discovery" logic.

---

## 4. Multi-Volume RAR Extraction

Games split into multiple RAR volumes.

* **Container File:** `.rar`, `.r00`, `.r01`, `part1.rar`...
* **Internal Structure:** Extracting the first one generates the game folder.

### How RetroArr Handles It
1.  **Join:** Detect sequence.
2.  **Extract:** Decompress main volume.
3.  **Patch:** Look for `Crack` folder after extraction.

---

## 5. Complex Installation Scripts (GOG Multipart)

GOG installers split into binaries.

* **Container File:** `setup_game.exe` + `setup_game-1.bin`.

### How RetroArr Handles It
1.  **Detection:** Ensure the `.exe` and `.bin` files are in the same folder.
2.  **Execution:** Treat the same as Priority 2 (Native Installer) with silent arguments.

---

## 6. WINE-Prefix Games (Linux/Mac)

Pre-configured folders for Wine.

* **Container File:** `drive_c` folders or Bottle-type structures.

### How RetroArr Handles It
1.  **Launch:** Don't execute the `.exe` directly, call `wine` or `proton` instead.

---

## 7. Steam Dump Extraction

Raw Steam files (Depots).

* **Container File:** Numeric folders (AppID).

### How RetroArr Handles It
1.  **Mapping:** Use AppID to rename folder.
2.  **Emulator:** Inject `steam_api64.dll` (Goldberg/SSE).

---

# Extended Logic Summary

Updated detection hierarchy:

| Priority | If found... | RetroArr Action |
| :--- | :--- | :--- |
| 1 | `.iso` / `.bin` | **Mount** -> Search `setup*.exe` |
| 2 | `setup*.exe` / `install*.exe` | **Execute** (Silent Mode) |
| 3 | `.rar` / `.zip` / `.7z` | **Extract** -> Search Executable |
| 4 | Folder with `steam_api.dll` | **Portable** -> Apply Emu |
| 5 | Folder only | **Scan** -> Identify main executable |
