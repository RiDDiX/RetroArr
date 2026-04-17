# RetroArr CORE: Game Launcher & Execution Architecture

**Context:** This document defines the logic of the `LauncherService`. Its responsibility is to determine the correct executable and launch it by delegating to the host OS.

---

## 1. Design Architecture (The Strategy Pattern)

The **Strategy** pattern is maintained for scalability.

### 1.1 Interfaces
```csharp
public interface ILaunchStrategy {
    bool IsSupported(Game game);
    Task LaunchAsync(Game game);
}
```

### 1.2 Registered Strategies
*   **`SteamLaunchStrategy`** — Launches via `steam://run/` protocol.
*   **`GogLaunchStrategy`** — Launches via `goggalaxy://runGame/` protocol.
*   **`NativeLaunchStrategy`** — Direct executable launch with OS-specific logic for Windows, macOS, and Linux.

---

## 2. Executable Discovery Algorithm

The scoring heuristic is maintained to automatically find the correct `.exe` or binary.

### 2.1 Scoring System
*   **Blacklist (Ignore):** `unins`, `setup`, `config`, `dxsetup`, `vcredist`, `crashhandler`.
*   **Positive Scoring:**
    *   **+100 pts:** File name == Parent folder name (exact match).
    *   **+90 pts:** File name == Parent folder name (normalized, ignoring spaces/dashes).
    *   **+50 pts:** Priority names (`AppRun`, `Start.sh`).
    *   **+30 pts:** Partial name match (folder name contains file name, length > 4).
    *   **+25 pts:** Key subfolders: `binaries`, `win64`, `release`, `shipping`, `retail`.
    *   **+20 pts:** Largest file bonus.
    *   **+10 pts:** Native Linux executable (extensionless binary on Linux).
*   **Negative Scoring:**
    *   **-50 pts:** Names containing `launch`, `settings`, `server`, `config`, `setup`, `install`.

### 2.2 Folder Structure Patterns
To improve Scoring precision, the system must first identify what type of folder structure we're dealing with.

#### A. The "Deep Nested" Pattern (Unreal/Unity Games)
The modern standard (AAA and many Indies). The executable in the root is usually just a fake launcher or bootstrap, while the real executable is hidden in subdirectories.

*   **Search Logic:** If no clear candidate is found in the root, descend recursively looking for key folders:
    *   `Binaries/Win64` (Standard Unreal Engine)
    *   `GameData` (Unity)
    *   `Bin`
*   **Golden Rule:** If two executables with the same name exist (one in root and one in subfolder), prioritize the larger one in the subfolder (usually the real binary). The root one is only valid if its size is < 5MB (typical Steam wrapper) and there's no better option.

#### B. The "Scene Release" Pattern (Junk Folders)
Downloads from unofficial sources (torrent/repacks) often include extra folders that **MUST** be ignored during scanning to avoid false positives and noise.

*   **Folder Blacklist (Recursive Skip):**
    *   `_CommonRedist`
    *   `Support`
    *   `DirectX`
    *   `Crack`, `CODEX`, `RUNE`, `SKIDROW`, `TENOKE` (Unapplied crack folders).
    *   `BonusContent`, `Soundtrack`, `Artbook`.

#### C. The "Installer" Trap (Non-Game Detection)
Before assigning an `ExecutablePath`, the system must verify if what it found is actually the game installer and not the game itself.

*   **Alert Heuristic:** The winning candidate has names like `setup.exe`, `install.exe` or `unins000.exe`.
*   **Action:**
    1.  Mark `GameStatus.InstallerDetected`.
    2.  **UI:** Change "Play" button to "Install / Setup".
    3.  **Logic:** Don't try to launch as silent native game; requires user interaction.

---

## 3. OS-Specific Launch Logic (NativeLaunchStrategy)

All three OS paths are handled by `NativeLaunchStrategy`, which delegates based on `RuntimeInformation.IsOSPlatform`.

### 3.1 Windows
*   **Target:** Windows 10/11.
*   **Logic:** Direct `Process.Start` with `UseShellExecute = true`.
*   **Requirement:** Set `WorkingDirectory` to the executable's folder.

### 3.2 macOS
*   **Target:** macOS.
*   **Philosophy:** "File Association Delegate". We don't try to force a specific tool. We let macOS decide how to open the file.
*   **Logic:**
    *   **Command:** `/usr/bin/open`
    *   **Arguments:** `"{executablePath}"`
*   **Expected Behavior:**
    *   If `.app`: Opens natively.
    *   If `.exe`: Will open with the application the user has associated by default (Whisky, Crossover, Wine) or show a system error if there's none. RetroArr doesn't manage compatibility, it just invokes the file.

### 3.3 Linux
*   **Target:** Linux Desktop / Steam Deck / Bazzite.
*   **Logic:**
    *   Native binary detection (ELF) vs Windows (.exe).
    *   **Runner Resolution Chain:** Per-game `PreferredRunner` field → auto-detect.
    *   **Auto-detect order:** Proton (if Steam installed) → Wine → error.
    *   Proton paths scanned: `~/.steam/steam/steamapps/common/Proton*`, `~/.local/share/Steam/steamapps/common/Proton*`, `~/.steam/root/steamapps/common/Proton*`, `/usr/share/steam/steamapps/common/Proton*`.
    *   Sets `STEAM_COMPAT_DATA_PATH` and `STEAM_COMPAT_CLIENT_INSTALL_PATH` for Proton launches.
*   **Export Integrations:** Lutris YAML, Steam Shortcuts (Deck Game Mode), XDG `.desktop` files via `/api/v3/linux/export/`.

---

## 4. UI Flow
1.  **Play:** Verify path -> Launch strategy.
2.  **Configure:** Allow manual executable selection.

---

### Why `open` on macOS
Letting macOS handle the file association means we don't reimplement Whisky/CrossOver logic:
* If `open` fails, it's a Finder/macOS issue — surfaces faster than a custom error path.
* If the user already set `.exe` files to open with Whisky in Finder, RetroArr inherits that — no "bottles" or `Z:` path juggling on our side.
