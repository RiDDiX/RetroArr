import { useState, useEffect, useCallback } from 'react';
import { useLocation } from 'react-router-dom';
import { useTranslation } from '../i18n/translations';
import './Status.css';

const API_BASE = '/api/v3/downloadclient';

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

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

function formatDate(dateStr: string): string {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
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
  const [activeTab, setActiveTab] = useState<TabKey>('activity');
  const [counts, setCounts] = useState<Counts>({ active: 0, failed: 0, unmapped: 0, blacklisted: 0 });

  // Sync tab from URL hash
  useEffect(() => {
    const hash = location.hash.replace('#', '') as TabKey;
    const validTabs: TabKey[] = ['activity', 'history', 'failed', 'unmapped', 'unmapped-files', 'blacklist'];
    if (hash && validTabs.includes(hash)) {
      setActiveTab(hash);
    }
  }, [location.hash]);

  // Activity state
  const [downloads, setDownloads] = useState<DownloadStatus[]>([]);
  const [editingPlatform, setEditingPlatform] = useState<string | null>(null);

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
  const [mapGameSearch, setMapGameSearch] = useState('');

  // Blacklist state
  const [blacklist, setBlacklist] = useState<BlacklistEntry[]>([]);
  const [blacklistQuery, setBlacklistQuery] = useState('');

  // Unmapped local files state
  const [unmappedFiles, setUnmappedFiles] = useState<UnmappedLocalFile[]>([]);
  const [unmappedFilesLoading, setUnmappedFilesLoading] = useState(false);
  const [gamesList, setGamesList] = useState<Array<{ id: number; title: string; platformId: number }>>([]);
  const [mapFileModal, setMapFileModal] = useState<UnmappedLocalFile | null>(null);
  const [mapFileGameId, setMapFileGameId] = useState<number | ''>('');
  const [createGameModal, setCreateGameModal] = useState<UnmappedLocalFile | null>(null);
  const [createGameTitle, setCreateGameTitle] = useState('');

  // Platforms
  const [platforms, setPlatforms] = useState<PlatformOption[]>([]);

  // Fetch platforms once
  useEffect(() => {
    fetch('/api/v3/platform')
      .then(r => r.json())
      .then(data => {
        if (Array.isArray(data)) setPlatforms(data.filter((p: PlatformOption) => p.enabled));
      })
      .catch(() => {});
  }, []);

  // Fetch counts on interval
  const fetchCounts = useCallback(() => {
    fetch(`${API_BASE}/counts`)
      .then(r => r.json())
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
    const fetchQueue = () => {
      fetch(`${API_BASE}/queue`)
        .then(r => r.json())
        .then(data => { if (Array.isArray(data)) setDownloads(data); })
        .catch(() => {});
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
      fetch('/api/v3/settings/gog/download-status')
        .then(r => r.json())
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

    fetch(`${API_BASE}/history?${params}`)
      .then(r => r.json())
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
    fetch(`${API_BASE}/history/failed`)
      .then(r => r.json())
      .then(data => { if (Array.isArray(data)) setFailed(data); })
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (activeTab === 'failed') fetchFailed();
  }, [activeTab, fetchFailed]);

  // Unmapped fetch
  const fetchUnmapped = useCallback(() => {
    fetch(`${API_BASE}/unmapped`)
      .then(r => r.json())
      .then(data => { if (Array.isArray(data)) setUnmapped(data); })
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (activeTab === 'unmapped') fetchUnmapped();
  }, [activeTab, fetchUnmapped]);

  // Blacklist fetch
  const fetchBlacklist = useCallback(() => {
    const params = blacklistQuery ? `?query=${encodeURIComponent(blacklistQuery)}` : '';
    fetch(`${API_BASE}/blacklist${params}`)
      .then(r => r.json())
      .then(data => { if (Array.isArray(data)) setBlacklist(data); })
      .catch(() => {});
  }, [blacklistQuery]);

  useEffect(() => {
    if (activeTab === 'blacklist') fetchBlacklist();
  }, [activeTab, fetchBlacklist]);

  // Unmapped local files fetch
  const fetchUnmappedFiles = useCallback(() => {
    setUnmappedFilesLoading(true);
    fetch('/api/v3/game/unmapped-files')
      .then(r => r.json())
      .then(data => { if (Array.isArray(data)) setUnmappedFiles(data); })
      .catch(() => {})
      .finally(() => setUnmappedFilesLoading(false));
  }, []);

  useEffect(() => {
    if (activeTab === 'unmapped-files') {
      fetchUnmappedFiles();
    }
    if (activeTab === 'unmapped' || activeTab === 'unmapped-files') {
      fetch('/api/v3/game')
        .then(r => r.json())
        .then(data => { if (Array.isArray(data)) setGamesList(data.map((g: { id: number; title: string; platformId: number }) => ({ id: g.id, title: g.title, platformId: g.platformId }))); })
        .catch(() => {});
    }
  }, [activeTab, fetchUnmappedFiles]);

  // Actions
  const handlePause = async (clientId: number, downloadId: string) => {
    await fetch(`${API_BASE}/queue/${clientId}/${encodeURIComponent(downloadId)}/pause`, { method: 'POST' });
  };

  const handleResume = async (clientId: number, downloadId: string) => {
    await fetch(`${API_BASE}/queue/${clientId}/${encodeURIComponent(downloadId)}/resume`, { method: 'POST' });
  };

  const handleDelete = async (clientId: number, downloadId: string) => {
    await fetch(`${API_BASE}/queue/${clientId}/${encodeURIComponent(downloadId)}`, { method: 'DELETE' });
  };

  const handleImport = async (clientId: number, downloadId: string) => {
    await fetch(`${API_BASE}/queue/${clientId}/${encodeURIComponent(downloadId)}/import`, { method: 'POST' });
  };

  const handleGogCancel = async (trackId: string) => {
    await fetch(`/api/v3/settings/gog/download-status/${trackId}`, { method: 'DELETE' });
  };

  const handleMapPlatform = async (downloadName: string, platformFolder: string) => {
    await fetch(`${API_BASE}/queue/map-platform`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ downloadName, platformFolder })
    });
    setEditingPlatform(null);
  };

  const handleHistoryDismiss = async (id: number) => {
    await fetch(`${API_BASE}/history/${id}/dismiss`, { method: 'POST' });
    fetchFailed();
    fetchHistory();
    fetchCounts();
  };

  const handleHistoryBlacklist = async (id: number) => {
    await fetch(`${API_BASE}/history/${id}/blacklist`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason: 'Blacklisted by user' })
    });
    fetchFailed();
    fetchHistory();
    fetchBlacklist();
    fetchCounts();
  };

  const handleBlacklistRemove = async (id: number) => {
    await fetch(`${API_BASE}/blacklist/${id}`, { method: 'DELETE' });
    fetchBlacklist();
    fetchCounts();
  };

  const detectFileType = (title: string): '' | 'Main' | 'Patches' | 'DLC' => {
    if (!title) return '';
    const t = title.toLowerCase();
    if (/\bdlc\b/.test(t) || /[-.]dlc[-.]/i.test(title)) return 'DLC';
    if (/\bupdate\b/.test(t) || /\bpatch\b/.test(t) || /\bhotfix\b/.test(t) || /\bfix\b/.test(t)) return 'Patches';
    return 'Main';
  };

  const handleMapAndImport = async () => {
    if (!mapModal || !mapPlatform) return;
    // 1. Set platform + game + subfolder mapping
    await fetch(`${API_BASE}/queue/map-platform`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        downloadName: mapModal.title,
        platformFolder: mapPlatform,
        gameId: mapGameId || null,
        importSubfolder: mapFileType === 'Main' || mapFileType === '' ? null : mapFileType
      })
    });
    // 2. Trigger import
    await fetch(`${API_BASE}/queue/${mapModal.downloadClientId}/${encodeURIComponent(mapModal.downloadId)}/import`, { method: 'POST' });
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

  const handleMapFileToGame = async () => {
    if (!mapFileModal || !mapFileGameId) return;
    try {
      await fetch(`/api/v3/game/${mapFileGameId}/map-file`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ filePath: mapFileModal.fullPath })
      });
      setMapFileModal(null);
      setMapFileGameId('');
      fetchUnmappedFiles();
    } catch { /* ignore */ }
  };

  const handleCreateGameFromFile = async () => {
    if (!createGameModal) return;
    try {
      await fetch('/api/v3/game/create-from-file', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          filePath: createGameModal.fullPath,
          title: createGameTitle || undefined,
          platformFolder: createGameModal.platformFolder || undefined
        })
      });
      setCreateGameModal(null);
      setCreateGameTitle('');
      fetchUnmappedFiles();
    } catch { /* ignore */ }
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
                {downloads.map(d => (
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
                            {editingPlatform === d.id ? (
                              <select
                                className="platform-select"
                                value={d.platformFolder || ''}
                                onChange={e => { handleMapPlatform(d.name, e.target.value); }}
                                onBlur={() => setEditingPlatform(null)}
                                autoFocus
                              >
                                <option value="">- select -</option>
                                {platforms.map(p => (
                                  <option key={p.slug} value={p.folderName}>{p.name}</option>
                                ))}
                              </select>
                            ) : (
                              <span className="platform-label" onClick={() => setEditingPlatform(d.id)}>
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
                        <button className="delete-btn" onClick={() => handleDelete(d.clientId, d.id)} title="Delete">✕</button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
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
                {failed.map(f => (
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
                  <tr key={u.downloadId}>
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
                        <button className="control-btn import-btn" onClick={() => { setMapModal(u); setMapPlatform(u.gamePlatform || ''); setMapGameId(u.gameId || ''); setMapFileType(detectFileType(u.title)); setMapGameSearch(''); }} title="Map & Import">
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
                  {blacklist.map(b => (
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
                  {unmappedFiles.map((f, idx) => (
                    <tr key={idx}>
                      <td><span className="platform-label">{f.platformFolder || '-'}</span></td>
                      <td className="reason-cell" title={f.folder}>{f.folder}</td>
                      <td title={f.fullPath}>{f.fileName}</td>
                      <td>{f.formattedSize}</td>
                      <td>{formatDate(f.lastModified)}</td>
                      <td>
                        <div className="control-actions">
                          <button className="control-btn import-btn" onClick={() => { setMapFileModal(f); setMapFileGameId(''); }} title={t('mapToGame')}>
                            📥
                          </button>
                          <button className="control-btn import-btn" onClick={() => { setCreateGameModal(f); setCreateGameTitle(f.folder || ''); }} title="Create new game">
                            ➕
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
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
              <select className="filter-select" value={mapFileType} onChange={e => setMapFileType(e.target.value as 'Main' | 'Patches' | 'DLC')}>
                <option value="Main">Main Game</option>
                <option value="Patches">Update / Patch</option>
                <option value="DLC">DLC</option>
              </select>
              {mapFileType !== 'Main' && mapFileType !== '' && (
                <div style={{ fontSize: '0.8em', color: 'var(--ctp-yellow)', marginTop: '4px' }}>
                  Auto-detected as {mapFileType === 'Patches' ? 'Update/Patch' : 'DLC'} from title
                </div>
              )}
            </div>
            {mapPlatform && (mapFileType === 'Patches' || mapFileType === 'DLC') && (
              <div className="modal-field">
                <label>Link to Game</label>
                <input
                  type="text"
                  className="filter-input"
                  placeholder="Search games..."
                  value={mapGameSearch}
                  onChange={e => { setMapGameSearch(e.target.value); setMapGameId(''); }}
                  style={{ marginBottom: '6px' }}
                />
                <select
                  className="filter-select"
                  value={mapGameId}
                  onChange={e => setMapGameId(e.target.value ? Number(e.target.value) : '')}
                >
                  <option value="">- Select Game (optional) -</option>
                  {gamesList
                    .filter(g => {
                      const platDef = platforms.find(p => p.folderName === mapPlatform);
                      const matchesPlatform = !platDef || g.platformId === platDef.id;
                      const matchesSearch = !mapGameSearch || g.title.toLowerCase().includes(mapGameSearch.toLowerCase());
                      return matchesPlatform && matchesSearch;
                    })
                    .map(g => (
                      <option key={g.id} value={g.id}>{g.title}</option>
                    ))}
                </select>
              </div>
            )}
            {mapPlatform && (
              <div className="modal-field">
                <label>Target Folder Preview</label>
                <div className="modal-value small">
                  Library/{mapPlatform}/{mapGameId ? (gamesList.find(g => g.id === mapGameId)?.title || mapModal.title) : mapModal.title}/{mapFileType === 'Patches' ? 'Patches/' : mapFileType === 'DLC' ? 'DLC/' : ''}
                </div>
              </div>
            )}
            <div className="modal-actions">
              <button className="btn-secondary" onClick={() => setMapModal(null)}>Cancel</button>
              <button className="btn-primary" disabled={!mapPlatform} onClick={handleMapAndImport}>
                Import Now
              </button>
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
              <select className="filter-select" value={mapFileGameId} onChange={e => setMapFileGameId(e.target.value ? Number(e.target.value) : '')}>
                <option value="">- Select Game -</option>
                {gamesList.map(g => (
                  <option key={g.id} value={g.id}>{g.title}</option>
                ))}
              </select>
            </div>
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
