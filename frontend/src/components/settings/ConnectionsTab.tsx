import React, { useState, useEffect, useRef } from 'react';
import apiClient, { getErrorMessage, SteamSyncStatus } from '../../api/client';
import igdbLogo from '../../assets/igdb_logo.png';
import steamLogo from '../../assets/steam_logo.png';

interface ConnectionsTabProps {
  language: string;
  t: (key: string) => string;
}

const ConnectionsTab: React.FC<ConnectionsTabProps> = ({ t }) => {
  // Steam
  const [steamApiKey, setSteamApiKey] = useState('');
  const [steamId, setSteamId] = useState('');
  const [steamConfigured, setSteamConfigured] = useState(false);
  const [steamTesting, setSteamTesting] = useState(false);
  const [steamTestResult, setSteamTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [steamSyncing, setSteamSyncing] = useState(false);
  const [steamSyncResult, setSteamSyncResult] = useState<{ success: boolean; message: string } | null>(null);
  const [steamSyncStatus, setSteamSyncStatus] = useState<SteamSyncStatus | null>(null);
  const steamSyncPollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // IGDB
  const [igdbClientId, setIgdbClientId] = useState('');
  const [igdbClientSecret, setIgdbClientSecret] = useState('');
  const [igdbConfigured, setIgdbConfigured] = useState(false);

  // GOG
  const [gogAuthCode, setGogAuthCode] = useState('');
  const [gogIsAuthenticated, setGogIsAuthenticated] = useState(false);
  const [gogUsername, setGogUsername] = useState('');
  const [gogSyncing, setGogSyncing] = useState(false);
  const [gogAuthenticating, setGogAuthenticating] = useState(false);
  const [gogSyncResult, setGogSyncResult] = useState<{ success: boolean; message: string } | null>(null);

  // ScreenScraper
  const [screenScraperUsername, setScreenScraperUsername] = useState('');
  const [screenScraperPassword, setScreenScraperPassword] = useState('');
  const [screenScraperEnabled, setScreenScraperEnabled] = useState(true);
  const [screenScraperConfigured, setScreenScraperConfigured] = useState(false);
  const [screenScraperTesting, setScreenScraperTesting] = useState(false);
  const [screenScraperTestResult, setScreenScraperTestResult] = useState<{ success: boolean; message: string } | null>(null);

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const [igdbRes, steamRes] = await Promise.all([
        apiClient.get('/settings/igdb'),
        apiClient.get('/settings/steam'),
      ]);
      setIgdbClientId(igdbRes.data.clientId || '');
      setIgdbConfigured(igdbRes.data.isConfigured === true);
      setSteamId(steamRes.data.steamId || '');
      setSteamConfigured(steamRes.data.isConfigured === true);

      try {
        const gogRes = await apiClient.get('/gog/settings');
        setGogIsAuthenticated(gogRes.data.isAuthenticated);
        setGogUsername(gogRes.data.username || '');
      } catch { /* GOG not available */ }

      try {
        const ssRes = await apiClient.get('/settings/screenscraper');
        setScreenScraperUsername(ssRes.data.username || '');
        setScreenScraperEnabled(ssRes.data.enabled !== false);
        setScreenScraperConfigured(ssRes.data.isConfigured === true);
      } catch { /* ScreenScraper not available */ }
    } catch (error) {
      console.error('Error loading connection settings:', error);
    }
  };

  // Steam handlers
  const handleSaveSteam = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/settings/steam', { apiKey: steamApiKey, steamId });
      alert(t('steamSettingsSaved'));
    } catch (error: unknown) {
      alert(`${t('error')} ${t('saveSteam')}: ${getErrorMessage(error)}`);
    }
  };

  const handleTestSteam = async () => {
    setSteamTesting(true);
    setSteamTestResult(null);
    try {
      const response = await apiClient.post('/settings/steam/test', { apiKey: steamApiKey, steamId });
      setSteamTestResult({ success: response.data.success, message: response.data.message });
    } catch (error: unknown) {
      setSteamTestResult({ success: false, message: `✗ ${t('error')}: ${getErrorMessage(error)}` });
    } finally {
      setSteamTesting(false);
    }
  };

  const stopSteamSyncPolling = () => {
    if (steamSyncPollRef.current) {
      clearInterval(steamSyncPollRef.current);
      steamSyncPollRef.current = null;
    }
  };

  const startSteamSyncPolling = () => {
    stopSteamSyncPolling();
    steamSyncPollRef.current = setInterval(async () => {
      try {
        const res = await apiClient.get<SteamSyncStatus>('/settings/steam/sync/status');
        const status = res.data;
        setSteamSyncStatus(status);
        if (!status.isSyncing) {
          stopSteamSyncPolling();
          setSteamSyncing(false);
          const msg = status.error
            ? `✗ Error: ${status.error}`
            : `✓ Sync complete — ${status.added} added, ${status.linked} linked, ${status.skipped} skipped${status.failed > 0 ? `, ${status.failed} failed` : ''}`;
          setSteamSyncResult({ success: !status.error, message: msg });
        }
      } catch {
        stopSteamSyncPolling();
        setSteamSyncing(false);
      }
    }, 1500);
  };

  const handleSyncSteam = async () => {
    setSteamSyncing(true);
    setSteamSyncResult(null);
    setSteamSyncStatus(null);
    try {
      await apiClient.post('/settings/steam/sync');
      startSteamSyncPolling();
    } catch (error: unknown) {
      setSteamSyncResult({ success: false, message: `✗ ${t('error')}: ${getErrorMessage(error)}` });
      setSteamSyncing(false);
    }
  };

  const handleCancelSteamSync = async () => {
    try {
      await apiClient.post('/settings/steam/sync/cancel');
    } catch { /* ignore */ }
  };

  useEffect(() => {
    return () => stopSteamSyncPolling();
  }, []);

  const handleDisconnectSteam = async () => {
    if (!window.confirm(t('disconnectConfirm'))) return;
    try {
      await apiClient.delete('/settings/steam');
      setSteamApiKey('');
      setSteamId('');
      setSteamConfigured(false);
      alert(t('steamSettingsSaved'));
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  // IGDB handlers
  const handleSaveMetadata = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/metadata/igdb', { clientId: igdbClientId, clientSecret: igdbClientSecret });
      alert(t('igdbSettingsSaved'));
    } catch (error: unknown) {
      alert(`${t('error')} ${t('saveMetadata')}: ${getErrorMessage(error)}`);
    }
  };

  const handleDisconnectIgdb = async () => {
    if (!window.confirm(t('disconnectConfirm'))) return;
    try {
      await apiClient.delete('/settings/igdb');
      setIgdbClientId('');
      setIgdbClientSecret('');
      setIgdbConfigured(false);
      alert(t('igdbSettingsSaved'));
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  // GOG handlers
  const handleGogLogin = async () => {
    try {
      const response = await apiClient.get('/gog/auth/url');
      window.open(response.data.url, '_blank', 'width=600,height=700');
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  const handleGogAuthCode = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!gogAuthCode.trim()) { alert('Please enter the authorization code'); return; }
    setGogAuthenticating(true);
    try {
      const response = await apiClient.post('/gog/auth/code', { code: gogAuthCode.trim() });
      if (response.data.success) {
        setGogIsAuthenticated(true);
        setGogAuthCode('');
        setGogSyncResult({ success: true, message: response.data.message });
      } else {
        setGogSyncResult({ success: false, message: response.data.message });
      }
    } catch (error: unknown) {
      setGogSyncResult({ success: false, message: getErrorMessage(error) });
    } finally {
      setGogAuthenticating(false);
    }
  };

  const handleSyncGog = async () => {
    setGogSyncing(true);
    setGogSyncResult(null);
    try {
      const response = await apiClient.post('/gog/sync');
      setGogSyncResult({ success: true, message: `Synced ${response.data.added} games (${response.data.skipped} skipped)` });
    } catch (error: unknown) {
      setGogSyncResult({ success: false, message: `✗ ${t('error')}: ${getErrorMessage(error)}` });
    } finally {
      setGogSyncing(false);
    }
  };

  const handleDisconnectGog = async () => {
    if (!window.confirm(t('disconnectConfirm'))) return;
    try {
      await apiClient.post('/gog/settings', { accessToken: null, refreshToken: null });
      setGogIsAuthenticated(false);
      setGogUsername('');
      setGogAuthCode('');
    } catch (error: unknown) {
      console.error('Error disconnecting GOG:', error);
    }
  };

  // ScreenScraper handlers
  const handleSaveScreenScraper = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/settings/screenscraper', { username: screenScraperUsername, password: screenScraperPassword, enabled: screenScraperEnabled });
      alert(t('screenScraperSaved') || 'ScreenScraper settings saved');
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  const handleTestScreenScraper = async () => {
    setScreenScraperTesting(true);
    setScreenScraperTestResult(null);
    try {
      const response = await apiClient.post('/settings/screenscraper/test', { username: screenScraperUsername, password: screenScraperPassword });
      setScreenScraperTestResult({ success: true, message: response.data.message || 'Connection successful' });
    } catch (error: unknown) {
      setScreenScraperTestResult({ success: false, message: getErrorMessage(error) });
    } finally {
      setScreenScraperTesting(false);
    }
  };

  const handleDisconnectScreenScraper = async () => {
    if (!window.confirm(t('disconnectConfirm'))) return;
    try {
      await apiClient.post('/settings/screenscraper', { username: '', password: '', enabled: false });
      setScreenScraperUsername('');
      setScreenScraperPassword('');
      setScreenScraperEnabled(false);
      setScreenScraperConfigured(false);
    } catch (error: unknown) {
      console.error('Error disconnecting ScreenScraper:', error);
    }
  };

  return (
    <>
      <div className="settings-section" id="connections">
        <div className="section-header-with-logo">
          <img src={steamLogo} alt="Steam" className="steam-logo" style={{ height: '60px' }} />
        </div>
        <p className="settings-description">{t('steamDesc')}</p>
        <form onSubmit={handleSaveSteam}>
          <div className="form-group">
            <label htmlFor="steam-api-key">{t('steamApiKey')}</label>
            <input type="password" id="steam-api-key" placeholder={steamConfigured ? '••••••••' : t('steamApiKey')} value={steamApiKey} onChange={(e) => setSteamApiKey(e.target.value)} />
            <small>{t('steamApiKeyHelp')} <a href="https://steamcommunity.com/dev/apikey" target="_blank" rel="noopener noreferrer">{t('steamDevPage')}</a></small>
          </div>
          <div className="form-group">
            <label htmlFor="steam-id">{t('steamId')}</label>
            <input type="text" id="steam-id" placeholder={t('steamId')} value={steamId} onChange={(e) => setSteamId(e.target.value)} />
          </div>
          <div className="button-group">
            <button type="button" className="btn-secondary" onClick={handleTestSteam} disabled={steamTesting || (!steamApiKey && !steamConfigured) || !steamId}>
              {steamTesting ? t('testing') : t('testConnection')}
            </button>
            <button type="button" className="btn-secondary" onClick={handleSyncSteam} disabled={steamSyncing || (!steamApiKey && !steamConfigured) || !steamId}>
              {steamSyncing ? t('syncing') : t('syncLibrary')}
            </button>
            {steamSyncing && (
              <button type="button" className="btn-delete" onClick={handleCancelSteamSync} style={{ marginLeft: '6px' }}>
                ✕ Cancel
              </button>
            )}
            <button type="submit" className="btn-primary">{t('saveSteam')}</button>
            {(steamApiKey || steamConfigured) && (
              <button type="button" className="btn-delete" onClick={handleDisconnectSteam} style={{ marginLeft: '10px' }}>
                {t('disconnect')}
              </button>
            )}
          </div>
          {steamTestResult && (
            <div className={`test-result ${steamTestResult.success ? 'success' : 'error'}`}>{steamTestResult.message}</div>
          )}
          {steamSyncing && steamSyncStatus && steamSyncStatus.total > 0 && (
            <div style={{ marginTop: '10px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', marginBottom: '4px', color: 'var(--ctp-subtext0)' }}>
                <span>{steamSyncStatus.currentGame || '...'}</span>
                <span>{steamSyncStatus.progress}/{steamSyncStatus.total}</span>
              </div>
              <div style={{ width: '100%', height: '8px', backgroundColor: 'var(--ctp-surface0)', borderRadius: '4px', overflow: 'hidden' }}>
                <div style={{ width: `${(steamSyncStatus.progress / steamSyncStatus.total) * 100}%`, height: '100%', backgroundColor: 'var(--ctp-blue)', borderRadius: '4px', transition: 'width 0.3s ease' }} />
              </div>
              <div style={{ display: 'flex', gap: '12px', fontSize: '0.8rem', marginTop: '4px', color: 'var(--ctp-subtext1)' }}>
                <span>✓ {steamSyncStatus.added} added</span>
                <span>🔗 {steamSyncStatus.linked} linked</span>
                <span>⏭ {steamSyncStatus.skipped} skipped</span>
                {steamSyncStatus.failed > 0 && <span style={{ color: 'var(--ctp-red)' }}>✗ {steamSyncStatus.failed} failed</span>}
              </div>
            </div>
          )}
          {steamSyncResult && (
            <div className={`test-result ${steamSyncResult.success ? 'success' : 'error'}`}>{steamSyncResult.message}</div>
          )}
        </form>
      </div>

      <div className="settings-section">
        <div className="section-header-with-logo">
          <img src={igdbLogo} alt="IGDB" className="igdb-logo" />
        </div>
        <p className="settings-description">{t('metadataDesc')}</p>
        <form onSubmit={handleSaveMetadata}>
          <div className="form-group">
            <label htmlFor="igdb-client-id">{t('igdbClientId')}</label>
            <input type="text" id="igdb-client-id" placeholder={t('igdbClientId')} value={igdbClientId} onChange={(e) => setIgdbClientId(e.target.value)} />
            <small>{t('twitchCredentialsHelp')} <a href="https://dev.twitch.tv/console/apps" target="_blank" rel="noopener noreferrer">{t('twitchConsole')}</a></small>
          </div>
          <div className="form-group">
            <label htmlFor="igdb-client-secret">{t('igdbClientSecret')}</label>
            <input type="password" id="igdb-client-secret" placeholder={igdbConfigured ? '••••••••' : t('igdbClientSecret')} value={igdbClientSecret} onChange={(e) => setIgdbClientSecret(e.target.value)} />
          </div>
          <div className="button-group">
            <button type="submit" className="btn-primary">{t('saveMetadata')}</button>
            {(igdbClientId || igdbConfigured) && (
              <button type="button" className="btn-delete" onClick={handleDisconnectIgdb} style={{ marginLeft: '10px' }}>
                {t('disconnect')}
              </button>
            )}
          </div>
        </form>
      </div>

      <div className="settings-section">
        <div className="section-header-with-logo">
          <h3 style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span style={{ fontSize: '1.5rem' }}>🎮</span> GOG Galaxy
          </h3>
        </div>
        <p className="settings-description">{t('gogDesc') || 'Connect your GOG Galaxy account to sync your game library.'}</p>

        {gogIsAuthenticated ? (
          <>
            <div style={{ marginBottom: '15px', padding: '10px', background: 'rgba(166, 227, 161, 0.1)', borderRadius: '8px', border: '1px solid rgba(166, 227, 161, 0.3)' }}>
              <span style={{ color: 'var(--ctp-green)' }}>✓ {t('gogConnected') || 'Connected to GOG Galaxy'}</span>
              {gogUsername && <span style={{ marginLeft: '10px' }}>({gogUsername})</span>}
            </div>
            <div className="button-group">
              <button type="button" className="btn-secondary" onClick={handleSyncGog} disabled={gogSyncing}>
                {gogSyncing ? t('syncing') : t('syncLibrary')}
              </button>
              <button type="button" className="btn-delete" onClick={handleDisconnectGog}>
                {t('disconnect')}
              </button>
            </div>
          </>
        ) : (
          <>
            <div style={{ marginBottom: '15px' }}>
              <p style={{ marginBottom: '10px', color: 'var(--ctp-text)' }}>{t('gogLoginStep1') || '1. Click "Login with GOG" to open the GOG login page'}</p>
              <p style={{ marginBottom: '10px', color: 'var(--ctp-text)' }}>{t('gogLoginStep2') || '2. Login with your GOG account'}</p>
              <p style={{ marginBottom: '10px', color: 'var(--ctp-text)' }}>{t('gogLoginStep3Url') || '3. Copy the ENTIRE URL from the address bar and paste it below'}</p>
              <div style={{ backgroundColor: 'var(--ctp-base)', padding: '8px 12px', borderRadius: '6px', fontSize: '0.75rem', fontFamily: 'monospace', color: 'var(--ctp-blue)', marginTop: '8px', wordBreak: 'break-all' }}>
                https://embed.gog.com/on_login_success?origin=client&code=<span style={{ color: 'var(--ctp-green)' }}>XXXXX...</span>
              </div>
            </div>
            <button type="button" className="btn-primary" onClick={handleGogLogin} style={{ marginBottom: '15px' }}>
              🔐 {t('gogLoginButton') || 'Login with GOG'}
            </button>
            <form onSubmit={handleGogAuthCode}>
              <div className="form-group">
                <label htmlFor="gog-auth-code">{t('gogAuthCodeOrUrl') || 'URL or Authorization Code'}</label>
                <input
                  type="text"
                  id="gog-auth-code"
                  placeholder={t('gogAuthCodePlaceholderUrl') || 'Paste the full URL or just the code here'}
                  value={gogAuthCode}
                  onChange={(e) => {
                    let value = e.target.value;
                    if (value.includes('code=')) {
                      try {
                        const url = new URL(value);
                        const code = url.searchParams.get('code');
                        if (code) value = code;
                      } catch {
                        const match = value.match(/code=([^&\s]+)/);
                        if (match) value = match[1];
                      }
                    }
                    setGogAuthCode(value);
                  }}
                />
                <small style={{ color: 'var(--ctp-subtext0)' }}>
                  {t('gogAuthCodeHelpUrl') || 'Paste the full URL - the code will be extracted automatically. Act fast, codes expire quickly!'}
                </small>
              </div>
              <button type="submit" className="btn-primary" disabled={gogAuthenticating || !gogAuthCode.trim()}>
                {gogAuthenticating ? t('authenticating') || 'Authenticating...' : t('gogSubmitCode') || 'Submit Code'}
              </button>
            </form>
          </>
        )}

        {gogSyncResult && (
          <div className={`test-result ${gogSyncResult.success ? 'success' : 'error'}`} style={{ marginTop: '15px' }}>{gogSyncResult.message}</div>
        )}
      </div>

      <div className="settings-section">
        <div className="section-header-with-logo">
          <h3 style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span style={{ fontSize: '1.5rem' }}>🕹️</span> ScreenScraper
          </h3>
        </div>
        <p className="settings-description">
          {t('screenScraperDesc') || 'ScreenScraper.fr provides metadata for retro games, arcade systems, and platforms not covered by IGDB. Create a free account at screenscraper.fr.'}
        </p>
        <form onSubmit={handleSaveScreenScraper}>
          <div className="form-group">
            <label htmlFor="screenscraper-username">{t('username') || 'Username'}</label>
            <input type="text" id="screenscraper-username" placeholder={t('username') || 'Username'} value={screenScraperUsername} onChange={(e) => setScreenScraperUsername(e.target.value)} />
            <small>
              {t('screenScraperHelp') || 'Register at '}
              <a href="https://www.screenscraper.fr/membreinscription.php" target="_blank" rel="noopener noreferrer" style={{ color: 'var(--ctp-blue)' }}>screenscraper.fr</a>
            </small>
          </div>
          <div className="form-group">
            <label htmlFor="screenscraper-password">{t('password') || 'Password'}</label>
            <input type="password" id="screenscraper-password" placeholder={screenScraperConfigured ? '••••••••' : (t('password') || 'Password')} value={screenScraperPassword} onChange={(e) => setScreenScraperPassword(e.target.value)} />
          </div>
          <div className="form-group">
            <label style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <input type="checkbox" checked={screenScraperEnabled} onChange={(e) => setScreenScraperEnabled(e.target.checked)} />
              {t('enabled') || 'Enabled'}
            </label>
          </div>
          <div className="button-group">
            <button type="button" className="btn-secondary" onClick={handleTestScreenScraper} disabled={screenScraperTesting || !screenScraperUsername || (!screenScraperPassword && !screenScraperConfigured)}>
              {screenScraperTesting ? t('testing') : t('testConnection')}
            </button>
            <button type="submit" className="btn-primary">{t('save')}</button>
            {(screenScraperUsername || screenScraperConfigured) && (
              <button type="button" className="btn-delete" onClick={handleDisconnectScreenScraper} style={{ marginLeft: '10px' }}>
                {t('disconnect')}
              </button>
            )}
          </div>
          {screenScraperTestResult && (
            <div className={`test-result ${screenScraperTestResult.success ? 'success' : 'error'}`}>{screenScraperTestResult.message}</div>
          )}
        </form>
      </div>
    </>
  );
};

export default ConnectionsTab;
