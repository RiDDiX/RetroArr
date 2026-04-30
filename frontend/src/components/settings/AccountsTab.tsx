import React, { useState, useEffect, useRef } from 'react';
import apiClient, { getErrorMessage, SteamSyncStatus, ProtonDbRefreshStatus } from '../../api/client';
import steamLogo from '../../assets/steam_logo.png';

interface AccountsTabProps {
  language: string;
  t: (key: string) => string;
}

const AccountsTab: React.FC<AccountsTabProps> = ({ t }) => {
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

  // ProtonDB
  const [protonRefreshing, setProtonRefreshing] = useState(false);
  const [protonRefreshStatus, setProtonRefreshStatus] = useState<ProtonDbRefreshStatus | null>(null);
  const [protonRefreshResult, setProtonRefreshResult] = useState<{ success: boolean; message: string } | null>(null);
  const protonPollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // GOG
  const [gogAuthCode, setGogAuthCode] = useState('');
  const [gogIsAuthenticated, setGogIsAuthenticated] = useState(false);
  const [gogUsername, setGogUsername] = useState('');
  const [gogSyncing, setGogSyncing] = useState(false);
  const [gogAuthenticating, setGogAuthenticating] = useState(false);
  const [gogSyncResult, setGogSyncResult] = useState<{ success: boolean; message: string } | null>(null);

  // Epic Games
  const [epicAuthCode, setEpicAuthCode] = useState('');
  const [epicIsAuthenticated, setEpicIsAuthenticated] = useState(false);
  const [epicDisplayName, setEpicDisplayName] = useState('');
  const [epicSyncing, setEpicSyncing] = useState(false);
  const [epicAuthenticating, setEpicAuthenticating] = useState(false);
  const [epicSyncResult, setEpicSyncResult] = useState<{ success: boolean; message: string } | null>(null);

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const steamRes = await apiClient.get('/settings/steam');
      setSteamId(steamRes.data.steamId || '');
      setSteamConfigured(steamRes.data.isConfigured === true);

      try {
        const gogRes = await apiClient.get('/gog/settings');
        setGogIsAuthenticated(gogRes.data.isAuthenticated);
        setGogUsername(gogRes.data.username || '');
      } catch { /* GOG not available */ }

      try {
        const epicRes = await apiClient.get('/epic/settings');
        setEpicIsAuthenticated(epicRes.data.isAuthenticated === true);
        setEpicDisplayName(epicRes.data.displayName || epicRes.data.accountId || '');
      } catch { /* Epic not available */ }
    } catch (error) {
      console.error('Error loading account settings:', error);
    }
  };

  // Epic handlers
  const handleEpicLogin = async () => {
    try {
      const res = await apiClient.get('/epic/auth/url');
      window.open(res.data.url, '_blank', 'width=900,height=800');
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  const handleEpicAuthCode = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!epicAuthCode.trim()) { alert('Enter the authorization code or paste the JSON blob from the Epic redirect page'); return; }
    setEpicAuthenticating(true);
    setEpicSyncResult(null);
    try {
      const res = await apiClient.post('/epic/auth/code', { code: epicAuthCode.trim() });
      if (res.data.success) {
        setEpicIsAuthenticated(true);
        setEpicDisplayName(res.data.displayName || res.data.accountId || '');
        setEpicAuthCode('');
        setEpicSyncResult({ success: true, message: res.data.message || 'Connected to Epic Games' });
      } else {
        setEpicSyncResult({ success: false, message: res.data.message || 'Authentication failed' });
      }
    } catch (error: unknown) {
      setEpicSyncResult({ success: false, message: getErrorMessage(error) });
    } finally {
      setEpicAuthenticating(false);
    }
  };

  const handleSyncEpic = async () => {
    setEpicSyncing(true);
    setEpicSyncResult(null);
    try {
      // catalog enrichment can take minutes for large libraries
      const res = await apiClient.post('/epic/sync', null, { timeout: 600000 });
      if (res.data.success) {
        const { added = 0, skipped = 0, failed = 0 } = res.data;
        setEpicSyncResult({
          success: true,
          message: `Epic sync done: ${added} added, ${skipped} skipped${failed > 0 ? `, ${failed} failed` : ''}`
        });
      } else {
        setEpicSyncResult({ success: false, message: res.data.message || 'Sync failed' });
      }
    } catch (error: unknown) {
      setEpicSyncResult({ success: false, message: `${t('error')}: ${getErrorMessage(error)}` });
    } finally {
      setEpicSyncing(false);
    }
  };

  const handleDisconnectEpic = async () => {
    if (!window.confirm(t('disconnectConfirm'))) return;
    try {
      await apiClient.delete('/epic/settings');
      setEpicIsAuthenticated(false);
      setEpicDisplayName('');
      setEpicAuthCode('');
    } catch (error: unknown) {
      console.error('Error disconnecting Epic:', error);
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
            : `✓ Sync complete - ${status.added} added, ${status.linked} linked, ${status.skipped} skipped${status.failed > 0 ? `, ${status.failed} failed` : ''}`;
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

  // ProtonDB
  const stopProtonPolling = () => {
    if (protonPollRef.current) {
      clearInterval(protonPollRef.current);
      protonPollRef.current = null;
    }
  };

  const startProtonPolling = () => {
    stopProtonPolling();
    protonPollRef.current = setInterval(async () => {
      try {
        const res = await apiClient.get<ProtonDbRefreshStatus>('/protondb/refresh/status');
        const status = res.data;
        setProtonRefreshStatus(status);
        if (!status.isRefreshing) {
          stopProtonPolling();
          setProtonRefreshing(false);
          const msg = status.error
            ? `✗ Error: ${status.error}`
            : `✓ ProtonDB refresh complete - ${status.updated} updated, ${status.skipped} skipped`;
          setProtonRefreshResult({ success: !status.error, message: msg });
        }
      } catch {
        stopProtonPolling();
        setProtonRefreshing(false);
      }
    }, 1500);
  };

  const handleRefreshProtonDb = async () => {
    setProtonRefreshing(true);
    setProtonRefreshResult(null);
    setProtonRefreshStatus(null);
    try {
      await apiClient.post('/protondb/refresh');
      startProtonPolling();
    } catch (error: unknown) {
      setProtonRefreshResult({ success: false, message: `✗ ${t('error')}: ${getErrorMessage(error)}` });
      setProtonRefreshing(false);
    }
  };

  const handleCancelProtonRefresh = async () => {
    try {
      await apiClient.post('/protondb/refresh/cancel');
    } catch { /* ignore */ }
  };

  useEffect(() => {
    // resume polling if a sync/refresh is already running on the backend
    apiClient.get<SteamSyncStatus>('/settings/steam/sync/status').then(res => {
      const status = res.data;
      if (status.isSyncing) {
        setSteamSyncing(true);
        setSteamSyncStatus(status);
        startSteamSyncPolling();
      }
    }).catch(() => { /* ignore */ });

    apiClient.get<ProtonDbRefreshStatus>('/protondb/refresh/status').then(res => {
      const status = res.data;
      if (status.isRefreshing) {
        setProtonRefreshing(true);
        setProtonRefreshStatus(status);
        startProtonPolling();
      }
    }).catch(() => { /* ignore */ });

    return () => {
      stopSteamSyncPolling();
      stopProtonPolling();
    };
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
    if (!gogAuthCode.trim()) { alert('Enter the authorization code'); return; }
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

  return (
    <>
      <div className="settings-section" id="accounts">
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

        <div style={{ marginTop: '20px', paddingTop: '16px', borderTop: '1px solid var(--ctp-surface1)' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '6px' }}>
            <span style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--ctp-text)' }}>ProtonDB</span>
            <span style={{ fontSize: '0.8rem', color: 'var(--ctp-subtext0)' }}>Linux / Steam Deck Compatibility</span>
          </div>
          <p style={{ fontSize: '0.85rem', color: 'var(--ctp-subtext1)', marginBottom: '10px' }}>
            Fetch ProtonDB compatibility ratings for all Steam games in your library. Shows Platinum, Gold, Silver, Bronze, or Borked status.
          </p>
          <div className="button-group">
            <button type="button" className="btn-secondary" onClick={handleRefreshProtonDb} disabled={protonRefreshing || !steamConfigured}>
              {protonRefreshing ? 'Refreshing...' : 'Refresh ProtonDB Ratings'}
            </button>
            {protonRefreshing && (
              <button type="button" className="btn-delete" onClick={handleCancelProtonRefresh} style={{ marginLeft: '6px' }}>
                ✕ Cancel
              </button>
            )}
          </div>
          {protonRefreshing && protonRefreshStatus && protonRefreshStatus.total > 0 && (
            <div style={{ marginTop: '10px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.85rem', marginBottom: '4px', color: 'var(--ctp-subtext0)' }}>
                <span>{protonRefreshStatus.currentGame || '...'}</span>
                <span>{protonRefreshStatus.progress}/{protonRefreshStatus.total}</span>
              </div>
              <div style={{ width: '100%', height: '8px', backgroundColor: 'var(--ctp-surface0)', borderRadius: '4px', overflow: 'hidden' }}>
                <div style={{ width: `${(protonRefreshStatus.progress / protonRefreshStatus.total) * 100}%`, height: '100%', backgroundColor: 'var(--ctp-green)', borderRadius: '4px', transition: 'width 0.3s ease' }} />
              </div>
              <div style={{ display: 'flex', gap: '12px', fontSize: '0.8rem', marginTop: '4px', color: 'var(--ctp-subtext1)' }}>
                <span>✓ {protonRefreshStatus.updated} updated</span>
                <span>⏭ {protonRefreshStatus.skipped} skipped</span>
              </div>
            </div>
          )}
          {protonRefreshResult && (
            <div className={`test-result ${protonRefreshResult.success ? 'success' : 'error'}`}>{protonRefreshResult.message}</div>
          )}
        </div>
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
            <span style={{ fontSize: '1.5rem' }}>🎯</span> Epic Games
          </h3>
        </div>
        <p className="settings-description">
          {t('epicDesc') || 'Connect your Epic Games account to sync your owned games into the library. Uses the same OAuth flow as Heroic and Legendary.'}
        </p>

        {epicIsAuthenticated ? (
          <>
            <div style={{ marginBottom: '15px', padding: '10px', background: 'rgba(166, 227, 161, 0.1)', borderRadius: '8px', border: '1px solid rgba(166, 227, 161, 0.3)' }}>
              <span style={{ color: 'var(--ctp-green)' }}>✓ {t('epicConnected') || 'Connected to Epic Games'}</span>
              {epicDisplayName && <span style={{ marginLeft: '10px' }}>({epicDisplayName})</span>}
            </div>
            <div className="button-group">
              <button type="button" className="btn-secondary" onClick={handleSyncEpic} disabled={epicSyncing}>
                {epicSyncing ? t('syncing') : t('syncLibrary')}
              </button>
              <button type="button" className="btn-delete" onClick={handleDisconnectEpic}>
                {t('disconnect')}
              </button>
            </div>
          </>
        ) : (
          <>
            <div style={{ marginBottom: '15px' }}>
              <p style={{ marginBottom: '10px', color: 'var(--ctp-text)' }}>1. Click "Login with Epic Games" to open the Epic login page.</p>
              <p style={{ marginBottom: '10px', color: 'var(--ctp-text)' }}>2. Sign in with your Epic account.</p>
              <p style={{ marginBottom: '10px', color: 'var(--ctp-text)' }}>3. After login a JSON page appears, copy the entire JSON or just the authorizationCode value and paste it below.</p>
              <div style={{ backgroundColor: 'var(--ctp-base)', padding: '8px 12px', borderRadius: '6px', fontSize: '0.75rem', fontFamily: 'monospace', color: 'var(--ctp-blue)', marginTop: '8px', wordBreak: 'break-all' }}>
                {'{"redirectUrl":"...","authorizationCode":"'}<span style={{ color: 'var(--ctp-green)' }}>XXXXX...</span>{'"}'}
              </div>
            </div>
            <button type="button" className="btn-primary" onClick={handleEpicLogin} style={{ marginBottom: '15px' }}>
              🔐 {t('epicLoginButton') || 'Login with Epic Games'}
            </button>
            <form onSubmit={handleEpicAuthCode}>
              <div className="form-group">
                <label htmlFor="epic-auth-code">{t('epicAuthCodeOrJson') || 'JSON or authorization code'}</label>
                <input
                  type="text"
                  id="epic-auth-code"
                  placeholder={t('epicAuthCodePlaceholder') || 'Paste the JSON or just the code here'}
                  value={epicAuthCode}
                  onChange={(e) => setEpicAuthCode(e.target.value)}
                />
                <small style={{ color: 'var(--ctp-subtext0)' }}>
                  {t('epicAuthCodeHelp') || 'Codes expire fast, paste it right after login.'}
                </small>
              </div>
              <button type="submit" className="btn-primary" disabled={epicAuthenticating || !epicAuthCode.trim()}>
                {epicAuthenticating ? t('authenticating') || 'Authenticating...' : t('epicSubmitCode') || 'Submit Code'}
              </button>
            </form>
          </>
        )}

        {epicSyncResult && (
          <div className={`test-result ${epicSyncResult.success ? 'success' : 'error'}`} style={{ marginTop: '15px' }}>{epicSyncResult.message}</div>
        )}
      </div>
    </>
  );
};

export default AccountsTab;
