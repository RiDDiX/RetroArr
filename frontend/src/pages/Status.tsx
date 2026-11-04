import { useState, useEffect, useCallback, useRef } from 'react';
import { useLocation } from 'react-router-dom';
import { useTranslation } from '../i18n/translations';
import apiClient, { getErrorMessage } from '../api/client';
import { formatDate } from '../utils/date';
import './Status.css';

const API_BASE = '/downloadclient';

interface DownloadStatus {
  id: string;
  clientId: number;
  clientName: string;
  name: string;
  size: number;
  progress: number;
  state: string;
  category: string;
  downloadPath: string;
  platformFolder: string | null;
  gameId: number | null;
  gameTitle: string | null;
  trackedState: string | null;
  statusMessages: string[];
}

interface HistoryEntry {
  id: number;
  downloadId: string;
  clientId: number;
  clientName: string;
  title: string;
  cleanTitle: string | null;
  platform: string | null;
  size: number;
  state: string;
  reason: string | null;
  sourcePath: string | null;
  destinationPath: string | null;
  importedAt: string;
  addedAt: string;
  gameId: number | null;
}

interface BlacklistEntry {
  id: number;
  downloadId: string | null;
  title: string;
  platform: string | null;
  reason: string;
  blacklistedAt: string;
  clientName: string | null;
}

interface UnmappedEntry {
  downloadId: string;
  downloadClientId: number;
  downloadClientName: string;
  title: string;
  size: number;
  outputPath: string | null;
  added: string;
  state: string;
  statusMessages: string[];
  gameId: number | null;
  gameTitle: string | null;
  gamePlatform: string | null;
}

interface Counts {
  active: number;
  failed: number;
  unmapped: number;
  blacklisted: number;
}

interface PlatformOption {
  id: number;
  name: string;
  slug: string;
  folderName: string;
  enabled: boolean;
}

interface UnmappedLocalFile {
  fileName: string;
  fullPath: string;
  folder: string;
  platformFolder: string;
  size: number;
  formattedSize: string;
  extension: string;
  lastModified: string;
}

interface GogDownloadStatus {
  id: string;
  gameTitle: string;
  fileName: string;
  filePath: string;
  totalBytes: number;
  bytesDownloaded: number;
  progressPercent: number;
  state: string;
  errorMessage: string | null;
  startedAt: string;
  completedAt: string | null;
}

type TabKey = 'activity' | 'history' | 'failed' | 'unmapped' | 'unmapped-files' | 'blacklist';

const validTabs: TabKey[] = ['activity', 'history', 'failed', 'unmapped', 'unmapped-files', 'blacklist'];

const tabFromHash = (hash: string): TabKey | null => {
  const tab = hash.replace('#', '') as TabKey;
  return validTabs.includes(tab) ? tab : null;
};

// Thousands of rows at once freeze the page for many seconds
const PAGE_SIZE = 200;

interface GameOption {
  id: number;
  title: string;
}

const NO_GAMES = { key: '', items: [] as GameOption[], total: 0 };

// The pickers get a page of matches from the server: the whole library as options freezes the page
function useGameSearch(enabled: boolean, search: string, platformId?: number) {
  const [result, setResult] = useState(NO_GAMES);
  const key = `${platformId ?? ''}|${search.trim()}`;
  useEffect(() => {
    if (!enabled) return;
    let current = true;
    const params = new URLSearchParams({ pageSize: '50' });
    if (search.trim()) params.set('search', search.trim());
    if (platformId) params.set('platformId', String(platformId));
    // One request once typing pauses, not one per key
    const timer = setTimeout(() => {
      apiClient.get(`/game/paged?${params}`)
        .then(r => r.data)
        .then(data => {
          if (!current || !Array.isArray(data?.items)) return;
          const items = data.items.map((g: GameOption) => ({ id: g.id, title: g.title }));
          setResult({ key, items, total: data.totalItems ?? items.length });
        })
        .catch(() => {});
    }, 250);
    return () => { current = false; clearTimeout(timer); };
  }, [enabled, key, search, platformId]);
  // Only the matches of this search: one picked from an older list may be missing from the new one, and the select
  // would show another option while the id stays picked
  return enabled && result.key === key ? result : NO_GAMES;
}

function ShowMore({ total, shown, onMore }: { total: number; shown: number; onMore: () => void }) {
  if (total <= shown) return null;
  return (
    <div style={{ padding: '12px', textAlign: 'center' }}>
      <button className="btn-secondary" onClick={onMore}>Show more ({total - shown} left)</button>
    </div>
  );
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

function getStateBadgeClass(state: string | number): string {
  const lower = String(state ?? '').toLowerCase();
  if (lower.includes('import') && lower.includes('fail')) return 'error';
  if (lower.includes('imported')) return 'completed';
  if (lower.includes('importing')) return 'copying';
  if (lower.includes('download')) return 'downloading';
  if (lower.includes('pending')) return 'checking';
  if (lower.includes('blocked')) return 'paused';
  if (lower.includes('ignored')) return 'paused';
  if (lower.includes('complet')) return 'completed';
  if (lower.includes('paus')) return 'paused';
  if (lower.includes('error') || lower.includes('fail')) return 'error';
  return 'downloading';
}

const Status: React.FC = () => {
  const { t } = useTranslation();
  const location = useLocation();
  // From the hash right away, so a link to another tab doesn't poll the queue first
  const [activeTab, setActiveTab] = useState<TabKey>(() => tabFromHash(location.hash) ?? 'activity');
  const [counts, setCounts] = useState<Counts>({ active: 0, failed: 0, unmapped: 0, blacklisted: 0 });
  // Rows shown in the open tab's list: a page, more on request
  const [shownIn, setShownIn] = useState({ tab: activeTab, n: PAGE_SIZE });
  const shown = shownIn.tab === activeTab ? shownIn.n : PAGE_SIZE;
  const showMore = () => setShownIn({ tab: activeTab, n: shown + PAGE_SIZE });

  // Sync tab from URL hash
  useEffect(() => {
    const tab = tabFromHash(location.hash);
    if (tab) {
      setActiveTab(tab);
    }
  }, [location.hash]);

  // Activity state
  const [downloads, setDownloads] = useState<DownloadStatus[]>([]);
  const [editingPlatform, setEditingPlatform] = useState<string | null>(null);
  const [removeTarget, setRemoveTarget] = useState<DownloadStatus | null>(null);
  const [removeFiles, setRemoveFiles] = useState(true);
  const [removing, setRemoving] = useState(false);
  const [importError, setImportError] = useState<string | null>(null);

  // GOG download state
  const [gogDownloads, setGogDownloads] = useState<GogDownloadStatus[]>([]);

  // History state
  const [history, setHistory] = useState<HistoryEntry[]>([]);
  const [historyQuery, setHistoryQuery] = useState('');
  const [historyPlatform, setHistoryPlatform] = useState('');
  const [historyState, setHistoryState] = useState('');
  const [historySort, setHistorySort] = useState('importedAt');
  const [historySortDesc, setHistorySortDesc] = useState(true);
  const [historyPage, setHistoryPage] = useState(1);
  const [historyTotalPages, setHistoryTotalPages] = useState(1);

  // Failed state
  const [failed, setFailed] = useState<HistoryEntry[]>([]);

  // Unmapped state
  const [unmapped, setUnmapped] = useState<UnmappedEntry[]>([]);
  const [mapModal, setMapModal] = useState<UnmappedEntry | null>(null);
  const [mapPlatform, setMapPlatform] = useState('');
  const [mapGameId, setMapGameId] = useState<number | ''>('');
  const [mapFileType, setMapFileType] = useState<'' | 'Main' | 'Patches' | 'DLC'>('');
  const [mapFileTouched, setMapFileTouched] = useState(false);
  const [mapGameSearch, setMapGameSearch] = useState('');

  // Blacklist state
  const [blacklist, setBlacklist] = useState<BlacklistEntry[]>([]);
  const [blacklistQuery, setBlacklistQuery] = useState('');

  // Unmapped local files state
  const [unmappedFiles, setUnmappedFiles] = useState<UnmappedLocalFile[]>([]);
  const [unmappedFilesLoading, setUnmappedFilesLoading] = useState(false);
  const [mapFileModal, setMapFileModal] = useState<UnmappedLocalFile | null>(null);
  const [mapFileGameId, setMapFileGameId] = useState<number | ''>('');
  const [mapFileSearch, setMapFileSearch] = useState('');
  const [fileError, setFileError] = useState<string | null>(null);
  const [createGameModal, setCreateGameModal] = useState<UnmappedLocalFile | null>(null);
  const [createGameTitle, setCreateGameTitle] = useState('');

  // Platforms
  const [platforms, setPlatforms] = useState<PlatformOption[]>([]);

  const mapGames = useGameSearch(!!mapModal && !!mapPlatform, mapGameSearch, platforms.find(p => p.folderName === mapPlatform)?.id);
  // The game the download is linked to, also when the first page of matches doesn't hold it
  const mapGameOptions = mapModal?.gameId && mapModal.gameTitle && mapGameId === mapModal.gameId && !mapGames.items.some(g => g.id === mapGameId)
    ? [{ id: mapModal.gameId, title: mapModal.gameTitle }, ...mapGames.items]
    : mapGames.items;
  const mapFileGames = useGameSearch(!!mapFileModal, mapFileSearch);

  // Fetch platforms once
  useEffect(() => {
    apiClient.get('/platform')
      .then(r => r.data)
      .then(data => {
        if (Array.isArray(data)) setPlatforms(data.filter((p: PlatformOption) => p.enabled));
      })
      .catch(() => {});
  }, []);

  // Fetch counts on interval
  const fetchCounts = useCallback(() => {
    apiClient.get(`${API_BASE}/counts`)
      .then(r => r.data)
      .then(data => setCounts(data))
      .catch(() => {});
  }, []);

  useEffect(() => {
    fetchCounts();
    // Pause the interval while the tab is hidden so we don't burn RPS on an
    // unseen page. `visibilitychange` snaps the clock back when the user
    // comes back so the UI is fresh the moment they look.
    let iv: ReturnType<typeof setInterval> | null = setInterval(fetchCounts, 10000);
    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        fetchCounts();
        if (!iv) iv = setInterval(fetchCounts, 10000);
      } else if (iv) {
        clearInterval(iv);
        iv = null;
      }
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      document.removeEventListener('visibilitychange', onVisibility);
      if (iv) clearInterval(iv);
    };
  }, [fetchCounts]);

  // Activity polling
  useEffect(() => {
    if (activeTab !== 'activity') return;
    // A poll still running is not started again: slow clients can take longer than the interval
    let busy = false;
    const fetchQueue = () => {
      if (busy) return;
      busy = true;
      apiClient.get(`${API_BASE}/queue`)
        .then(r => r.data)
        .then(data => { if (Array.isArray(data)) setDownloads(data); })
        .catch(() => {})
        .finally(() => { busy = false; });
    };
    fetchQueue();
    let iv: ReturnType<typeof setInterval> | null = setInterval(fetchQueue, 3000);
    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        fetchQueue();
        if (!iv) iv = setInterval(fetchQueue, 3000);
      } else if (iv) {
        clearInterval(iv);
        iv = null;
      }
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      document.removeEventListener('visibilitychange', onVisibility);
      if (iv) clearInterval(iv);
    };
  }, [activeTab]);

  // GOG downloads polling
  useEffect(() => {
    if (activeTab !== 'activity') return;
    const fetchGog = () => {
      apiClient.get('/settings/gog/download-status')
        .then(r => r.data)
        .then(data => { if (Array.isArray(data)) setGogDownloads(data); })
        .catch(() => {});
    };
    fetchGog();
    let iv: ReturnType<typeof setInterval> | null = setInterval(fetchGog, 2000);
    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        fetchGog();
        if (!iv) iv = setInterval(fetchGog, 2000);
      } else if (iv) {
        clearInterval(iv);
        iv = null;
      }
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      document.removeEventListener('visibilitychange', onVisibility);
      if (iv) clearInterval(iv);
    };
  }, [activeTab]);

  // History fetch
  const fetchHistory = useCallback(() => {
    const params = new URLSearchParams();
    if (historyQuery) params.set('query', historyQuery);
    if (historyPlatform) params.set('platform', historyPlatform);
    if (historyState) params.set('state', historyState);
    params.set('sortBy', historySort);
    params.set('sortDescending', String(historySortDesc));
    params.set('page', String(historyPage));
    params.set('pageSize', '25');

    apiClient.get(`${API_BASE}/history?${params}`)
      .then(r => r.data)
      .then(data => {
        setHistory(data.items || []);
        setHistoryTotalPages(data.totalPages || 1);
      })
      .catch(() => {});
  }, [historyQuery, historyPlatform, historyState, historySort, historySortDesc, historyPage]);

  useEffect(() => {
    if (activeTab === 'history') fetchHistory();
  }, [activeTab, fetchHistory]);

  // Failed fetch
  const fetchFailed = useCallback(() => {
    apiClient.get(`${API_BASE}/history/failed`)
      .then(r => r.data)
      .then(data => { if (Array.isArray(data)) setFailed(data); })
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (activeTab === 'failed') fetchFailed();
  }, [activeTab, fetchFailed]);

  // Unmapped fetch
  const fetchUnmapped = useCallback(() => {
    apiClient.get(`${API_BASE}/unmapped`)
      .then(r => r.data)
      .then(data => { if (Array.isArray(data)) setUnmapped(data); })
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (activeTab === 'unmapped') fetchUnmapped();
  }, [activeTab, fetchUnmapped]);

  // Blacklist fetch. Only the latest request's answer is shown: an older, slower one would overwrite it.
  const blacklistRequest = useRef(0);
  const fetchBlacklist = useCallback(() => {
    const request = ++blacklistRequest.current;
    const params = blacklistQuery ? `?query=${encodeURIComponent(blacklistQuery)}` : '';
    apiClient.get(`${API_BASE}/blacklist${params}`)
      .then(r => r.data)
      .then(data => { if (request === blacklistRequest.current && Array.isArray(data)) setBlacklist(data); })
      .catch(() => {});
  }, [blacklistQuery]);

  useEffect(() => {
    if (activeTab !== 'blacklist') return;
    // One request once typing pauses, not one per key
    const timer = setTimeout(fetchBlacklist, 250);
    return () => clearTimeout(timer);
  }, [activeTab, fetchBlacklist]);

  // Unmapped local files fetch
  const fetchUnmappedFiles = useCallback(() => {
    setUnmappedFilesLoading(true);
    apiClient.get('/game/unmapped-files')
      .then(r => r.data)
      .then(data => { if (Array.isArray(data)) setUnmappedFiles(data); })
      .catch(() => {})
      .finally(() => setUnmappedFilesLoading(false));
  }, []);

  useEffect(() => {
    if (activeTab === 'unmapped-files') {
      fetchUnmappedFiles();
    }
  }, [activeTab, fetchUnmappedFiles]);

  // Actions
  const handlePause = async (clientId: number, downloadId: string) => {
    setImportError(null);
    try {
      await apiClient.post(`${API_BASE}/queue/${clientId}/${encodeURIComponent(downloadId)}/pause`);
    } catch (error) {
      setImportError(getErrorMessage(error));
    }
  };

  const handleResume = async (clientId: number, downloadId: string) => {
    setImportError(null);
    try {
      await apiClient.post(`${API_BASE}/queue/${clientId}/${encodeURIComponent(downloadId)}/resume`);
    } catch (error) {
      setImportError(getErrorMessage(error));
    }
  };

  const handleDelete = async () => {
    const target = removeTarget;
    if (!target || removing) return;
    setRemoving(true);
    try {
      await apiClient.delete(`${API_BASE}/queue/${target.clientId}/${encodeURIComponent(target.id)}?deleteFiles=${removeFiles}`);
    } catch {
      // the next queue poll shows whether it went through
    } finally {
      setRemoving(false);
      setRemoveTarget(current => (current === target ? null : current));
    }
  };

  // A manual import copies the whole download, so it gets no timeout; the server says why it refused
  const importDownload = (clientId: number, downloadId: string) =>
    apiClient.post(`/downloadclient/queue/${clientId}/${encodeURIComponent(downloadId)}/import`, undefined, { timeout: 0 });

  const handleImport = async (clientId: number, downloadId: string) => {
    setImportError(null);
    try {
      await importDownload(clientId, downloadId);
    } catch (error) {
      setImportError(getErrorMessage(error));
    }
  };

  const handleGogCancel = async (trackId: string) => {
    setImportError(null);
    try {
      await apiClient.delete(`/settings/gog/download-status/${trackId}`);
    } catch (error) {
      setImportError(getErrorMessage(error));
    }
  };

  const handleMapPlatform = async (d: DownloadStatus, platformFolder: string) => {
    setImportError(null);
    try {
      await apiClient.post(`${API_BASE}/queue/map-platform`, { downloadName: d.name, clientId: d.clientId, downloadId: d.id, platformFolder });
      setEditingPlatform(null);
    } catch (error) {
      setImportError(getErrorMessage(error));
    }
  };

  // History, Failed and Blacklist have no error banner; the refreshed lists show what went through
  const handleHistoryDismiss = async (id: number) => {
    try {
      await apiClient.post(`${API_BASE}/history/${id}/dismiss`);
    } catch (error) {
      console.error('Dismiss failed:', getErrorMessage(error));
    } finally {
      fetchFailed();
      fetchHistory();
      fetchCounts();
    }
  };

  const handleHistoryBlacklist = async (id: number) => {
    try {
      await apiClient.post(`${API_BASE}/history/${id}/blacklist`, { reason: 'Blacklisted by user' });
    } catch (error) {
      console.error('Blacklist failed:', getErrorMessage(error));
    } finally {
      fetchFailed();
      fetchHistory();
      fetchBlacklist();
      fetchCounts();
    }
  };

  const handleBlacklistRemove = async (id: number) => {
    try {
      await apiClient.delete(`${API_BASE}/blacklist/${id}`);
    } catch (error) {
      console.error('Blacklist remove failed:', getErrorMessage(error));
    } finally {
      fetchBlacklist();
      fetchCounts();
    }
  };

  // The import's own rules pick the file type for the game and platform chosen in the dialog, until the user picks one
  useEffect(() => {
    if (!mapModal || mapFileTouched) return;
    let current = true;
    const params = new URLSearchParams({ title: mapModal.title });
    if (mapGameId) params.set('gameId', String(mapGameId));
    if (mapPlatform) params.set('platform', mapPlatform);
    apiClient.get(`${API_BASE}/queue/import-subfolder?${params}`)
      .then(r => r.data)
      .then(data => { if (current && data?.importSubfolder) setMapFileType(data.importSubfolder); })
      .catch(() => {});
    return () => { current = false; };
  }, [mapModal, mapGameId, mapPlatform, mapFileTouched]);

  const handleMapAndImport = async () => {
    if (!mapModal || !mapPlatform) return;
    setFileError(null);
    try {
      // 1. Set platform + game + subfolder mapping
      await apiClient.post('/downloadclient/queue/map-platform', {
        downloadName: mapModal.title,
        clientId: mapModal.downloadClientId,
        downloadId: mapModal.downloadId,
        platformFolder: mapPlatform,
        // 0 and a picked "Main" take back a game and a Patches/DLC subfolder the download was linked to. The suggestion
        // only shows what the import would do: unpicked, the server keeps the grab's subfolder or detects it.
        gameId: mapGameId || 0,
        importSubfolder: mapFileTouched ? mapFileType || null : null
      });
      // 2. Trigger import
      await importDownload(mapModal.downloadClientId, mapModal.downloadId);
    } catch (error) {
      showFileError(error);
      // The mapping may be saved although the import was refused
      fetchUnmapped();
      fetchCounts();
      return;
    }
    setMapModal(null);
    setMapPlatform('');
    setMapGameId('');
    setMapFileType('');
    setMapGameSearch('');
    fetchUnmapped();
    fetchCounts();
  };

  const handleHistorySort = (col: string) => {
    if (historySort === col) {
      setHistorySortDesc(!historySortDesc);
    } else {
      setHistorySort(col);
      setHistorySortDesc(true);
    }
    setHistoryPage(1);
  };

  // The server says why it refused (400, 409, 500); the dialog stays open with its message
  const showFileError = (error: unknown) => {
    const message = getErrorMessage(error);
    setFileError(typeof message === 'string' ? message : t('error'));
  };

  const handleMapFileToGame = async () => {
    if (!mapFileModal || !mapFileGameId) return;
    try {
      await apiClient.post(`/game/${mapFileGameId}/map-file`, { filePath: mapFileModal.fullPath });
      setMapFileModal(null);
      setMapFileGameId('');
      fetchUnmappedFiles();
    } catch (error) {
      showFileError(error);
    }
  };

  const handleCreateGameFromFile = async () => {
    if (!createGameModal) return;
    try {
      await apiClient.post('/game/create-from-file', {
        filePath: createGameModal.fullPath,
        title: createGameTitle || undefined,
        platformFolder: createGameModal.platformFolder || undefined
      });
      setCreateGameModal(null);
      setCreateGameTitle('');
      fetchUnmappedFiles();
    } catch (error) {
      showFileError(error);
    }
  };

  const tabBadge = (count: number) => count > 0 ? <span className="tab-badge">{count}</span> : null;

  return (
    <div className="status-page">
      <div className="status-header">
        <h1>{t('downloads')}</h1>
        <p>{t('statusPageDesc')}</p>
      </div>

      <div className="status-tabs">
        <button className={`status-tab ${activeTab === 'activity' ? 'active' : ''}`} onClick={() => setActiveTab('activity')}>
          {t('downloadsQueue')} {tabBadge(counts.active)}
        </button>
        <button className={`status-tab ${activeTab === 'history' ? 'active' : ''}`} onClick={() => setActiveTab('history')}>
          {t('downloadsHistory')}
        </button>
        <button className={`status-tab ${activeTab === 'failed' ? 'active' : ''}`} onClick={() => setActiveTab('failed')}>
          {t('downloadsFailed')} {tabBadge(counts.failed)}
        </button>
        <button className={`status-tab ${activeTab === 'unmapped' ? 'active' : ''}`} onClick={() => setActiveTab('unmapped')}>
          {t('downloadsUnmapped')} {tabBadge(counts.unmapped)}
        </button>
        <button className={`status-tab ${activeTab === 'unmapped-files' ? 'active' : ''}`} onClick={() => setActiveTab('unmapped-files')}>
          {t('notMappedFiles')} {unmappedFiles.length > 0 ? tabBadge(unmappedFiles.length) : null}
        </button>
        <button className={`status-tab ${activeTab === 'blacklist' ? 'active' : ''}`} onClick={() => setActiveTab('blacklist')}>
          {t('downloadsBlacklist')} {tabBadge(counts.blacklisted)}
        </button>
      </div>

      {/* ===== ACTIVITY TAB ===== */}
      {activeTab === 'activity' && (
        <div className="downloads-table-container">
          {importError && <div className="modal-value small" style={{ color: 'var(--ctp-red)', padding: '8px 16px' }}>{t('error')}: {importError}</div>}
          {/* GOG Downloads Section */}
          {gogDownloads.length > 0 && (
            <>
              <div style={{ padding: '12px 16px', background: 'var(--ctp-surface0)', borderRadius: '8px 8px 0 0', borderBottom: '1px solid var(--ctp-surface1)', display: 'flex', alignItems: 'center', gap: '8px', marginBottom: 0 }}>
                <span style={{ fontSize: '1.1em' }}>🟣</span>
                <strong style={{ color: 'var(--ctp-text)' }}>GOG Downloads</strong>
                <span style={{ color: 'var(--ctp-subtext0)', fontSize: '0.85em' }}>({gogDownloads.length})</span>
              </div>
              <table className="downloads-table" style={{ marginBottom: '20px' }}>
                <thead>
                  <tr>
                    <th>Title</th>
                    <th>Status</th>
                    <th>Size</th>
                    <th>Path</th>
                    <th>Started</th>
                    <th style={{ textAlign: 'right' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {gogDownloads.map(g => (
                    <tr key={g.id}>
                      <td>
                        <div>
                          {g.fileName}
                          <div style={{ fontSize: '0.85em', color: 'var(--ctp-green)', marginTop: '2px' }}>
                            🎮 {g.gameTitle}
                          </div>
                          {g.state === 'Downloading' && g.totalBytes > 0 && (
                            <div className="progress-bar-container" style={{ marginTop: '6px' }}>
                              <div className="progress-bar-fill" style={{ width: `${g.progressPercent}%` }} />
                            </div>
                          )}
                          {g.errorMessage && (
                            <div className="status-messages">
                              <div className="status-message-item">{g.errorMessage}</div>
                            </div>
                          )}
                        </div>
                      </td>
                      <td>
                        <span style={{ color: 'var(--ctp-mauve)' }}>GOG</span>
                        <div style={{ fontSize: '0.8em', marginTop: '2px' }}>
                          <span className={`status-badge ${g.state === 'Completed' ? 'completed' : g.state === 'Failed' ? 'error' : 'downloading'}`}>
                            {g.state}
                          </span>
                          {g.state === 'Downloading' && g.progressPercent > 0 && (
                            <span style={{ marginLeft: '6px', color: 'var(--ctp-subtext0)' }}>{g.progressPercent.toFixed(1)}%</span>
                          )}
                        </div>
                      </td>
                      <td>
                        {g.totalBytes > 0 ? (
                          <>
                            {formatBytes(g.bytesDownloaded)}
                            <div style={{ fontSize: '0.8em', color: 'var(--ctp-subtext0)' }}>
                              / {formatBytes(g.totalBytes)}
                            </div>
                          </>
                        ) : (
                          formatBytes(g.bytesDownloaded)
                        )}
                      </td>
                      <td className="reason-cell" title={g.filePath}>{g.filePath}</td>
                      <td>{formatDate(g.startedAt)}</td>
                      <td>
                        <div className="control-actions">
                          <button className="delete-btn" onClick={() => handleGogCancel(g.id)} title={g.state === 'Downloading' ? 'Cancel' : 'Remove'}>✕</button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}

          {downloads.length === 0 && gogDownloads.length === 0 ? (
            <div className="empty-state">No active downloads</div>
          ) : downloads.length > 0 ? (
            <table className="downloads-table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Client</th>
                  <th>Size</th>
                  <th>Path</th>
                  <th>Added</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {downloads.slice(0, shown).map(d => (
                  <tr key={`${d.clientId}-${d.id}`}>
                    <td>
                      <div>
                        {d.name}
                        {d.gameTitle && (
                          <div style={{ fontSize: '0.85em', color: 'var(--ctp-green)', marginTop: '2px' }}>
                            🎮 {d.gameTitle}
                          </div>
                        )}
                        {!d.platformFolder && !d.gameTitle && (
                          <div className="status-messages">
                            <div className="status-message-item">Platform not detected. Assign a platform manually from the Unmapped tab.</div>
                          </div>
                        )}
                        {d.platformFolder && (
                          <div style={{ fontSize: '0.8em', color: 'var(--ctp-subtext0)', marginTop: '2px' }}>
                            {editingPlatform === `${d.clientId}-${d.id}` ? (
                              <select
                                className="platform-select"
                                value={d.platformFolder || ''}
                                onChange={e => { handleMapPlatform(d, e.target.value); }}
                                onBlur={() => setEditingPlatform(null)}
                                autoFocus
                              >
                                <option value="">- select -</option>
                                {platforms.map(p => (
                                  <option key={p.slug} value={p.folderName}>{p.name}</option>
                                ))}
                              </select>
                            ) : (
                              <span className="platform-label" onClick={() => setEditingPlatform(`${d.clientId}-${d.id}`)}>
                                📂 {d.platformFolder}
                              </span>
                            )}
                          </div>
                        )}
                        {d.statusMessages && d.statusMessages.length > 0 && (
                          <div className="status-messages">
                            {d.statusMessages.map((msg, i) => (
                              <div key={i} className="status-message-item">{msg}</div>
                            ))}
                          </div>
                        )}
                      </div>
                    </td>
                    <td>
                      {d.clientName}
                      <div style={{ fontSize: '0.8em', marginTop: '2px' }}>
                        <span className={`status-badge ${getStateBadgeClass(d.trackedState || d.state)}`}>
                          {d.trackedState || d.state}
                        </span>
                        {d.progress > 0 && d.progress < 100 && (
                          <span style={{ marginLeft: '6px', color: 'var(--ctp-subtext0)' }}>{d.progress?.toFixed(1)}%</span>
                        )}
                      </div>
                    </td>
                    <td>{formatBytes(d.size)}</td>
                    <td className="reason-cell" title={d.downloadPath}>{d.downloadPath}</td>
                    <td>{formatDate(new Date().toISOString())}</td>
                    <td>
                      <div className="control-actions">
                        {String(d.state ?? '').toLowerCase() === 'downloading' && (
                          <button className="control-btn" onClick={() => handlePause(d.clientId, d.id)} title="Pause">⏸</button>
                        )}
                        {String(d.state ?? '').toLowerCase() === 'paused' && (
                          <button className="control-btn" onClick={() => handleResume(d.clientId, d.id)} title="Resume">▶</button>
                        )}
                        {(String(d.state ?? '').toLowerCase() === 'completed' || String(d.trackedState ?? '').toLowerCase().includes('pending')) && (
                          <button className="control-btn import-btn" onClick={() => handleImport(d.clientId, d.id)} title="Import">📥</button>
                        )}
                        <button className="delete-btn" onClick={() => { setRemoveFiles(true); setRemoveTarget(d); }} title="Delete">✕</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
          <ShowMore total={downloads.length} shown={shown} onMore={showMore} />
        </div>
      )}

      {/* ===== HISTORY TAB ===== */}
      {activeTab === 'history' && (
        <div className="tab-content-area">
          <div className="history-filters">
            <input
              type="text"
              className="filter-input"
              placeholder="Search by title..."
              value={historyQuery}
              onChange={e => { setHistoryQuery(e.target.value); setHistoryPage(1); }}
            />
            <select className="filter-select" value={historyPlatform} onChange={e => { setHistoryPlatform(e.target.value); setHistoryPage(1); }}>
              <option value="">All Platforms</option>
              {platforms.map(p => (
                <option key={p.slug} value={p.folderName}>{p.name}</option>
              ))}
            </select>
            <select className="filter-select" value={historyState} onChange={e => { setHistoryState(e.target.value); setHistoryPage(1); }}>
              <option value="">All States</option>
              <option value="Imported">Imported</option>
              <option value="ImportFailed">Failed</option>
              <option value="Ignored">Ignored</option>
            </select>
          </div>

          <div className="downloads-table-container">
            {history.length === 0 ? (
              <div className="empty-state">No history entries found</div>
            ) : (
              <table className="downloads-table">
                <thead>
                  <tr>
                    <th className="sortable-th" onClick={() => handleHistorySort('title')}>
                      Title {historySort === 'title' && (historySortDesc ? '▼' : '▲')}
                    </th>
                    <th className="sortable-th" onClick={() => handleHistorySort('platform')}>
                      Platform {historySort === 'platform' && (historySortDesc ? '▼' : '▲')}
                    </th>
                    <th className="sortable-th" onClick={() => handleHistorySort('state')}>
                      State {historySort === 'state' && (historySortDesc ? '▼' : '▲')}
                    </th>
                    <th className="sortable-th" onClick={() => handleHistorySort('importedAt')}>
                      Date {historySort === 'importedAt' && (historySortDesc ? '▼' : '▲')}
                    </th>
                    <th>Client</th>
                    <th className="sortable-th" onClick={() => handleHistorySort('size')}>
                      Size {historySort === 'size' && (historySortDesc ? '▼' : '▲')}
                    </th>
                    <th>Reason</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map(h => (
                    <tr key={h.id}>
                      <td title={h.title}>{h.cleanTitle || h.title}</td>
                      <td><span className="platform-label">{h.platform || '-'}</span></td>
                      <td><span className={`status-badge ${getStateBadgeClass(h.state)}`}>{h.state}</span></td>
                      <td>{formatDate(h.importedAt)}</td>
                      <td>{h.clientName}</td>
                      <td>{formatBytes(h.size)}</td>
                      <td className="reason-cell" title={h.reason || ''}>{h.reason || ''}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {historyTotalPages > 1 && (
            <div className="pagination">
              <button disabled={historyPage <= 1} onClick={() => setHistoryPage(p => p - 1)}>← Prev</button>
              <span>Page {historyPage} of {historyTotalPages}</span>
              <button disabled={historyPage >= historyTotalPages} onClick={() => setHistoryPage(p => p + 1)}>Next →</button>
            </div>
          )}
        </div>
      )}

      {/* ===== FAILED TAB ===== */}
      {activeTab === 'failed' && (
        <div className="downloads-table-container">
          {failed.length === 0 ? (
            <div className="empty-state">No failed imports</div>
          ) : (
            <table className="downloads-table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Platform</th>
                  <th>Reason</th>
                  <th>Date</th>
                  <th>Client</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {failed.slice(0, shown).map(f => (
                  <tr key={f.id}>
                    <td title={f.title}>{f.cleanTitle || f.title}</td>
                    <td><span className="platform-label">{f.platform || '-'}</span></td>
                    <td className="reason-cell" title={f.reason || ''}>{f.reason || 'Unknown'}</td>
                    <td>{formatDate(f.importedAt)}</td>
                    <td>{f.clientName}</td>
                    <td>
                      <div className="control-actions">
                        <button className="control-btn" onClick={() => handleHistoryDismiss(f.id)} title="Dismiss">✓</button>
                        <button className="control-btn blacklist-btn" onClick={() => handleHistoryBlacklist(f.id)} title="Blacklist">🚫</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <ShowMore total={failed.length} shown={shown} onMore={showMore} />
        </div>
      )}

      {/* ===== UNMAPPED TAB ===== */}
      {activeTab === 'unmapped' && (
        <div className="downloads-table-container">
          {unmapped.length === 0 ? (
            <div className="empty-state">No unmapped downloads</div>
          ) : (
            <table className="downloads-table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Client</th>
                  <th>Size</th>
                  <th>Path</th>
                  <th>Added</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {unmapped.map(u => (
                  <tr key={`${u.downloadClientId}-${u.downloadId}`}>
                    <td>
                      <div>
                        {u.title}
                        {u.gameTitle && (
                          <div style={{ fontSize: '0.85em', color: 'var(--ctp-green)', marginTop: '2px' }}>
                            🎮 {u.gameTitle}
                            {u.gamePlatform && <span style={{ marginLeft: '8px', color: 'var(--ctp-subtext0)' }}>📂 {u.gamePlatform}</span>}
                          </div>
                        )}
                        {u.statusMessages && u.statusMessages.length > 0 && (
                          <div className="status-messages">
                            {u.statusMessages.map((msg, i) => (
                              <div key={i} className="status-message-item">{msg}</div>
                            ))}
                          </div>
                        )}
                      </div>
                    </td>
                    <td>{u.downloadClientName}</td>
                    <td>{formatBytes(u.size)}</td>
                    <td className="reason-cell" title={u.outputPath || ''}>{u.outputPath || '-'}</td>
                    <td>{formatDate(u.added)}</td>
                    <td>
                      <div className="control-actions">
                        <button className="control-btn import-btn" onClick={() => { setMapModal(u); setFileError(null); setMapPlatform(u.gamePlatform || ''); setMapGameId(u.gameId || ''); setMapFileType(''); setMapFileTouched(false); setMapGameSearch(''); }} title="Map & Import">
                          📥
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {/* ===== BLACKLIST TAB ===== */}
      {activeTab === 'blacklist' && (
        <div className="tab-content-area">
          <div className="history-filters">
            <input
              type="text"
              className="filter-input"
              placeholder="Search blacklist..."
              value={blacklistQuery}
              onChange={e => setBlacklistQuery(e.target.value)}
            />
          </div>
          <div className="downloads-table-container">
            {blacklist.length === 0 ? (
              <div className="empty-state">No blacklisted downloads</div>
            ) : (
              <table className="downloads-table">
                <thead>
                  <tr>
                    <th>Title</th>
                    <th>Platform</th>
                    <th>Reason</th>
                    <th>Date</th>
                    <th>Client</th>
                    <th style={{ textAlign: 'right' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {blacklist.slice(0, shown).map(b => (
                    <tr key={b.id}>
                      <td>{b.title}</td>
                      <td><span className="platform-label">{b.platform || '-'}</span></td>
                      <td className="reason-cell" title={b.reason}>{b.reason}</td>
                      <td>{formatDate(b.blacklistedAt)}</td>
                      <td>{b.clientName || '-'}</td>
                      <td>
                        <div className="control-actions">
                          <button className="delete-btn" onClick={() => handleBlacklistRemove(b.id)} title="Remove from blacklist">✕</button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <ShowMore total={blacklist.length} shown={shown} onMore={showMore} />
          </div>
        </div>
      )}

      {/* ===== NOT MAPPED FILES TAB ===== */}
      {activeTab === 'unmapped-files' && (
        <div className="tab-content-area">
          <p className="tab-description">{t('notMappedFilesDesc')}</p>
          <div className="downloads-table-container">
            {unmappedFilesLoading ? (
              <div className="empty-state">Loading...</div>
            ) : unmappedFiles.length === 0 ? (
              <div className="empty-state">{t('noFilesFound')}</div>
            ) : (
              <table className="downloads-table">
                <thead>
                  <tr>
                    <th>Platform</th>
                    <th>Folder</th>
                    <th>File</th>
                    <th>Size</th>
                    <th>Modified</th>
                    <th style={{ textAlign: 'right' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {unmappedFiles.slice(0, shown).map((f, idx) => (
                    <tr key={idx}>
                      <td><span className="platform-label">{f.platformFolder || '-'}</span></td>
                      <td className="reason-cell" title={f.folder}>{f.folder}</td>
                      <td title={f.fullPath}>{f.fileName}</td>
                      <td>{f.formattedSize}</td>
                      <td>{formatDate(f.lastModified)}</td>
                      <td>
                        <div className="control-actions">
                          <button className="control-btn import-btn" onClick={() => { setMapFileModal(f); setMapFileGameId(''); setMapFileSearch(''); setFileError(null); }} title={t('mapToGame')}>
                            📥
                          </button>
                          <button className="control-btn import-btn" onClick={() => { setCreateGameModal(f); setCreateGameTitle(f.folder || ''); setFileError(null); }} title="Create new game">
                            ➕
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            {!unmappedFilesLoading && <ShowMore total={unmappedFiles.length} shown={shown} onMore={showMore} />}
          </div>
        </div>
      )}

      {/* ===== MAP & IMPORT MODAL ===== */}
      {mapModal && (
        <div className="modal-overlay" onClick={() => setMapModal(null)}>
          <div className="modal-content map-modal" onClick={e => e.stopPropagation()}>
            <h2>Map & Import</h2>
            <div className="modal-field">
              <label>Download Title</label>
              <div className="modal-value">{mapModal.title}</div>
            </div>
            <div className="modal-field">
              <label>Download Path</label>
              <div className="modal-value small">{mapModal.outputPath || '-'}</div>
            </div>
            <div className="modal-field">
              <label>Platform <span className="required">*</span></label>
              <select className="filter-select" value={mapPlatform} onChange={e => { setMapPlatform(e.target.value); setMapGameId(''); setMapGameSearch(''); }}>
                <option value="">- Select Platform -</option>
                {platforms.map(p => (
                  <option key={p.slug} value={p.folderName}>{p.name}</option>
                ))}
              </select>
            </div>
            <div className="modal-field">
              <label>File Type <span className="required">*</span></label>
              <select className="filter-select" value={mapFileType} onChange={e => { setMapFileType(e.target.value as '' | 'Main' | 'Patches' | 'DLC'); setMapFileTouched(true); }}>
                <option value="">{t('monitorDecisionAuto')}</option>
                <option value="Main">Main Game</option>
                <option value="Patches">Update / Patch</option>
                <option value="DLC">DLC</option>
              </select>
              {!mapFileTouched && (mapFileType === 'Patches' || mapFileType === 'DLC') && (
                <div style={{ fontSize: '0.8em', color: 'var(--ctp-yellow)', marginTop: '4px' }}>
                  Auto-detected as {mapFileType === 'Patches' ? 'Update/Patch' : 'DLC'} from title
                </div>
              )}
            </div>
            {mapPlatform && (() => {
              const matches = mapGameOptions;
              return (
                <div className="modal-field">
                  <label>
                    {mapFileType !== 'Patches' && mapFileType !== 'DLC'
                      ? 'Map to existing game (search by name)'
                      : 'Link to game'}
                  </label>
                  <input
                    type="text"
                    className="filter-input"
                    placeholder="Search games by name..."
                    value={mapGameSearch}
                    onChange={e => { setMapGameSearch(e.target.value); setMapGameId(''); }}
                    style={{ marginBottom: '6px' }}
                  />
                  <select
                    className="filter-select"
                    size={Math.min(8, Math.max(2, matches.length + 1))}
                    value={mapGameId}
                    onChange={e => setMapGameId(e.target.value ? Number(e.target.value) : '')}
                  >
                    <option value="">
                      {mapFileType !== 'Patches' && mapFileType !== 'DLC'
                        ? '- New game from download title -'
                        : '- Select game (optional) -'}
                    </option>
                    {matches.map(g => (
                      <option key={g.id} value={g.id}>{g.title}</option>
                    ))}
                  </select>
                  <div style={{ fontSize: '0.8em', color: 'var(--ctp-subtext0)', marginTop: '4px' }}>
                    {mapGames.total} game{mapGames.total === 1 ? '' : 's'} on this platform
                    {mapGameSearch ? ` matching "${mapGameSearch}"` : ''}
                    {mapGames.total > mapGames.items.length ? `, first ${mapGames.items.length} shown` : ''}
                  </div>
                </div>
              );
            })()}
            {mapPlatform && (
              <div className="modal-field">
                <label>Target Folder Preview</label>
                <div className="modal-value small">
                  Library/{mapPlatform}/{mapGameId ? (mapGameOptions.find(g => g.id === mapGameId)?.title || mapModal.title) : mapModal.title}/{mapFileType === 'Patches' ? 'Patches/' : mapFileType === 'DLC' ? 'DLC/' : ''}
                </div>
              </div>
            )}
            {fileError && <div className="modal-value small" style={{ color: 'var(--ctp-red)' }}>{t('error')}: {fileError}</div>}
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setMapModal(null)}>Cancel</button>
              <button className="btn-primary" disabled={!mapPlatform} onClick={handleMapAndImport}>
                Import Now
              </button>
            </div>
          </div>
        </div>
      )}

      {removeTarget && (
        <div className="modal-overlay" onClick={() => { if (!removing) setRemoveTarget(null); }}>
          <div className="modal-content map-modal" onClick={e => e.stopPropagation()}>
            <h2>{t('deleteDownload')}</h2>
            <div className="modal-field">
              <label>{removeTarget.clientName}</label>
              <div className="modal-value">{removeTarget.name}</div>
            </div>
            <label style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <input type="checkbox" checked={removeFiles} onChange={e => setRemoveFiles(e.target.checked)} />
              Also delete the downloaded files
            </label>
            <div className="modal-value small">Files already imported into the library are not affected.</div>
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setRemoveTarget(null)} disabled={removing}>Cancel</button>
              <button className="btn-primary" onClick={handleDelete} disabled={removing}>Remove</button>
            </div>
          </div>
        </div>
      )}

      {/* ===== MAP FILE TO GAME MODAL ===== */}
      {mapFileModal && (
        <div className="modal-overlay" onClick={() => setMapFileModal(null)}>
          <div className="modal-content map-modal" onClick={e => e.stopPropagation()}>
            <h2>{t('mapToGame')}</h2>
            <div className="modal-field">
              <label>File</label>
              <div className="modal-value">{mapFileModal.fileName}</div>
            </div>
            <div className="modal-field">
              <label>Full Path</label>
              <div className="modal-value small">{mapFileModal.fullPath}</div>
            </div>
            <div className="modal-field">
              <label>Folder</label>
              <div className="modal-value small">{mapFileModal.folder}</div>
            </div>
            <div className="modal-field">
              <label>Platform</label>
              <div className="modal-value">{mapFileModal.platformFolder || '-'}</div>
            </div>
            <div className="modal-field">
              <label>Game <span className="required">*</span></label>
              <input
                type="text"
                className="filter-input"
                placeholder="Search games by name..."
                value={mapFileSearch}
                onChange={e => { setMapFileSearch(e.target.value); setMapFileGameId(''); }}
                style={{ marginBottom: '6px' }}
              />
              <select className="filter-select" value={mapFileGameId} onChange={e => setMapFileGameId(e.target.value ? Number(e.target.value) : '')}>
                <option value="">- Select Game -</option>
                {mapFileGames.items.map(g => (
                  <option key={g.id} value={g.id}>{g.title}</option>
                ))}
              </select>
              {mapFileGames.total > mapFileGames.items.length && (
                <div style={{ fontSize: '0.8em', color: 'var(--ctp-subtext0)', marginTop: '4px' }}>
                  First {mapFileGames.items.length} of {mapFileGames.total} games, search to narrow
                </div>
              )}
            </div>
            {fileError && <div className="modal-value small" style={{ color: 'var(--ctp-red)' }}>{t('error')}: {fileError}</div>}
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setMapFileModal(null)}>Cancel</button>
              <button className="btn-primary" disabled={!mapFileGameId} onClick={handleMapFileToGame}>
                {t('mapToGame')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ===== CREATE GAME FROM FILE MODAL ===== */}
      {createGameModal && (
        <div className="modal-overlay" onClick={() => setCreateGameModal(null)}>
          <div className="modal-content map-modal" onClick={e => e.stopPropagation()}>
            <h2>Create Game From File</h2>
            <div className="modal-field">
              <label>File</label>
              <div className="modal-value">{createGameModal.fileName}</div>
            </div>
            <div className="modal-field">
              <label>Full Path</label>
              <div className="modal-value small">{createGameModal.fullPath}</div>
            </div>
            <div className="modal-field">
              <label>Platform</label>
              <div className="modal-value">{createGameModal.platformFolder || '-'}</div>
            </div>
            <div className="modal-field">
              <label>Game Title</label>
              <input
                type="text"
                className="filter-select"
                value={createGameTitle}
                onChange={e => setCreateGameTitle(e.target.value)}
                placeholder="Enter game title..."
              />
            </div>
            {fileError && <div className="modal-value small" style={{ color: 'var(--ctp-red)' }}>{t('error')}: {fileError}</div>}
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setCreateGameModal(null)}>Cancel</button>
              <button className="btn-primary" disabled={!createGameTitle.trim()} onClick={handleCreateGameFromFile}>
                Create Game
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default Status;
