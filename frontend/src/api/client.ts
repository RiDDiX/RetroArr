import axios from 'axios';

const apiClient = axios.create({
  baseURL: '/api/v3',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Attach a unique X-Request-Id header to every request for log correlation
apiClient.interceptors.request.use((config) => {
  const id = Math.random().toString(36).substring(2, 14);
  config.headers = config.headers || {};
  config.headers['X-Request-Id'] = id;
  return config;
});

export default apiClient;

export const isAxiosError = axios.isAxiosError;

// ===== Error Helpers =====

export function getErrorMessage(error: unknown, fallback = 'An error occurred'): string {
  if (axios.isAxiosError(error)) {
    return error.response?.data?.message || error.response?.data?.error || error.response?.data || error.message || fallback;
  }
  if (error instanceof Error) {
    return error.message;
  }
  return fallback;
}

export function isTimeoutError(error: unknown): boolean {
  if (axios.isAxiosError(error)) {
    return error.code === 'ECONNABORTED' || (error.message?.includes('timeout') ?? false);
  }
  return false;
}

// ===== Type Definitions =====

export interface Game {
  id: number;
  title: string;
  alternativeTitle?: string;
  year: number;
  overview?: string;
  storyline?: string;
  platformId: number;
  platform?: Platform;
  added: string;
  images: GameImages;
  genres: string[];
  availablePlatforms?: string[];
  developer?: string;
  publisher?: string;
  releaseDate?: string;
  rating?: number;
  ratingCount?: number;
  status: string;
  monitored: boolean;
  path?: string;
  sizeOnDisk?: number;
  gameFiles: GameFile[];
  igdbId?: number;
  steamId?: number;
  gogId?: string;
  installPath?: string;
  isInstallable: boolean;
  executablePath?: string;
  isExternal: boolean;
  isOwned?: boolean;
  metadataSource?: string;
  installerPath?: string;
  installerStatus?: string;
  updateFiles?: GameUpdateFile[];
}

export interface GameImages {
  coverUrl?: string;
  coverLargeUrl?: string;
  backgroundUrl?: string;
  bannerUrl?: string;
  screenshots: string[];
  artworks: string[];
}

export interface GameFile {
  id: number;
  gameId: number;
  relativePath: string;
  size: number;
  dateAdded: string;
  quality?: string;
  releaseGroup?: string;
  edition?: string;
  languages: string[];
}

export interface GameUpdateFile {
  fileName: string;
  filePath: string;
  version?: string;
  size: number;
  dateAdded?: string;
  type: string;
}

export interface Platform {
  id: number;
  name: string;
  slug: string;
  folderName: string;
  type: string;
  icon?: string;
  enabled: boolean;
  category?: string;
  igdbPlatformId?: number;
  screenScraperSystemId?: number;
  parentPlatformId?: number;
}

export interface DashboardStats {
  totalGames: number;
  totalPlatforms: number;
  totalSize: number;
  recentlyAdded: { id: number; title: string; added: string; coverUrl: string | null }[];
  platformBreakdown: Record<string, number>;
  installedGames: number;
  externalGames: number;
  favoriteGames: number;
  genreStats: { genre: string; count: number }[];
  platformStats: { platformId: number; platform: string; count: number }[];
  yearStats: { year: number; count: number }[];
  ratingStats: { excellent: number; good: number; average: number; poor: number; unrated: number };
  statusStats: { status: string; count: number }[];
}

export interface DownloadClient {
  id?: number;
  name: string;
  implementation: string;
  host: string;
  port: number;
  username?: string;
  password?: string;
  category?: string;
  urlBase?: string;
  apiKey?: string;
  enable: boolean;
  useSsl: boolean;
  priority: number;
  remotePathMapping?: string;
  localPathMapping?: string;
}

export interface SearchResult {
  title: string;
  size: number;
  indexer: string;
  downloadUrl: string;
  magnetUrl?: string;
  infoUrl?: string;
  categories: number[];
  seeders?: number;
  leechers?: number;
  protocol: string;
}

export interface ScanStatus {
  isScanning: boolean;
  lastGameFound?: string;
  gamesAddedCount: number;
  currentScanDirectory?: string;
  currentScanFile?: string;
  filesScannedCount: number;
}

export interface EmulatorStatus {
  installed: boolean;
  version: string | null;
  path: string;
  assetsUrl: string;
}

export interface EmulatorUpdateInfo {
  currentVersion: string | null;
  latestVersion: string | null;
  updateAvailable: boolean;
}

export interface Collection {
  id: number;
  name: string;
  description?: string;
  icon?: string;
  color?: string;
  coverUrl?: string;
  gameCount: number;
  collectionGames: CollectionGame[];
  games: CollectionGameSummary[];
}

export interface CollectionGame {
  id: number;
  collectionId: number;
  gameId: number;
  game?: Game;
}

export interface CollectionGameSummary {
  id: number;
  title: string;
  coverUrl?: string;
  year?: number;
  platform?: string;
}

export interface GameReview {
  id: number;
  gameId: number;
  userRating?: number;
  completionStatus?: string;
  notes?: string;
  isFavorite: boolean;
}

export interface Tag {
  id: number;
  name: string;
}

export interface MediaSettings {
  folderPath: string;
  downloadPath: string;
  destinationPath: string;
  winePrefixPath: string;
  platform: string;
  folderNamingMode: string;
  gogDownloadsPath: string;
  destinationPathPattern: string;
  useDestinationPattern: boolean;
}

export interface ProwlarrSettings {
  url: string;
  apiKey: string;
}

export interface JackettSettings {
  url: string;
  apiKey: string;
}

export interface IgdbSettings {
  clientId: string;
  clientSecret: string;
}

export interface SteamSettings {
  apiKey: string;
  steamId: string;
}

export interface ScreenScraperSettings {
  username: string;
  password: string;
  enabled: boolean;
}

export interface PostDownloadSettings {
  enableAutoMove: boolean;
  enableAutoExtract: boolean;
  enableDeepClean: boolean;
  monitorIntervalSeconds: number;
  unwantedExtensions: string[];
}

export interface HydraSource {
  id?: number;
  name: string;
  url: string;
  enabled: boolean;
}

export interface DatabaseConfig {
  type: string;
  sqlitePath?: string;
  host?: string;
  port?: number;
  database?: string;
  username?: string;
  password?: string;
  useSsl?: boolean;
  connectionTimeout?: number;
  isConfigured?: boolean;
}

export interface DatabaseStats {
  databaseType: string;
  gamesCount: number;
  gameFilesCount: number;
  collectionsCount: number;
  tagsCount: number;
  reviewsCount: number;
  downloadHistoryCount: number;
}

export interface MigrationResult {
  success: boolean;
  message?: string;
  error?: string;
  backupPath?: string;
  rowCounts?: Record<string, { source: number; target: number }>;
  restartRequired?: boolean;
}

export interface CacheConfig {
  enabled: boolean;
  connectionString: string;
  libraryListTtlSeconds: number;
  gameDetailTtlSeconds: number;
  metadataTtlSeconds: number;
  downloadStatusTtlSeconds: number;
  dbStatsTtlSeconds: number;
  isConnected?: boolean;
}

// ===== API Functions =====

// -- Games --
export const gamesApi = {
  getAll: () => apiClient.get<Game[]>('/game', { params: { t: Date.now() } }),
  getById: (id: number, lang: string) => apiClient.get<Game>(`/game/${id}`, { params: { lang } }),
  create: (game: Partial<Game>) => apiClient.post<Game>('/game', game),
  update: (id: number, updates: Partial<Game>) => apiClient.put<Game>(`/game/${id}`, updates),
  delete: (id: number) => apiClient.delete(`/game/${id}`),
  deleteAll: () => apiClient.delete('/game/all'),
  lookup: (term: string, lang: string) => apiClient.get<Game[]>('/game/lookup', { params: { term, lang } }),
  getProblems: () => apiClient.get('/game/problems'),
  play: (id: number) => apiClient.post(`/game/${id}/play`),
  install: (id: number) => apiClient.post(`/game/${id}/install`),
  uninstall: (id: number) => apiClient.post(`/game/${id}/uninstall`),
  resolveProblem: (id: number) => apiClient.post(`/game/${id}/resolve-problem`),
};

// -- Platforms --
export const platformsApi = {
  getAll: () => apiClient.get<Platform[]>('/platform'),
  getEnabled: () => apiClient.get<Platform[]>('/platform', { params: { enabledOnly: true } }),
};

// -- Dashboard --
export const dashboardApi = {
  getStats: () => apiClient.get<DashboardStats>('/dashboard/stats'),
  getRandom: () => apiClient.get<Game>('/dashboard/random'),
  exportLibrary: () => apiClient.get('/dashboard/export', { responseType: 'blob' }),
  importLibrary: (data: unknown) => apiClient.post('/dashboard/import', data),
};

export interface MetadataRescanStatus {
  isRescanning: boolean;
  total: number;
  progress: number;
  updated: number;
  currentGame?: string;
}

// -- Media / Scanner --
export const mediaApi = {
  getSettings: () => apiClient.get<MediaSettings>('/media', { params: { t: Date.now() } }),
  saveSettings: (settings: Partial<MediaSettings>) => apiClient.post('/media', settings),
  startScan: (options?: { forceRefresh?: boolean; platform?: string }) => apiClient.post('/media/scan', options),
  stopScan: () => apiClient.post('/media/scan/stop'),
  getScanStatus: () => apiClient.get<ScanStatus>('/media/scan/status'),
  startMetadataRescan: (options?: { platformId?: number; missingOnly?: boolean; preferredSource?: string }) =>
    apiClient.post('/media/metadata/rescan', options),
  getMetadataRescanStatus: () => apiClient.get<MetadataRescanStatus>('/media/metadata/rescan/status'),
};

// -- Search --
export const searchApi = {
  search: (query: string, categories?: string) => apiClient.get<SearchResult[]>('/search', { params: { query, categories } }),
  getPlatforms: () => apiClient.get('/search/platforms'),
  testConnection: (type: string, settings: unknown) => apiClient.post('/search/test', { type, ...settings as Record<string, unknown> }),
};

// -- Settings --
export const settingsApi = {
  getIgdb: () => apiClient.get<IgdbSettings>('/settings/igdb'),
  saveIgdb: (settings: IgdbSettings) => apiClient.post('/settings/igdb', settings),
  deleteIgdb: () => apiClient.delete('/settings/igdb'),
  testIgdb: (settings: IgdbSettings) => apiClient.post('/metadata/igdb', settings),

  getProwlarr: () => apiClient.get<ProwlarrSettings>('/settings/prowlarr'),
  saveProwlarr: (settings: ProwlarrSettings) => apiClient.post('/settings/prowlarr', settings),

  getJackett: () => apiClient.get<JackettSettings>('/settings/jackett'),
  saveJackett: (settings: JackettSettings) => apiClient.post('/settings/jackett', settings),

  getSteam: () => apiClient.get<SteamSettings>('/settings/steam'),
  saveSteam: (settings: SteamSettings) => apiClient.post('/settings/steam', settings),
  deleteSteam: () => apiClient.delete('/settings/steam'),
  testSteam: (settings: SteamSettings) => apiClient.post('/settings/steam/test', settings),
  syncSteam: () => apiClient.post('/settings/steam/sync'),

  getScreenScraper: () => apiClient.get<ScreenScraperSettings>('/settings/screenscraper'),
  saveScreenScraper: (settings: Partial<ScreenScraperSettings>) => apiClient.post('/settings/screenscraper', settings),
  testScreenScraper: (settings: ScreenScraperSettings) => apiClient.post('/settings/screenscraper/test', settings),

  getPostDownload: () => apiClient.get<PostDownloadSettings>('/postdownload'),
  savePostDownload: (settings: PostDownloadSettings) => apiClient.post('/postdownload', settings),

  getDatabase: () => apiClient.get<DatabaseConfig>('/settings/database'),
  saveDatabase: (config: DatabaseConfig) => apiClient.put('/settings/database', config),
  testDatabase: (config: DatabaseConfig) => apiClient.post('/settings/database/test', config),
  migrateDatabase: (config: DatabaseConfig) => apiClient.post<MigrationResult>('/settings/database/migrate', config),
  backupDatabase: () => apiClient.post('/settings/database/backup'),
  getDatabaseStats: () => apiClient.get<DatabaseStats>('/settings/database/stats'),

  getLogging: () => apiClient.get('/settings/logging'),
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  saveLogging: (settings: any) => apiClient.post('/settings/logging', settings),
  exportLogs: (params: { timeRange: string }) => apiClient.post('/settings/logging/export', params, { responseType: 'arraybuffer' }),

  getCache: () => apiClient.get<CacheConfig>('/settings/cache'),
  saveCache: (config: CacheConfig) => apiClient.put('/settings/cache', config),
  testCache: (config: { connectionString: string }) => apiClient.post('/settings/cache/test', config),
  clearCache: () => apiClient.post('/settings/cache/clear'),
};

// -- Download Clients --
export const downloadClientsApi = {
  getAll: () => apiClient.get<DownloadClient[]>('/downloadclient'),
  add: (client: Partial<DownloadClient>) => apiClient.post('/downloadclient', client),
  update: (id: number, client: Partial<DownloadClient>) => apiClient.put(`/downloadclient/${id}`, client),
  delete: (id: number) => apiClient.delete(`/downloadclient/${id}`),
  test: (client: Partial<DownloadClient>) => apiClient.post('/downloadclient/test', client),
};

// -- Collections --
export const collectionsApi = {
  getAll: () => apiClient.get<Collection[]>('/collection'),
  create: (data: FormData | Partial<Collection>) => apiClient.post('/collection', data),
  update: (id: number, data: FormData | Partial<Collection>) => apiClient.put(`/collection/${id}`, data),
  delete: (id: number) => apiClient.delete(`/collection/${id}`),
};

// -- Reviews --
export const reviewsApi = {
  getByGameId: (gameId: number) => apiClient.get<GameReview>(`/review/game/${gameId}`),
  setRating: (gameId: number, rating: number) => apiClient.post(`/review/game/${gameId}`, { userRating: rating }),
  setStatus: (gameId: number, status: string) => apiClient.post(`/review/game/${gameId}`, { completionStatus: status }),
  setNotes: (gameId: number, notes: string) => apiClient.post(`/review/game/${gameId}`, { notes }),
  toggleFavorite: (gameId: number) => apiClient.post(`/review/game/${gameId}/favorite`),
};

// -- Tags --
export const tagsApi = {
  addToGame: (gameId: number, tagId: number, tagName: string) => apiClient.post(`/tag/game/${gameId}`, { tagId, tagName }),
  removeFromGame: (gameId: number, tagId: number) => apiClient.delete(`/tag/game/${gameId}/${tagId}`),
};

export interface CoreMapping {
  platformId: number;
  slug: string;
  name: string;
  core: string;
}

export interface BiosFileStatus {
  file: string;
  system: string;
  found: boolean;
}

export interface EmulatorHealth {
  healthy: boolean;
  loaderPresent: boolean;
  cachedCores: string[];
  missingCores: string[];
  totalSupportedCores: number;
  bios: BiosFileStatus[];
  biosDirectory: string;
}

// -- Emulator --
export const emulatorApi = {
  getStatus: () => apiClient.get<EmulatorStatus>('/emulator/status'),
  getHealth: () => apiClient.get<EmulatorHealth>('/emulator/health'),
  checkUpdate: () => apiClient.get<EmulatorUpdateInfo>('/emulator/check-update'),
  install: () => apiClient.post('/emulator/install'),
  uninstall: () => apiClient.delete('/emulator/uninstall'),
  isPlayable: (gameId: number) => apiClient.get(`/emulator/${gameId}/playable`),
  getCoreMapping: () => apiClient.get<CoreMapping[]>('/emulator/cores/mapping'),
};

// -- Hydra Sources --
export const hydraApi = {
  getAll: () => apiClient.get<HydraSource[]>('/hydra'),
  create: (source: Partial<HydraSource>) => apiClient.post('/hydra', source),
  update: (id: number, source: Partial<HydraSource>) => apiClient.put(`/hydra/${id}`, source),
  delete: (id: number) => apiClient.delete(`/hydra/${id}`),
};

// -- Steam Profile/Social --
export const steamApi = {
  getProfile: () => apiClient.get('/steam/profile'),
  getStats: () => apiClient.get('/steam/stats'),
  getRecent: () => apiClient.get('/steam/recent'),
  getFriends: () => apiClient.get('/steam/friends'),
};

// -- GOG --
export const gogApi = {
  getStatus: () => apiClient.get('/gog/status'),
  getSettings: () => apiClient.get('/gog/settings'),
  saveSettings: (settings: unknown) => apiClient.post('/gog/settings', settings),
  getAuthUrl: () => apiClient.get<{ url: string }>('/gog/auth/url'),
  submitAuthCode: (code: string) => apiClient.post('/gog/auth/code', { code }),
  sync: () => apiClient.post('/gog/sync'),
  getDownloads: (gogId: string) => apiClient.get(`/settings/gog/downloads/${gogId}`),
};

// -- Debug --
export const debugApi = {
  getLogs: (params?: { level?: string; limit?: number }) => apiClient.get('/debug/logs', { params }),
  clearLogs: () => apiClient.delete('/debug/logs'),
  getScanProgress: () => apiClient.get('/debug/scan-progress'),
};

// -- Switch USB --
export const switchApi = {
  getDevices: () => apiClient.get('/nsw/devices'),
  install: (filePath: string, device: string) => apiClient.post('/nsw/install', { filePath, device }),
  getProgress: () => apiClient.get('/nsw/progress'),
};

// -- Filesystem --
export const filesystemApi = {
  browse: (path: string) => apiClient.get(`/filesystem`, { params: { path } }),
};

// -- Metadata Review --
export interface MetadataReviewItem {
  gameId: number;
  title: string;
  alternativeTitle?: string;
  platformId: number;
  platformName?: string;
  platformSlug?: string;
  matchConfidence?: number;
  reviewReason: string;
  currentIgdbId?: number;
  coverUrl?: string;
  added: string;
}

export interface MatchCandidate {
  igdbId: number;
  title: string;
  alternativeNames: string[];
  platforms: string[];
  year?: number;
  coverUrl?: string;
  score: number;
  source: string;
  overview?: string;
  developer?: string;
  publisher?: string;
  genres?: string[];
  rating?: number;
  coverLargeUrl?: string;
  backgroundUrl?: string;
  bannerUrl?: string;
}

export const metadataReviewApi = {
  getQueue: (platformFilter?: string) =>
    apiClient.get<MetadataReviewItem[]>('/metadata/review', { params: { platformFilter }, timeout: 120000 }),
  getCandidates: (gameId: number, searchOverride?: string) =>
    apiClient.get<MatchCandidate[]>(`/metadata/review/${gameId}/candidates`, { params: { searchOverride } }),
  confirm: (gameId: number, igdbId: number, score?: number, source?: string, screenScraperData?: Record<string, unknown>) =>
    apiClient.post(`/metadata/review/${gameId}/confirm`, { igdbId, score, source: source || 'IGDB', screenScraperData }),
  skip: (gameId: number) =>
    apiClient.post(`/metadata/review/${gameId}/skip`),
  dismiss: (gameId: number) =>
    apiClient.post(`/metadata/review/${gameId}/dismiss`),
};
