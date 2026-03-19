import { useQuery, useMutation, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import {
  gamesApi,
  platformsApi,
  dashboardApi,
  mediaApi,
  settingsApi,
  downloadClientsApi,
  collectionsApi,
  reviewsApi,
  emulatorApi,
  hydraApi,
  gogApi,
  Game,
  DownloadClient,
  IgdbSettings,
} from './client';

// ===== Query Keys =====
export const queryKeys = {
  games: {
    all: ['games'] as const,
    byId: (id: number, lang: string) => ['games', id, lang] as const,
    problems: ['games', 'problems'] as const,
    lookup: (term: string, lang: string) => ['games', 'lookup', term, lang] as const,
  },
  platforms: {
    all: ['platforms'] as const,
    enabled: ['platforms', 'enabled'] as const,
  },
  dashboard: {
    stats: ['dashboard', 'stats'] as const,
    random: ['dashboard', 'random'] as const,
  },
  media: {
    settings: ['media', 'settings'] as const,
    scanStatus: ['media', 'scanStatus'] as const,
  },
  settings: {
    igdb: ['settings', 'igdb'] as const,
    prowlarr: ['settings', 'prowlarr'] as const,
    jackett: ['settings', 'jackett'] as const,
    steam: ['settings', 'steam'] as const,
    screenScraper: ['settings', 'screenScraper'] as const,
    postDownload: ['settings', 'postDownload'] as const,
    database: ['settings', 'database'] as const,
    databaseStats: ['settings', 'databaseStats'] as const,
  },
  downloadClients: {
    all: ['downloadClients'] as const,
  },
  collections: {
    all: ['collections'] as const,
  },
  reviews: {
    byGameId: (gameId: number) => ['reviews', gameId] as const,
  },
  emulator: {
    status: ['emulator', 'status'] as const,
    update: ['emulator', 'update'] as const,
  },
  hydra: {
    all: ['hydra'] as const,
  },
  gog: {
    status: ['gog', 'status'] as const,
    settings: ['gog', 'settings'] as const,
  },
  debug: {
    logs: (params?: { level?: string; limit?: number }) => ['debug', 'logs', params] as const,
    scanProgress: ['debug', 'scanProgress'] as const,
  },
};

// ===== Game Hooks =====

export const useGames = (options?: Partial<UseQueryOptions<Game[]>>) =>
  useQuery({
    queryKey: queryKeys.games.all,
    queryFn: () => gamesApi.getAll().then(r => r.data),
    staleTime: 30_000,
    ...options,
  });

export const useGame = (id: number, lang: string, options?: Partial<UseQueryOptions<Game>>) =>
  useQuery({
    queryKey: queryKeys.games.byId(id, lang),
    queryFn: () => gamesApi.getById(id, lang).then(r => r.data),
    enabled: id > 0,
    ...options,
  });

export const useGameProblems = () =>
  useQuery({
    queryKey: queryKeys.games.problems,
    queryFn: () => gamesApi.getProblems().then(r => r.data),
  });

export const useDeleteGame = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => gamesApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.games.all });
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.stats });
    },
  });
};

export const useUpdateGame = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, updates }: { id: number; updates: Partial<Game> }) => gamesApi.update(id, updates),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.games.all });
      queryClient.invalidateQueries({ queryKey: ['games', variables.id] });
    },
  });
};

// ===== Platform Hooks =====

export const usePlatforms = () =>
  useQuery({
    queryKey: queryKeys.platforms.all,
    queryFn: () => platformsApi.getAll().then(r => r.data),
    staleTime: 5 * 60_000,
  });

export const useEnabledPlatforms = () =>
  useQuery({
    queryKey: queryKeys.platforms.enabled,
    queryFn: () => platformsApi.getEnabled().then(r => r.data),
    staleTime: 5 * 60_000,
  });

// ===== Dashboard Hooks =====

export const useDashboardStats = () =>
  useQuery({
    queryKey: queryKeys.dashboard.stats,
    queryFn: () => dashboardApi.getStats().then(r => r.data),
    staleTime: 60_000,
  });

// ===== Media / Scanner Hooks =====

export const useMediaSettings = () =>
  useQuery({
    queryKey: queryKeys.media.settings,
    queryFn: () => mediaApi.getSettings().then(r => r.data),
  });

export const useScanStatus = (enabled = true) =>
  useQuery({
    queryKey: queryKeys.media.scanStatus,
    queryFn: () => mediaApi.getScanStatus().then(r => r.data),
    refetchInterval: enabled ? 3000 : false,
    enabled,
  });

export const useStartScan = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (options?: { forceRefresh?: boolean }) => mediaApi.startScan(options),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.media.scanStatus });
    },
  });
};

// ===== Settings Hooks =====

export const useIgdbSettings = () =>
  useQuery({
    queryKey: queryKeys.settings.igdb,
    queryFn: () => settingsApi.getIgdb().then(r => r.data),
    staleTime: 5 * 60_000,
  });

export const useSaveIgdbSettings = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (settings: IgdbSettings) => settingsApi.saveIgdb(settings),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.settings.igdb });
    },
  });
};

export const useProwlarrSettings = () =>
  useQuery({
    queryKey: queryKeys.settings.prowlarr,
    queryFn: () => settingsApi.getProwlarr().then(r => r.data),
    staleTime: 5 * 60_000,
  });

export const useJackettSettings = () =>
  useQuery({
    queryKey: queryKeys.settings.jackett,
    queryFn: () => settingsApi.getJackett().then(r => r.data),
    staleTime: 5 * 60_000,
  });

export const useSteamSettings = () =>
  useQuery({
    queryKey: queryKeys.settings.steam,
    queryFn: () => settingsApi.getSteam().then(r => r.data),
    staleTime: 5 * 60_000,
  });

export const useScreenScraperSettings = () =>
  useQuery({
    queryKey: queryKeys.settings.screenScraper,
    queryFn: () => settingsApi.getScreenScraper().then(r => r.data),
    staleTime: 5 * 60_000,
  });

export const usePostDownloadSettings = () =>
  useQuery({
    queryKey: queryKeys.settings.postDownload,
    queryFn: () => settingsApi.getPostDownload().then(r => r.data),
    staleTime: 5 * 60_000,
  });

export const useDatabaseConfig = () =>
  useQuery({
    queryKey: queryKeys.settings.database,
    queryFn: () => settingsApi.getDatabase().then(r => r.data),
    staleTime: 5 * 60_000,
  });

export const useDatabaseStats = () =>
  useQuery({
    queryKey: queryKeys.settings.databaseStats,
    queryFn: () => settingsApi.getDatabaseStats().then(r => r.data),
  });

// ===== Download Client Hooks =====

export const useDownloadClients = () =>
  useQuery({
    queryKey: queryKeys.downloadClients.all,
    queryFn: () => downloadClientsApi.getAll().then(r => r.data),
  });

export const useAddDownloadClient = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (client: Partial<DownloadClient>) => downloadClientsApi.add(client),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.downloadClients.all });
    },
  });
};

export const useDeleteDownloadClient = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => downloadClientsApi.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.downloadClients.all });
    },
  });
};

// ===== Collection Hooks =====

export const useCollections = () =>
  useQuery({
    queryKey: queryKeys.collections.all,
    queryFn: () => collectionsApi.getAll().then(r => r.data),
  });

// ===== Review Hooks =====

export const useGameReview = (gameId: number) =>
  useQuery({
    queryKey: queryKeys.reviews.byGameId(gameId),
    queryFn: () => reviewsApi.getByGameId(gameId).then(r => r.data),
    enabled: gameId > 0,
  });

export const useSetGameRating = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ gameId, rating }: { gameId: number; rating: number }) => reviewsApi.setRating(gameId, rating),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.reviews.byGameId(variables.gameId) });
    },
  });
};

// ===== Emulator Hooks =====

export const useEmulatorStatus = () =>
  useQuery({
    queryKey: queryKeys.emulator.status,
    queryFn: () => emulatorApi.getStatus().then(r => r.data),
  });

export const useEmulatorHealth = () =>
  useQuery({
    queryKey: ['emulator', 'health'] as const,
    queryFn: () => emulatorApi.getHealth().then(r => r.data),
  });

export const useCoreMapping = () =>
  useQuery({
    queryKey: ['emulator', 'coreMapping'] as const,
    queryFn: () => emulatorApi.getCoreMapping().then(r => r.data),
    staleTime: 10 * 60_000,
  });

export const useEmulatorInstall = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => emulatorApi.install(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.emulator.status });
    },
  });
};

// ===== Hydra Hooks =====

export const useHydraSources = () =>
  useQuery({
    queryKey: queryKeys.hydra.all,
    queryFn: () => hydraApi.getAll().then(r => r.data),
  });

// ===== GOG Hooks =====

export const useGogStatus = () =>
  useQuery({
    queryKey: queryKeys.gog.status,
    queryFn: () => gogApi.getStatus().then(r => r.data),
  });
