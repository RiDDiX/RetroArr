## v1.0.83 (2026-03-20)

### Changes
- Add per-platform metadata source selection (IGDB/ScreenScraper) (239ddcd)

## v1.0.82 (2026-03-20)

### Changes
- Fix platform rules: PS Vita, PS3, PS4, PSP, PSX, Switch extensions + RetroBat aliases (100f814)

## v1.0.81 (2026-03-20)

### Changes
- Fix GOG patch downloads missing extensions + scanner creating duplicates (c8d254f)

## v1.0.80 (2026-03-19)

### Changes
- Fix GOG downloads missing file extensions (198c250)

## v1.0.79 (2026-03-19)

### Changes
- Render game description as HTML instead of raw text (823167f)
- Show GOG downloads in Downloads page with progress tracking (9e1e82c)

## v1.0.78 (2026-03-19)

### Changes
- Fix GOG download: use embed.gog.com API + redirect handling + token refresh (b5bb7df)

## v1.0.77 (2026-03-19)

### Changes
- Fix GOG downloads parser: handle nested language-wrapped format (ba60aae)

## v1.0.76 (2026-03-19)

### Changes
- Fix UNIQUE constraint crash in MetadataReview ConfirmMatch (7fb4a90)

## v1.0.75 (2026-03-19)

### Changes
- Fix scan regression: library root /media blocked + platform detection never used (e0a7957)

## v1.0.74 (2026-03-19)

### Changes
- Fix scanner randomly stopping + missing platforms with loose files (bcad635)

## v1.0.73 (2026-03-19)

### Changes
- Auto-export metadata images/videos to platform folders (RetroBat/Batocera/ES convention) (9eb543e)
- Add local media support: images + videos from RetroBat/Batocera folder convention (e017162)
- Fix ScreenScraper returning only cover: fetch full details via jeuInfos after search (795f623)

## v1.0.72 (2026-03-19)

### Changes
- Add scraper choice dialog for metadata rescan + dual-source candidates in metadata review (21a04d6)

## v1.0.71 (2026-03-19)

### Changes
- Fix ScreenScraper metadata not applied when selecting search results in Edit Game modal (773ac71)
- chore: bump version to v1.0.70 [skip ci] (d78b4c7)
- Add diagnostic logging to ScreenScraper search + lookup controller (6be3016)
- Fix ScreenScraper API: correct base URL, pass dev credentials to test endpoint, add error logging (a760128)
- chore: bump version to v1.0.69 [skip ci] (e8aa7ad)
- Initial commit (bec24f8)

## v1.0.70 (2026-03-19)

### Changes
- Add diagnostic logging to ScreenScraper search + lookup controller (6be3016)
- Fix ScreenScraper API: correct base URL, pass dev credentials to test endpoint, add error logging (a760128)
- chore: bump version to v1.0.69 [skip ci] (e8aa7ad)
- Initial commit (bec24f8)

## v1.0.69 (2026-03-19)

### Changes
- Initial commit (bec24f8)

## v1.0.68 (2026-03-19)

### Changes
- Rewrite Logging and Cleanup (4b241fd)
- Initial commit (6374e98)

## v1.0.67 (2026-03-18)

### Changes
- Fix ScreenScraper search: use jeuRecherche.php for text search (0ceed94)

## v1.0.66 (2026-03-18)

### Changes
- Add PS1 serial prefixes, ScreenScraper system IDs, update docs (404f428)

## v1.0.65 (2026-03-18)

### Changes
- fix: exclude folder-level operations from per-game rename in GameDetails (6d556b4)
- feat: supplementary file rename for DLC/Patch files (fe4f867)
- chore: bump version to v1.0.64 [skip ci] (85c143d)
- fix: space-version normalization always overwrites partial _versionRegex match (9a7e393)
- Fix Switch TitleID classification, add Vita/PSP serials, scene tag stripping, docs (b016d29)
- fix: scanner stops prematurely — per-platform exception isolation + faster batching (f861eae)
- fix: cleanup stale games + resync files on rescan (2fb49e9)
- chore: bump version to v1.0.63 [skip ci] (47c7aa2)
- feat: supplementary content detection (Updates, DLCs, Patches) (24fcacd)
- fix: raise IgdbPlatformId duplicate threshold in audit test (973bdd7)
- fix: update test expectations to match corrected GetPlatformFromExtension returns (7c2a000)
- feat: add 80+ platform definitions matching RetroBat system list (7ef646f)
- fix: correct platform mapping for Game Boy, GBC, Master System, Game Gear (afb0bc8)
- docs: redesign README with logo header, cleaner layout (afc1bd4)
- feat: revision detection, scanner backfill, Files tab fix, extended region support (41c7dbe)
- feat: region and language detection from ROM filenames (a1b9c27)

## v1.0.64 (2026-03-18)

### Changes
- fix: space-version normalization always overwrites partial _versionRegex match (9a7e393)
- Fix Switch TitleID classification, add Vita/PSP serials, scene tag stripping, docs (b016d29)
- fix: scanner stops prematurely — per-platform exception isolation + faster batching (f861eae)
- fix: cleanup stale games + resync files on rescan (2fb49e9)
- chore: bump version to v1.0.63 [skip ci] (47c7aa2)
- feat: supplementary content detection (Updates, DLCs, Patches) (24fcacd)
- fix: raise IgdbPlatformId duplicate threshold in audit test (973bdd7)
- fix: update test expectations to match corrected GetPlatformFromExtension returns (7c2a000)
- feat: add 80+ platform definitions matching RetroBat system list (7ef646f)
- fix: correct platform mapping for Game Boy, GBC, Master System, Game Gear (afb0bc8)
- docs: redesign README with logo header, cleaner layout (afc1bd4)
- feat: revision detection, scanner backfill, Files tab fix, extended region support (41c7dbe)
- feat: region and language detection from ROM filenames (a1b9c27)

## v1.0.63 (2026-03-18)

### Changes
- feat: supplementary content detection (Updates, DLCs, Patches) (24fcacd)
- fix: raise IgdbPlatformId duplicate threshold in audit test (973bdd7)
- fix: update test expectations to match corrected GetPlatformFromExtension returns (7c2a000)
- feat: add 80+ platform definitions matching RetroBat system list (7ef646f)
- fix: correct platform mapping for Game Boy, GBC, Master System, Game Gear (afb0bc8)
- docs: redesign README with logo header, cleaner layout (afc1bd4)
- feat: revision detection, scanner backfill, Files tab fix, extended region support (41c7dbe)
- feat: region and language detection from ROM filenames (a1b9c27)

## v1.0.62 (2026-03-18)

### Changes
- fix: scanner missing games + UNIQUE constraint violations (daafbe5)

## v1.0.61 (2026-03-18)

### Changes
- ci: improve GHA cache pruning to enforce 5GB cap (3ae235e)
- fix: prevent duplicate redacted headers on logging settings save (8b53b04)
- chore: bump version to v1.0.60 [skip ci] (d122b46)
- perf: fix timeout on resort scan, fix-platforms, and metadata review for large libraries (7cd3a2a)
- fix: resolve nullability warnings in CachedGameRepository (1b2bedd)

## v1.0.60 (2026-03-18)

### Changes
- perf: fix timeout on resort scan, fix-platforms, and metadata review for large libraries (7cd3a2a)
- fix: resolve nullability warnings in CachedGameRepository (1b2bedd)

## v1.0.59 (2026-03-17)

### Changes
- Initial release (c732ec7)

## v1.0.58 (2026-03-17)

### Changes
- feat: rename on server, region flags, PS3 title fix, import review workflow (ce5a1c3)

## v1.0.57 (2026-03-17)

### Changes
- perf: fix log flooding and slow library loading with 15K+ games (f7a2a76)

## v1.0.56 (2026-03-17)

### Changes
- feat: persistent structured logging system with NLog, per-feature files, correlation IDs, redaction, and Settings UI (4b35e81)
- feat: auto-refresh UI on library events and optimize performance (bc5765a)
- refactor: simplify navigation to 5 primary items (8787fa6)
- refactor: replace hardcoded colors with theme tokens (3a38645)
- fix: add missing CSS variable aliases and 6 new theme presets (7d92353)

## v1.0.55 (2026-03-17)

### Changes
- Fix game file download handler to avoid popup blocker (d41c3bd)

## v1.0.54 (2026-03-17)

### Changes
- Fix platform detection when library root IS a platform folder (7175cfc)

## v1.0.53 (2026-03-17)

### Changes
- Fix CI: add dotnet restore step to quality-gate in docker-build.yml (9322665)

## v1.0.52 (2026-03-17)

### Changes
- Fix re-scan platform correction for existing games (b5d98ff)

## v1.0.51 (2026-03-17)

### Changes
- Fix deterministic platform classification in Media Scanner (c75fc77)

## v1.0.50 (2026-03-17)

### Changes
- Harden codebase and CI/CD pipeline (5d62ef9)

## v1.0.49 (2026-03-17)

### Changes
- Add Game folder creation, post-download content detection and rename (0107b9f)

## v1.0.48 (2026-03-17)

### Changes
- Add batch platform fix: auto-detect correct platform from file path (4ca358f)
- Fix wrong platform assignment and Resort container extension handling (fb5df49)

## v1.0.47 (2026-03-17)

### Changes
- Add 3DO platform, update all platform extensions from Batocera/RetroBat audit, add FileSetResolver (7ad0ed0)

## v1.0.46 (2026-03-17)

### Changes
- Resort UI: add search, folder filter, and platform reassignment (b7e20d4)

## v1.0.45 (2026-03-17)

### Changes
- Fix resort: skip D1 when game is in a valid alternative platform folder (818a44f)
- Fix resort: file-mode games (ROMs) skip D2 rename, preserve filename (b802cfc)

## v1.0.44 (2026-03-16)

### Changes
- Add Resort/Rename system: detection, preview, apply engine + UI (403ed34)
- feat: add frontend compatibility mode (RetroBat/Batocera folder naming) (d288396)

## v1.0.43 (2026-03-16)

### Changes
- Fix core logic defects: import dedup, file sync, path resolution, metadata platform, GoG cancellation (833d1b5)

## v1.0.42 (2026-03-16)

### Changes
- fix: audit hardening — 11 defect fixes + tests (a71a97b)
- feat: enhance Map & Import with game selector + file type detection (9069bfe)

## v1.0.41 (2026-03-16)

### Changes
- (no tracked changes)

## v1.0.40 (2026-03-16)

### Changes
- fix: seed Platforms table on startup + harden GetGameFiles (af29c2d)
- fix: prevent 500 on GetGameFiles — add try-catch + remove dynamic cast (b134f03)

## v1.0.39 (2026-03-16)

### Changes
- fix: classify DLC files in SyncGameFilesFromDisk alongside Patches (fc9f230)
- refactor: deterministic ResolveGameFolder without fallback scan (7fdda91)
- fix: ResolveGameFolder with fallback scan + auto-persist game.Path (505d66a)
- feat: DLC subfolder routing + fix Game Files not showing (8a8630b)
- feat: auto-detect [Update]/[Patch] releases and import to Patches/ subfolder (d20c349)

## v1.0.38 (2026-03-16)

### Changes
- fix: multi-platform game support in download import pipeline (a50c063)

## v1.0.37 (2026-03-16)

### Changes
- feat: show game name in Downloads Queue and Unmapped tabs (3e64101)

## v1.0.36 (2026-03-16)

### Changes
- feat: GameFile tracking, patch routing, create-from-file, platform ID fix (4fd1da8)
- fix: MergeMetadataIntoExisting now copies Path/ExecutablePath from scanner (9b6d0c9)

## v1.0.35 (2026-03-16)

### Changes
- fix: UpdateAsync calls require (int id, Game game) signature (0879452)

## v1.0.34 (2026-03-16)

### Changes
- feat: Downloads menu with hover sub-menu, Not Mapped Files tab, Patches tab (166e9f1)
- feat: Sonarr/Radarr-style game folder management + targeted download import (67e2a3e)

## v1.0.33 (2026-03-16)

### Changes
- feat: game files listing + download + GOG download-to-folder in GameDetails (3c4b507)

## v1.0.32 (2026-03-16)

### Changes
- fix: TryFetchMetadata IGDB ID dedup now platform-aware (1c46f53)

## v1.0.31 (2026-03-16)

### Changes
- fix: platform scan not finding games — 4 bugs fixed (cefa930)

## v1.0.30 (2026-03-16)

### Changes
- fix: resolve 3 C# compilation errors in MediaController metadata rescan (cf575b2)

## v1.0.29 (2026-03-16)

### Changes
- feat: Library redesign with platform sidebar, per-platform scan/rescan, lint fixes (c9ff6ae)
- fix: EmulatorController PlatformIdToCore used IGDB IDs instead of internal IDs (b624caa)

## v1.0.28 (2026-03-16)

### Changes
- fix: remove hardcoded Xbox/macOS filter from Library platform list (c9ebdee)

## v1.0.27 (2026-03-16)

### Changes
- fix: add missing DB migration columns for metadata review fields (2344ad6)
- fix: scanner platform detection — match by FolderName OR Slug, add missing platform rules (0bbff23)

## v1.0.26 (2026-03-16)

### Changes
- feat: harden metadata scraper with multi-variant search, scoring, and review queue (c5ea2a4)

## v1.0.25 (2026-03-16)

### Changes
- fix: download activity badge stuck at stale count (3e20293)

## v1.0.24 (2026-03-16)

### Changes
- fix: complete platform rule coverage for all 70+ platforms (152f9d7)
- fix: MediaScanner platform detection and persistence (c6f88f1)
- fix: replace hardcoded Spanish tooltip in GameCard with t('deleteFromLibrary') (033aaf4)
- fix: set PlatformId during import so games appear in library under correct platform (0a802af)

## v1.0.23 (2026-03-15)

### Changes
- fix: coerce state to string before toLowerCase in Status.tsx (numeric enum from API) (d9ae5a2)
- fix: update About page with current features, dynamic version, remove outdated DBI/USB references (da28ea5)

## v1.0.22 (2026-03-15)

### Changes
- fix: add missing #endregion for Request/Response Models region (3f1162c)

## v1.0.21 (2026-03-15)

### Changes
- feat: download tracking system with History, Blacklist, Unmapped, and tabbed Status UI (7db59f1)
- Fix false Imported state: PostDownloadProcessor now returns PostDownloadResult (c3a5286)

## v1.0.20 (2026-03-15)

### Changes
- Refactor post-download to Sonarr-inspired tracked download pipeline (af19ad8)

## v1.0.19 (2026-03-15)

### Changes
- Add category filter to DownloadMonitor, improve game name cleaning at import (00f161b)

## v1.0.18 (2026-03-15)

### Changes
- Fix download processing: null-safety, safe SABnzbd parsing, manual import button (a72007b)

## v1.0.17 (2026-03-15)

### Changes
- Fix DownloadPlatformTracker crash: handle non-URL entries in LookupByName (e00bd8a)

## v1.0.16 (2026-03-15)

### Changes
- Add platform-aware download processing, category filtering, and manual platform mapping (b92e174)

## v1.0.15 (2026-03-15)

### Changes
- Fix Status page: replace hardcoded localhost:5002 URLs with apiClient for Docker/production compatibility (c103cda)

## v1.0.14 (2026-03-15)

### Changes
- Update dependencies: eslint 9 flat config, react-fontawesome v3, Node 22, remove deprecated packages (e3c15b5)

## v1.0.13 (2026-03-15)

### Changes
- Fix download routing: recognize 'usenet' protocol for SABnzbd/NZBGet client selection (f4035ba)

## v1.0.12 (2026-03-15)

### Changes
- UI: fix platform tag contrast, smart back button, modern translucent badges (e91fa7f)

## v1.0.11 (2026-03-15)

### Changes
- Align Prowlarr integration with official API spec: add grabs, subcategories, category IDs (fbdccf7)
- Fix Prowlarr search returning 0 results: add required limit parameter to API call (bc2b301)

## v1.0.10 (2026-03-15)

### Changes
- Optimize Docker build: enable GHA layer cache, add cache mounts for npm/NuGet, reduce context size (5278016)
- Replace hardcoded changelog with dynamic version/changelog from backend API (56f8f52)

## v1.0.9 (2026-03-15)

### Changes
- Add editable search query field + smarter query sanitization for better indexer results (746afb8)

## v1.0.8 (2026-03-15)

### Changes
- Fix search pipeline: correct IndexerFlags type, Jackett error/protocol handling, age display (8195282)
- Sanitize search query: strip colons and special chars that break indexer searches (ea75065)

## v1.0.7 (2026-03-15)

### Changes
- Fix Prowlarr search: IndexerFlags type mismatch + remove silent error swallow (935bf9f)
- Fix scanner UNIQUE constraint crash when game already matched by IgdbId (cdc099e)

## v1.0.6 (2026-03-14)

### Changes
- Fix missing PreferredRunner column in database migration (e397e77)
- Upgrade CI: opt into Node.js 24, bump docker/build-push-action to v6 (5ba915e)
- Fix search returning silent empty results: surface provider diagnostics to frontend (14a79ac)

## v1.0.5 (2026-03-14)

### Changes
- RetroArr v1.0.4 — Self-hosted game library manager & PVR (247209b)

## v1.0.4 (2026-03-14)

### Changes
- Rewrite README: fix platform count, add missing features, add TOC/config/dev sections (02b81ef)

## v1.0.3 (2026-03-14)

### Changes
- Replace roadmap with Linux Gaming, Plugin System, and App Store sections (e03ea37)
- Add tests for Linux exports, plugin loader, and plugin executor (1574ae3)
- Add Linux export UI and register services in DI container (5f991e4)
- Update app store manifests for Unraid, CasaOS, and Synology (3c3a74e)
- Add plugin engine with fault isolation, circuit breaker, and example plugins (94b260c)
- Add Linux gaming integrations: Proton/Wine runner detection, Lutris/Steam/desktop exports (6670723)
- fix: Prowlarr release search returns no results due to nullable type mismatch (efe7c48)
- refactor: core logic audit rewrite (R1-R12) (bcf0e63)
- chore: bump version to v1.0.1 [skip ci] (634abf8)

## v1.0.2 (2026-03-14)

### Changes
- Fix Docker build: remove deleted app_logo.ico COPY, regenerate ico (05b48a3)
- chore: bump version to v1.0.1 [skip ci] (976e0fd)
- Replace hardcoded colors in TSX inline styles with theme variables (9448b2c)
- Consolidate theme system: replace hardcoded colors with CSS variables (94bc603)
- Remove unused assets and dependencies, optimize images (07d4570)
- Wire up centralized API client and refactor Settings shell (858834a)
- Extract shared UI components and Settings tab sub-components (dbb8063)
- feat: RetroArr v1.0.0 - Self-hosted game library manager & PVR (261613b)

## v1.0.1 (2026-03-14)

### Changes
- Replace hardcoded colors in TSX inline styles with theme variables (9448b2c)
- Consolidate theme system: replace hardcoded colors with CSS variables (94bc603)
- Remove unused assets and dependencies, optimize images (07d4570)
- Wire up centralized API client and refactor Settings shell (858834a)
- Extract shared UI components and Settings tab sub-components (dbb8063)
- feat: RetroArr v1.0.0 - Self-hosted game library manager & PVR (261613b)

## v1.0.0 (2026-03-14)

### RetroArr - Initial Public Release

- Self-hosted game library manager & PVR inspired by Radarr/Sonarr
- Intelligent library scanning with 80+ platform support
- Rich metadata integration (IGDB, Steam, ScreenScraper)
- Download client support (qBittorrent, Transmission, Deluge, SABnzbd, NZBGet)
- Prowlarr and Jackett indexer integration
- EmulatorJS web player with save states for retro platforms
- Steam and GOG library sync
- Modern React frontend with code splitting and React Query
- Docker support (amd64/arm64) with pre-downloaded emulator cores
- Multi-language UI (EN, DE, ES, FR, RU, ZH, JA)
- GitHub Actions CI/CD with GHCR container publishing
