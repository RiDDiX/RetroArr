# BEFORE Performance Baseline (2026-03-14)

## Bundle Sizes
| File | Size |
|------|------|
| bundle.53d01db3.js (main) | 2.0 MiB |
| FA vendor chunk | 1.4 MiB |
| Settings chunk | 245 KiB |
| GameDetails chunk | 218 KiB |
| Library chunk | 121 KiB |
| Problems chunk | 37 KiB |
| Dashboard chunk | 32 KiB |
| Collections chunk | 31 KiB |
| Statistics chunk | 29 KiB |
| Status chunk | 25 KiB |
| Settings CSS chunk | 25 KiB |
| FolderExplorerModal chunk | 21 KiB |
| User chunk | 19 KiB |
| About chunk | 6.3 KiB |
| **Total UI output** | **8.3 MiB** |
| - chunks/ | 2.2 MiB |
| - platforms/ (SVG/ICO icons) | 1.1 MiB |
| - PNG images | ~3.0 MiB (2x 1.4MB logo dupes) |

## Build Time
- Frontend (webpack): ~2.8s

## Polling Intervals (from code)
| Location | Interval | What |
|----------|----------|------|
| hooks.ts:176 (useScanStatus) | 3000ms | Scan status polling via React Query |
| ScannerStatus.tsx:54 | 3000ms | Duplicate scan status poll (setInterval) |
| Status.tsx:39 | 3000ms | Download queue polling |
| Settings.tsx:624 | setInterval | Library scan progress |
| Settings.tsx:924 | setInterval | GOG sync progress |
| DebugConsole.tsx:236 | setInterval | Debug log refresh |
| SwitchInstallerModal.tsx:69 | setInterval | Switch install progress |
| index.tsx:86 | setInterval | Startup check (5s timeout) |

## Known Issues
- ScannerStatus + useScanStatus = duplicate polling for same endpoint
- No virtualization for game lists
- 2x 1.4MB PNG dupes (app_logo.png and nav_eye.png are identical content)
- FontAwesome vendor chunk is 1.4 MiB (tree-shaking not optimal)
