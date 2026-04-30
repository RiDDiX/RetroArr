import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import apiClient, { getErrorMessage, isAxiosError } from '../../api/client';
import FolderExplorerModal from '../FolderExplorerModal';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faFolderOpen, faSync, faSearch } from '@fortawesome/free-solid-svg-icons';

interface MediaTabProps {
  language: string;
  t: (key: string) => string;
}

const MediaTab: React.FC<MediaTabProps> = ({ language, t }) => {
  const navigate = useNavigate();
  const [pendingDiscoveries, setPendingDiscoveries] = useState<number>(0);
  const [folderPath, setFolderPath] = useState('');
  const [downloadPath, setDownloadPath] = useState('');
  const [destinationPath, setDestinationPath] = useState('');
  const [destinationPathPattern, setDestinationPathPattern] = useState('{Platform}/{Title}');
  const [useDestinationPattern, setUseDestinationPattern] = useState(true);
  const [folderNamingMode, setFolderNamingMode] = useState('native');
  const [winePrefixPath, setWinePrefixPath] = useState('');
  const [biosPath, setBiosPath] = useState('');
  const [biosFiles, setBiosFiles] = useState<{ filename: string; present: boolean }[]>([]);
  const [trashPath, setTrashPath] = useState('');
  const [trashRetentionDays, setTrashRetentionDays] = useState(14);
  const [missingRetentionDays, setMissingRetentionDays] = useState(14);
  const [scanning, setScanning] = useState(false);
  const [permissionsReport, setPermissionsReport] = useState<{
    processUid?: number | null;
    processGid?: number | null;
    puidEnv?: string | null;
    pgidEnv?: string | null;
    checks: { key: string; path: string; exists: boolean; readable: boolean; writable: boolean; hint?: string | null }[];
  } | null>(null);
  const [permissionsLoading, setPermissionsLoading] = useState(false);
  const [showFolderExplorer, setShowFolderExplorer] = useState(false);
  const [activeFolderField, setActiveFolderField] = useState<'media' | 'download' | 'destination' | 'wine' | 'bios'>('media');

  const [postDownloadSettings, setPostDownloadSettings] = useState({
    enableAutoMove: true,
    enableAutoExtract: true,
    enableDeepClean: true,
    monitorIntervalSeconds: 60,
    unwantedExtensions: ['.txt', '.nfo', '.url']
  });

  useEffect(() => {
    loadSettings();

    const handleFolderSelected = (event: Event) => {
      const customEvent = event as CustomEvent;
      setFolderPath(customEvent.detail);
    };

    const handleSettingsUpdated = () => {
      loadSettings();
    };

    window.addEventListener('FOLDER_SELECTED_EVENT', handleFolderSelected);
    window.addEventListener('SETTINGS_UPDATED_EVENT', handleSettingsUpdated);

    return () => {
      window.removeEventListener('FOLDER_SELECTED_EVENT', handleFolderSelected);
      window.removeEventListener('SETTINGS_UPDATED_EVENT', handleSettingsUpdated);
    };
  }, []);

  const loadPermissions = async () => {
    setPermissionsLoading(true);
    try {
      const res = await apiClient.get('/media/permissions');
      setPermissionsReport(res.data);
    } catch (err) {
      console.error('Permissions check failed:', err);
    } finally {
      setPermissionsLoading(false);
    }
  };

  useEffect(() => { loadPermissions(); }, []);

  // poll pending discovery count so the badge stays current after import or scan
  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      try {
        const res = await apiClient.get<{ count: number }>('/discovery/count');
        if (!cancelled) setPendingDiscoveries(res.data?.count ?? 0);
      } catch {
        if (!cancelled) setPendingDiscoveries(0);
      }
    };
    tick();
    const id = setInterval(tick, 8000);
    return () => { cancelled = true; clearInterval(id); };
  }, []);

  const triggerDiscoveryScan = async () => {
    try {
      await apiClient.post('/discovery/scan');
      navigate('/discover');
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  // Monitor Scan Status
  useEffect(() => {
    let intervalId: ReturnType<typeof setInterval> | undefined;
    if (scanning) {
      intervalId = setInterval(async () => {
        try {
          const response = await apiClient.get('/media/scan/status');
          if (!response.data.isScanning) setScanning(false);
        } catch {
          setScanning(false);
        }
      }, 1000);
    }
    return () => { if (intervalId) clearInterval(intervalId); };
  }, [scanning]);

  const loadSettings = async () => {
    try {
      const [mediaRes, postDownloadRes] = await Promise.all([
        apiClient.get('/media'),
        apiClient.get('/postdownload'),
      ]);
      setFolderPath(mediaRes.data.folderPath);
      setDownloadPath(mediaRes.data.downloadPath || '');
      setDestinationPath(mediaRes.data.destinationPath || '');
      setDestinationPathPattern(mediaRes.data.destinationPathPattern || '{Platform}/{Title}');
      setUseDestinationPattern(mediaRes.data.useDestinationPattern !== false);
      setFolderNamingMode(mediaRes.data.folderNamingMode || 'native');
      setWinePrefixPath(mediaRes.data.winePrefixPath || '');
      setBiosPath(mediaRes.data.biosPath || '');
      setTrashPath(mediaRes.data.trashPath || '');
      setTrashRetentionDays(typeof mediaRes.data.trashRetentionDays === 'number' ? mediaRes.data.trashRetentionDays : 14);
      setMissingRetentionDays(typeof mediaRes.data.missingRetentionDays === 'number' ? mediaRes.data.missingRetentionDays : 14);
      setPostDownloadSettings(postDownloadRes.data);

      try {
        const biosRes = await apiClient.get('/emulator/bios');
        setBiosFiles(biosRes.data?.files ?? []);
        if (!mediaRes.data.biosPath && biosRes.data?.biosDirectory) {
          setBiosPath(biosRes.data.biosDirectory);
        }
      } catch { /* non-fatal */ }
    } catch (error) {
      console.error('Error loading media settings:', error);
    }
  };

  const saveMediaConfig = async (overrides?: { folderPath?: string; downloadPath?: string; destinationPath?: string; destinationPathPattern?: string; useDestinationPattern?: boolean; folderNamingMode?: string; winePrefixPath?: string; biosPath?: string; trashPath?: string; trashRetentionDays?: number; missingRetentionDays?: number }) => {
    try {
      await apiClient.post('/media', {
        FolderPath: overrides?.folderPath ?? folderPath,
        DownloadPath: overrides?.downloadPath ?? downloadPath,
        DestinationPath: overrides?.destinationPath ?? destinationPath,
        DestinationPathPattern: overrides?.destinationPathPattern ?? destinationPathPattern,
        UseDestinationPattern: overrides?.useDestinationPattern ?? useDestinationPattern,
        FolderNamingMode: overrides?.folderNamingMode ?? folderNamingMode,
        WinePrefixPath: overrides?.winePrefixPath ?? winePrefixPath,
        BiosPath: overrides?.biosPath ?? biosPath,
        TrashPath: overrides?.trashPath ?? trashPath,
        TrashRetentionDays: overrides?.trashRetentionDays ?? trashRetentionDays,
        MissingRetentionDays: overrides?.missingRetentionDays ?? missingRetentionDays,
        Platform: 'default'
      });
    } catch (error: unknown) {
      alert(`${t('error')} ${t('mediaSettingsSaved')}: ${getErrorMessage(error)}`);
    }
  };

  const handleSaveMediaSettings = (e: React.FormEvent) => {
    e.preventDefault();
    saveMediaConfig();
  };

  const handleScanNow = async (specificPath?: string) => {
    setScanning(true);
    try {
      await apiClient.post('/media/scan', { folderPath: specificPath || folderPath, platform: 'default' });
    } catch (error: unknown) {
      setScanning(false);
      if (isAxiosError(error) && error.response?.status === 400) {
        alert(t('igdbRequired'));
      } else {
        alert(`${t('error')} ${t('scanNow')}: ${getErrorMessage(error)}`);
      }
    }
  };

  const [healing, setHealing] = useState(false);
  const handleHealPlatforms = async () => {
    if (!window.confirm(t('healPlatformsConfirm') || 'Walk every game and fix rows whose platform disagrees with their path?')) return;
    setHealing(true);
    try {
      const res = await apiClient.post('/media/heal-platforms');
      const { healed = 0, dupesDropped = 0 } = res.data || {};
      alert(`${t('healPlatformsDone') || 'Heal done.'} healed: ${healed}, dropped: ${dupesDropped}`);
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    } finally {
      setHealing(false);
    }
  };

  const startFolderPolling = () => {
    setScanning(true);
    let attempts = 0;
    const initialPath = folderPath;
    const pollInterval = setInterval(async () => {
      attempts++;
      try {
        const response = await apiClient.get(`/media?t=${Date.now()}`);
        const currentPath = response.data.folderPath;
        const currentDownloadPath = response.data.downloadPath;
        const currentDestinationPath = response.data.destinationPath;
        if (currentPath !== initialPath || currentDownloadPath !== downloadPath || currentDestinationPath !== destinationPath) {
          setFolderPath(currentPath);
          setDownloadPath(currentDownloadPath || '');
          setDestinationPath(currentDestinationPath || '');
          clearInterval(pollInterval);
          setScanning(false);
        }
      } catch (e) {
        console.error("Polling error", e);
      }
      if (attempts > 60) { clearInterval(pollInterval); setScanning(false); }
    }, 500);
  };

  const handleSavePostDownload = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/postdownload', postDownloadSettings);
      alert(t('postDownloadSettingsSaved'));
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  return (
    <>
      <div className="settings-section" id="media">
        <div className="section-header-with-logo">
          <h3>{t('mediaFolderTitle')}</h3>
        </div>
        <p className="settings-description">{t('mediaFolderDesc')}</p>

        {pendingDiscoveries > 0 && (
          <div
            style={{
              marginBottom: '12px',
              padding: '8px 12px',
              borderRadius: '6px',
              border: '1px solid var(--ctp-yellow)',
              background: 'rgba(249, 226, 175, 0.1)',
              display: 'flex',
              alignItems: 'center',
              gap: '10px',
              flexWrap: 'wrap'
            }}
          >
            <span style={{ color: 'var(--ctp-text)' }}>
              {pendingDiscoveries} discovered game{pendingDiscoveries === 1 ? '' : 's'} waiting to be imported.
            </span>
            <button type="button" className="btn-primary" style={{ marginLeft: 'auto' }} onClick={() => navigate('/discover')}>
              Continue with {pendingDiscoveries}
            </button>
          </div>
        )}
        <form onSubmit={handleSaveMediaSettings}>
          <div className="form-group">
            <label htmlFor="folder-path">{t('mediaFolderPath')}</label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <input id="folder-path" type="text" value={folderPath} onChange={(e) => setFolderPath(e.target.value)} onBlur={() => saveMediaConfig({ folderPath })} placeholder="/home/user/games" style={{ flex: 1 }} />
              <button type="button" className="btn-secondary" onClick={() => handleScanNow()} disabled={scanning || !folderPath} title={t('scanNow')}>
                <FontAwesomeIcon icon={faSync} spin={scanning} />
              </button>
              <button
                type="button"
                className="btn-secondary"
                onClick={triggerDiscoveryScan}
                disabled={scanning || !folderPath}
                title="Scan only, do not fetch metadata yet"
              >
                <FontAwesomeIcon icon={faSearch} />
              </button>
              <button type="button" className="btn-secondary" onClick={() => {
                // @ts-expect-error Photino native bridge
                if (window.external && window.external.sendMessage) {
                  startFolderPolling();
                  // @ts-expect-error Photino native bridge
                  window.external.sendMessage('SELECT_FOLDER');
                } else {
                  setActiveFolderField('media');
                  setShowFolderExplorer(true);
                }
              }} title={t('selectFolder')}>
                <FontAwesomeIcon icon={faFolderOpen} />
              </button>
            </div>
            <div style={{ marginTop: '8px' }}>
              <button
                type="button"
                className="btn-secondary"
                onClick={handleHealPlatforms}
                disabled={healing || scanning}
                title={t('healPlatformsTitle') || 'Fix rows whose stored platform disagrees with the file path'}
              >
                {healing ? (t('healPlatformsRunning') || 'Fixing…') : (t('healPlatforms') || 'Fix Wrong Platforms')}
              </button>
            </div>
          </div>

          <div className="form-group">
            <label htmlFor="download-path">{t('downloadPath')}</label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <input id="download-path" type="text" value={downloadPath} onChange={(e) => setDownloadPath(e.target.value)} onBlur={() => saveMediaConfig({ downloadPath })} placeholder="/Volumes/Downloads" style={{ flex: 1 }} />
              <button type="button" className="btn-secondary" onClick={() => {
                // @ts-expect-error Photino native bridge
                if (window.external && window.external.sendMessage) {
                  startFolderPolling();
                  // @ts-expect-error Photino native bridge
                  window.external.sendMessage('SELECT_FOLDER:DOWNLOAD');
                } else {
                  setActiveFolderField('download');
                  setShowFolderExplorer(true);
                }
              }}>
                <FontAwesomeIcon icon={faFolderOpen} />
              </button>
            </div>
          </div>

          <div className="form-group">
            <label htmlFor="destination-path">{t('destinationPath')}</label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <input id="destination-path" type="text" value={destinationPath} onChange={(e) => setDestinationPath(e.target.value)} onBlur={() => saveMediaConfig({ destinationPath })} placeholder="/Volumes/Media/Games" style={{ flex: 1 }} />
              <button type="button" className="btn-secondary" onClick={() => {
                // @ts-expect-error Photino native bridge
                if (window.external && window.external.sendMessage) {
                  startFolderPolling();
                  // @ts-expect-error Photino native bridge
                  window.external.sendMessage('SELECT_FOLDER:DESTINATION');
                } else {
                  setActiveFolderField('destination');
                  setShowFolderExplorer(true);
                }
              }}>
                <FontAwesomeIcon icon={faFolderOpen} />
              </button>
            </div>
          </div>

          <div className="form-group" style={{ marginTop: '15px', padding: '15px', background: 'var(--surface-1)', borderRadius: '8px', border: '1px solid var(--border)' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '10px' }}>
              <input type="checkbox" id="use-destination-pattern" checked={useDestinationPattern} onChange={(e) => { setUseDestinationPattern(e.target.checked); saveMediaConfig({ useDestinationPattern: e.target.checked }); }} style={{ width: 'auto' }} />
              <label htmlFor="use-destination-pattern" style={{ margin: 0, fontWeight: 600 }}>
                {t('useDestinationPattern') || 'Use Path Pattern for Downloads'}
              </label>
            </div>
            <p style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)', marginBottom: '10px' }}>
              {t('destinationPatternDesc') || 'Automatically organize downloads by platform. Available variables: {Platform}, {Title}, {Year}'}
            </p>
            <label htmlFor="destination-pattern">{t('destinationPattern') || 'Path Pattern'}</label>
            <input id="destination-pattern" type="text" value={destinationPathPattern} onChange={(e) => setDestinationPathPattern(e.target.value)} onBlur={() => saveMediaConfig({ destinationPathPattern })} placeholder="{Platform}/{Title}" disabled={!useDestinationPattern} style={{ opacity: useDestinationPattern ? 1 : 0.5 }} />
            <small style={{ display: 'block', marginTop: '5px', color: 'var(--ctp-overlay0)' }}>
              {t('destinationPatternExample') || 'Example: {Platform}/{Title} → switch/Zelda Tears of the Kingdom'}
            </small>
          </div>

          <div className="form-group" style={{ marginTop: '15px', padding: '15px', background: 'var(--surface-1)', borderRadius: '8px', border: '1px solid var(--border)' }}>
            <label htmlFor="folder-naming-mode" style={{ fontWeight: 600 }}>
              {t('folderNamingMode') || 'Folder Naming Mode'}
            </label>
            <p style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)', marginBottom: '10px' }}>
              {t('folderNamingModeDesc') || 'Controls platform folder names for compatibility with retro gaming frontends. Native uses RetroArr defaults. RetroBat and Batocera modes rename platform folders to match their expected conventions.'}
            </p>
            <select
              id="folder-naming-mode"
              value={folderNamingMode}
              onChange={(e) => { setFolderNamingMode(e.target.value); saveMediaConfig({ folderNamingMode: e.target.value }); }}
              style={{ width: '100%', padding: '8px', borderRadius: '6px', background: 'var(--surface-0)', color: 'var(--text)', border: '1px solid var(--border)' }}
            >
              <option value="native">{t('folderNamingNative') || 'Native (RetroArr)'}</option>
              <option value="retrobat">{t('folderNamingRetroBat') || 'RetroBat Compatible'}</option>
              <option value="batocera">{t('folderNamingBatocera') || 'Batocera Compatible'}</option>
            </select>
          </div>

          <div className="form-group" style={{ marginTop: '20px', borderTop: '1px solid #444', paddingTop: '15px' }}>
            <div className="section-header-with-logo" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
              <h4>Folder permissions</h4>
              <button
                onClick={loadPermissions}
                disabled={permissionsLoading}
                className="btn-secondary"
                style={{ padding: '6px 12px', fontSize: '0.85em' }}
              >
                <FontAwesomeIcon icon={faSync} spin={permissionsLoading} /> {permissionsLoading ? 'Checking…' : 'Recheck'}
              </button>
            </div>
            <p className="settings-description-sm" style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)' }}>
              Verifies that the configured folders exist and the RetroArr process can read &amp; write them.
              Useful when running with PUID/PGID under Docker (linuxserver-style images).
            </p>
            {permissionsReport && (
              <div style={{ marginTop: '10px' }}>
                <div style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)', marginBottom: '8px' }}>
                  Process UID={permissionsReport.processUid ?? 'n/a'}, GID={permissionsReport.processGid ?? 'n/a'}
                  {permissionsReport.puidEnv && <> · PUID env={permissionsReport.puidEnv}</>}
                  {permissionsReport.pgidEnv && <> · PGID env={permissionsReport.pgidEnv}</>}
                </div>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85em' }}>
                  <thead>
                    <tr style={{ textAlign: 'left', borderBottom: '1px solid var(--border)' }}>
                      <th style={{ padding: '6px 8px' }}>Setting</th>
                      <th style={{ padding: '6px 8px' }}>Path</th>
                      <th style={{ padding: '6px 8px' }}>Exists</th>
                      <th style={{ padding: '6px 8px' }}>Read</th>
                      <th style={{ padding: '6px 8px' }}>Write</th>
                      <th style={{ padding: '6px 8px' }}>Hint</th>
                    </tr>
                  </thead>
                  <tbody>
                    {permissionsReport.checks.map(c => {
                      const ok = c.exists && c.readable && c.writable;
                      const dot = (v: boolean) => <span style={{ color: v ? 'var(--ctp-green)' : 'var(--ctp-red)' }}>{v ? '✓' : '✗'}</span>;
                      return (
                        <tr key={c.key} style={{ borderBottom: '1px solid var(--surface-1)' }}>
                          <td style={{ padding: '6px 8px', fontWeight: 600 }}>{c.key}</td>
                          <td style={{ padding: '6px 8px' }}><code>{c.path || '-'}</code></td>
                          <td style={{ padding: '6px 8px' }}>{dot(c.exists)}</td>
                          <td style={{ padding: '6px 8px' }}>{dot(c.readable)}</td>
                          <td style={{ padding: '6px 8px' }}>{dot(c.writable)}</td>
                          <td style={{ padding: '6px 8px', color: ok ? 'var(--ctp-overlay0)' : 'var(--ctp-peach)' }}>
                            {c.hint || (ok ? 'OK' : '-')}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div className="form-group" style={{ marginTop: '20px', borderTop: '1px solid #444', paddingTop: '15px' }}>
            <div className="section-header-with-logo">
              <h4>{t('biosTitle') || 'BIOS files'}</h4>
            </div>
            <p className="settings-description-sm" style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)' }}>
              {t('biosPathDesc') || 'Drop core-specific BIOS files (e.g. scph1001.bin for PS1, saturn_bios.bin) into this folder. RetroArr serves them to EmulatorJS when a game requires them.'}
            </p>
            <label htmlFor="bios-path">{t('biosPath') || 'BIOS folder'}</label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <input
                id="bios-path"
                type="text"
                value={biosPath}
                onChange={(e) => setBiosPath(e.target.value)}
                onBlur={() => saveMediaConfig({ biosPath })}
                placeholder="/app/config/bios"
                style={{ flex: 1 }}
              />
              <button type="button" className="btn-secondary" onClick={() => { setActiveFolderField('bios'); setShowFolderExplorer(true); }} title={t('selectFolder')}>
                <FontAwesomeIcon icon={faFolderOpen} />
              </button>
            </div>
            {biosFiles.length > 0 && (
              <ul style={{ marginTop: '10px', fontSize: '0.85em', columns: 2 }}>
                {biosFiles.map((f) => (
                  <li key={f.filename} style={{ color: f.present ? 'var(--success, #4ade80)' : 'var(--muted, #888)' }}>
                    {f.present ? '✓' : '○'} {f.filename}
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div className="form-group" style={{ marginTop: '20px', borderTop: '1px solid #444', paddingTop: '15px' }}>
            <div className="section-header-with-logo">
              <h4>{t('trashTitle') || 'Trash'}</h4>
            </div>
            <p className="settings-description-sm" style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)' }}>
              {t('trashPathDesc') || 'Deleted game files are moved here first so you can restore them. Set retention to 0 to keep entries until you empty the trash manually.'}
            </p>
            <label htmlFor="trash-path">{t('trashPath') || 'Trash folder'}</label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <input
                id="trash-path"
                type="text"
                value={trashPath}
                onChange={(e) => setTrashPath(e.target.value)}
                onBlur={() => saveMediaConfig({ trashPath })}
                placeholder="/app/config/trash"
                style={{ flex: 1 }}
              />
            </div>
            <label htmlFor="trash-retention" style={{ marginTop: '10px', display: 'block' }}>
              {t('trashRetentionLabel') || 'Retention (days, 0 = manual only)'}
            </label>
            <input
              id="trash-retention"
              type="number"
              min={0}
              max={3650}
              value={trashRetentionDays}
              onChange={(e) => setTrashRetentionDays(parseInt(e.target.value, 10) || 0)}
              onBlur={() => saveMediaConfig({ trashRetentionDays })}
              style={{ width: 140 }}
            />

            <label htmlFor="missing-retention" style={{ marginTop: '14px', display: 'block' }}>
              {t('missingRetentionLabel') || 'Missing-flag retention (days, 0 = keep forever)'}
            </label>
            <p className="settings-description-sm" style={{ fontSize: '0.8em', color: 'var(--ctp-subtext0)', margin: '2px 0 6px' }}>
              {t('missingRetentionDesc') || 'How long a game stays flagged as Missing before a full scan purges it from the DB.'}
            </p>
            <input
              id="missing-retention"
              type="number"
              min={0}
              max={3650}
              value={missingRetentionDays}
              onChange={(e) => setMissingRetentionDays(parseInt(e.target.value, 10) || 0)}
              onBlur={() => saveMediaConfig({ missingRetentionDays })}
              style={{ width: 140 }}
            />
          </div>

          <div className="form-group" style={{ marginTop: '20px', borderTop: '1px solid #444', paddingTop: '15px' }}>
            <div className="section-header-with-logo">
              <h4>{t('wineIntegration')}</h4>
            </div>
            <p className="settings-description-sm" style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)' }}>{t('winePrefixPathDesc')}</p>
            <label htmlFor="wine-path">{t('winePrefixPath')}</label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <input id="wine-path" type="text" value={winePrefixPath} onChange={(e) => setWinePrefixPath(e.target.value)} onBlur={() => saveMediaConfig({ winePrefixPath })} placeholder="/Users/name/Library/Containers/com.isaacmarovitz.Whisky/Bottles/..." style={{ flex: 1 }} />
              <button type="button" className="btn-secondary" onClick={() => handleScanNow(winePrefixPath)} disabled={scanning || !winePrefixPath} title={t('scanNow')}>
                <FontAwesomeIcon icon={faSync} spin={scanning} />
              </button>
              <button type="button" className="btn-secondary" onClick={() => { setActiveFolderField('wine'); setShowFolderExplorer(true); }} title={t('selectFolder')}>
                <FontAwesomeIcon icon={faFolderOpen} />
              </button>
            </div>
          </div>
        </form>
      </div>

      <div className="settings-section" id="post-download">
        <div className="section-header-with-logo">
          <h3>{t('postDownloadTitle')}</h3>
        </div>
        <p className="settings-description">{t('postDownloadDesc')}</p>
        <form onSubmit={handleSavePostDownload}>
          <div className="form-group checkbox-group">
            <label htmlFor="enable-auto-move">
              <input type="checkbox" id="enable-auto-move" checked={postDownloadSettings.enableAutoMove} onChange={(e) => setPostDownloadSettings({ ...postDownloadSettings, enableAutoMove: e.target.checked })} />
              {t('enableAutoMove')}
            </label>
          </div>
          <div className="form-group checkbox-group">
            <label htmlFor="enable-auto-extract">
              <input type="checkbox" id="enable-auto-extract" checked={postDownloadSettings.enableAutoExtract} onChange={(e) => setPostDownloadSettings({ ...postDownloadSettings, enableAutoExtract: e.target.checked })} />
              {t('enableAutoExtract')}
            </label>
          </div>
          <div className="form-group checkbox-group">
            <label htmlFor="enable-deep-clean">
              <input type="checkbox" id="enable-deep-clean" checked={postDownloadSettings.enableDeepClean} onChange={(e) => setPostDownloadSettings({ ...postDownloadSettings, enableDeepClean: e.target.checked })} />
              {t('enableDeepClean')}
            </label>
          </div>
          <div className="form-group">
            <label htmlFor="monitor-interval">{t('monitorInterval')}</label>
            <input type="number" id="monitor-interval" value={postDownloadSettings.monitorIntervalSeconds} onChange={(e) => setPostDownloadSettings({ ...postDownloadSettings, monitorIntervalSeconds: parseInt(e.target.value) || 60 })} />
          </div>
          <div className="form-group">
            <label htmlFor="unwanted-extensions">{t('unwantedExtensions')}</label>
            <input type="text" id="unwanted-extensions" value={postDownloadSettings.unwantedExtensions?.join(', ') || ''} onChange={(e) => setPostDownloadSettings({ ...postDownloadSettings, unwantedExtensions: e.target.value.split(',').map(s => s.trim()) })} placeholder=".txt, .nfo, .url" />
          </div>
          <div className="button-group">
            <button type="submit" className="btn-primary">{t('savePostDownload')}</button>
          </div>
        </form>
      </div>

      {showFolderExplorer && (
        <FolderExplorerModal
          initialPath={
            activeFolderField === 'media' ? folderPath :
            activeFolderField === 'download' ? downloadPath :
            activeFolderField === 'destination' ? destinationPath :
            activeFolderField === 'bios' ? biosPath :
            winePrefixPath
          }
          onSelect={(path) => {
            if (activeFolderField === 'media') { setFolderPath(path); saveMediaConfig({ folderPath: path }); }
            else if (activeFolderField === 'download') { setDownloadPath(path); saveMediaConfig({ downloadPath: path }); }
            else if (activeFolderField === 'destination') { setDestinationPath(path); saveMediaConfig({ destinationPath: path }); }
            else if (activeFolderField === 'bios') { setBiosPath(path); saveMediaConfig({ biosPath: path }); }
            else if (activeFolderField === 'wine') { setWinePrefixPath(path); saveMediaConfig({ winePrefixPath: path }); }
            setShowFolderExplorer(false);
          }}
          onClose={() => setShowFolderExplorer(false)}
          language={language}
        />
      )}
    </>
  );
};

export default MediaTab;
