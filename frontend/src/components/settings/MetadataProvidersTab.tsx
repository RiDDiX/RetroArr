import React, { useState, useEffect } from 'react';
import apiClient, { getErrorMessage } from '../../api/client';
import igdbLogo from '../../assets/igdb_logo.png';

interface MetadataProvidersTabProps {
  language: string;
  t: (key: string) => string;
}

const MetadataProvidersTab: React.FC<MetadataProvidersTabProps> = ({ t }) => {
  // IGDB
  const [igdbClientId, setIgdbClientId] = useState('');
  const [igdbClientSecret, setIgdbClientSecret] = useState('');
  const [igdbConfigured, setIgdbConfigured] = useState(false);

  // ScreenScraper
  const [screenScraperUsername, setScreenScraperUsername] = useState('');
  const [screenScraperPassword, setScreenScraperPassword] = useState('');
  const [screenScraperEnabled, setScreenScraperEnabled] = useState(true);
  const [screenScraperConfigured, setScreenScraperConfigured] = useState(false);
  const [screenScraperTesting, setScreenScraperTesting] = useState(false);
  const [screenScraperTestResult, setScreenScraperTestResult] = useState<{ success: boolean; message: string } | null>(null);

  // TheGamesDB
  const [tgdbApiKey, setTgdbApiKey] = useState('');
  const [tgdbEnabled, setTgdbEnabled] = useState(true);
  const [tgdbConfigured, setTgdbConfigured] = useState(false);
  const [tgdbTesting, setTgdbTesting] = useState(false);
  const [tgdbTestResult, setTgdbTestResult] = useState<{ success: boolean; message: string } | null>(null);

  // SteamGridDB (image-only fallback)
  const [sgdbApiKey, setSgdbApiKey] = useState('');
  const [sgdbEnabled, setSgdbEnabled] = useState(true);
  const [sgdbConfigured, setSgdbConfigured] = useState(false);
  const [sgdbTesting, setSgdbTesting] = useState(false);
  const [sgdbTestResult, setSgdbTestResult] = useState<{ success: boolean; message: string } | null>(null);

  // Epic Store (anonymous, no API key)
  const [epicStoreEnabled, setEpicStoreEnabled] = useState(true);
  const [epicStoreLocale, setEpicStoreLocale] = useState('en-US');
  const [epicStoreCountry, setEpicStoreCountry] = useState('US');
  const [epicStoreTesting, setEpicStoreTesting] = useState(false);
  const [epicStoreTestResult, setEpicStoreTestResult] = useState<{ success: boolean; message: string } | null>(null);

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const igdbRes = await apiClient.get('/settings/igdb');
      setIgdbClientId(igdbRes.data.clientId || '');
      setIgdbConfigured(igdbRes.data.isConfigured === true);

      try {
        const ssRes = await apiClient.get('/settings/screenscraper');
        setScreenScraperUsername(ssRes.data.username || '');
        setScreenScraperEnabled(ssRes.data.enabled !== false);
        setScreenScraperConfigured(ssRes.data.isConfigured === true);
      } catch { /* ScreenScraper not available */ }

      try {
        const tgdbRes = await apiClient.get('/settings/thegamesdb');
        setTgdbEnabled(tgdbRes.data.enabled !== false);
        setTgdbConfigured(tgdbRes.data.isConfigured === true);
      } catch { /* TheGamesDB not available */ }

      try {
        const sgdbRes = await apiClient.get('/settings/steamgriddb');
        setSgdbEnabled(sgdbRes.data.enabled !== false);
        setSgdbConfigured(sgdbRes.data.isConfigured === true);
      } catch { /* SteamGridDB not available */ }

      try {
        const epicStoreRes = await apiClient.get('/settings/epicstore');
        setEpicStoreEnabled(epicStoreRes.data.enabled !== false);
        if (epicStoreRes.data.locale) setEpicStoreLocale(epicStoreRes.data.locale);
        if (epicStoreRes.data.country) setEpicStoreCountry(epicStoreRes.data.country);
      } catch { /* Epic Store not available */ }
    } catch (error) {
      console.error('Error loading metadata provider settings:', error);
    }
  };

  const handleSaveEpicStore = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/settings/epicstore', {
        enabled: epicStoreEnabled,
        locale: epicStoreLocale,
        country: epicStoreCountry
      });
      alert(t('epicStoreSettingsSaved') || 'Epic Store settings saved');
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  const handleTestEpicStore = async () => {
    setEpicStoreTesting(true);
    setEpicStoreTestResult(null);
    try {
      const response = await apiClient.post('/settings/epicstore/test');
      setEpicStoreTestResult({ success: response.data.success !== false, message: response.data.message || 'Connection successful' });
    } catch (error: unknown) {
      setEpicStoreTestResult({ success: false, message: getErrorMessage(error) });
    } finally {
      setEpicStoreTesting(false);
    }
  };

  const handleDisableEpicStore = async () => {
    if (!window.confirm(t('disconnectConfirm'))) return;
    try {
      await apiClient.delete('/settings/epicstore');
      setEpicStoreEnabled(false);
    } catch (error: unknown) {
      console.error('Error disabling Epic Store:', error);
    }
  };

  // SteamGridDB handlers
  const handleSaveSgdb = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/settings/steamgriddb', { apiKey: sgdbApiKey, enabled: sgdbEnabled });
      alert(t('sgdbSettingsSaved') || 'SteamGridDB settings saved');
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  const handleTestSgdb = async () => {
    setSgdbTesting(true);
    setSgdbTestResult(null);
    try {
      const response = await apiClient.post('/settings/steamgriddb/test', { apiKey: sgdbApiKey });
      setSgdbTestResult({ success: response.data.success !== false, message: response.data.message || 'Connection successful' });
    } catch (error: unknown) {
      setSgdbTestResult({ success: false, message: getErrorMessage(error) });
    } finally {
      setSgdbTesting(false);
    }
  };

  const handleDisconnectSgdb = async () => {
    if (!window.confirm(t('disconnectConfirm'))) return;
    try {
      await apiClient.delete('/settings/steamgriddb');
      setSgdbApiKey('');
      setSgdbEnabled(false);
      setSgdbConfigured(false);
    } catch (error: unknown) {
      console.error('Error disconnecting SteamGridDB:', error);
    }
  };

  // TheGamesDB handlers
  const handleSaveTgdb = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/settings/thegamesdb', { apiKey: tgdbApiKey, enabled: tgdbEnabled });
      alert(t('tgdbSettingsSaved') || 'TheGamesDB settings saved');
    } catch (error: unknown) {
      alert(`${t('error')}: ${getErrorMessage(error)}`);
    }
  };

  const handleTestTgdb = async () => {
    setTgdbTesting(true);
    setTgdbTestResult(null);
    try {
      const response = await apiClient.post('/settings/thegamesdb/test', { apiKey: tgdbApiKey });
      setTgdbTestResult({ success: response.data.success !== false, message: response.data.message || 'Connection successful' });
    } catch (error: unknown) {
      setTgdbTestResult({ success: false, message: getErrorMessage(error) });
    } finally {
      setTgdbTesting(false);
    }
  };

  const handleDisconnectTgdb = async () => {
    if (!window.confirm(t('disconnectConfirm'))) return;
    try {
      await apiClient.delete('/settings/thegamesdb');
      setTgdbApiKey('');
      setTgdbEnabled(false);
      setTgdbConfigured(false);
    } catch (error: unknown) {
      console.error('Error disconnecting TheGamesDB:', error);
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
      setScreenScraperTestResult({ success: response.data.success !== false, message: response.data.message || 'Connection successful' });
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
      <div className="settings-section" id="metadata">
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

      <div className="settings-section">
        <div className="section-header-with-logo">
          <h3 style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span style={{ fontSize: '1.5rem' }}>🎲</span> TheGamesDB
          </h3>
        </div>
        <p className="settings-description">
          {t('tgdbDesc') || 'TheGamesDB is a community-maintained database with rich metadata and box art. Used as fallback after IGDB and ScreenScraper.'}
        </p>
        <form onSubmit={handleSaveTgdb}>
          <div className="form-group">
            <label htmlFor="tgdb-api-key">{t('apiKey') || 'API Key'}</label>
            <input type="password" id="tgdb-api-key" placeholder={tgdbConfigured ? '••••••••' : (t('apiKey') || 'API Key')} value={tgdbApiKey} onChange={(e) => setTgdbApiKey(e.target.value)} />
            <small>
              {t('tgdbHelp') || 'Request a key on the '}
              <a href="https://forums.thegamesdb.net/" target="_blank" rel="noopener noreferrer" style={{ color: 'var(--ctp-blue)' }}>thegamesdb.net forum</a>
            </small>
          </div>
          <div className="form-group">
            <label style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <input type="checkbox" checked={tgdbEnabled} onChange={(e) => setTgdbEnabled(e.target.checked)} />
              {t('enabled') || 'Enabled'}
            </label>
          </div>
          <div className="button-group">
            <button type="button" className="btn-secondary" onClick={handleTestTgdb} disabled={tgdbTesting || (!tgdbApiKey && !tgdbConfigured)}>
              {tgdbTesting ? t('testing') : t('testConnection')}
            </button>
            <button type="submit" className="btn-primary">{t('save')}</button>
            {(tgdbApiKey || tgdbConfigured) && (
              <button type="button" className="btn-delete" onClick={handleDisconnectTgdb} style={{ marginLeft: '10px' }}>
                {t('disconnect')}
              </button>
            )}
          </div>
          {tgdbTestResult && (
            <div className={`test-result ${tgdbTestResult.success ? 'success' : 'error'}`}>{tgdbTestResult.message}</div>
          )}
        </form>
      </div>

      <div className="settings-section">
        <div className="section-header-with-logo">
          <h3 style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span style={{ fontSize: '1.5rem' }}>🖼️</span> SteamGridDB
          </h3>
        </div>
        <p className="settings-description">
          {t('sgdbDesc') || 'SteamGridDB provides community-made grids, heroes and logos. Used only to fill missing artwork after another provider already matched the game.'}
        </p>
        <form onSubmit={handleSaveSgdb}>
          <div className="form-group">
            <label htmlFor="sgdb-api-key">{t('apiKey') || 'API Key'}</label>
            <input type="password" id="sgdb-api-key" placeholder={sgdbConfigured ? '••••••••' : (t('apiKey') || 'API Key')} value={sgdbApiKey} onChange={(e) => setSgdbApiKey(e.target.value)} />
            <small>
              {t('sgdbHelp') || 'Generate a key in your '}
              <a href="https://www.steamgriddb.com/profile/preferences/api" target="_blank" rel="noopener noreferrer" style={{ color: 'var(--ctp-blue)' }}>SteamGridDB profile</a>
            </small>
          </div>
          <div className="form-group">
            <label style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <input type="checkbox" checked={sgdbEnabled} onChange={(e) => setSgdbEnabled(e.target.checked)} />
              {t('enabled') || 'Enabled'}
            </label>
          </div>
          <div className="button-group">
            <button type="button" className="btn-secondary" onClick={handleTestSgdb} disabled={sgdbTesting || (!sgdbApiKey && !sgdbConfigured)}>
              {sgdbTesting ? t('testing') : t('testConnection')}
            </button>
            <button type="submit" className="btn-primary">{t('save')}</button>
            {(sgdbApiKey || sgdbConfigured) && (
              <button type="button" className="btn-delete" onClick={handleDisconnectSgdb} style={{ marginLeft: '10px' }}>
                {t('disconnect')}
              </button>
            )}
          </div>
          {sgdbTestResult && (
            <div className={`test-result ${sgdbTestResult.success ? 'success' : 'error'}`}>{sgdbTestResult.message}</div>
          )}
        </form>
      </div>

      <div className="settings-section">
        <div className="section-header-with-logo">
          <h3 style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span style={{ fontSize: '1.5rem' }}>🎯</span> Epic Store
          </h3>
        </div>
        <p className="settings-description">
          {t('epicStoreDesc') || 'Anonymous Epic Store metadata via the public GraphQL API. No login required, used as final fallback after IGDB, ScreenScraper and TheGamesDB.'}
        </p>
        <form onSubmit={handleSaveEpicStore}>
          <div className="form-group">
            <label style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <input type="checkbox" checked={epicStoreEnabled} onChange={(e) => setEpicStoreEnabled(e.target.checked)} />
              {t('enabled') || 'Enabled'}
            </label>
          </div>
          <div style={{ display: 'flex', gap: '10px' }}>
            <div className="form-group" style={{ flex: 1 }}>
              <label htmlFor="epicstore-locale">Locale</label>
              <input type="text" id="epicstore-locale" value={epicStoreLocale} onChange={(e) => setEpicStoreLocale(e.target.value)} placeholder="en-US" />
            </div>
            <div className="form-group" style={{ flex: 1 }}>
              <label htmlFor="epicstore-country">Country</label>
              <input type="text" id="epicstore-country" value={epicStoreCountry} onChange={(e) => setEpicStoreCountry(e.target.value)} placeholder="US" />
            </div>
          </div>
          <div className="button-group">
            <button type="button" className="btn-secondary" onClick={handleTestEpicStore} disabled={epicStoreTesting}>
              {epicStoreTesting ? t('testing') : t('testConnection')}
            </button>
            <button type="submit" className="btn-primary">{t('save')}</button>
            {epicStoreEnabled && (
              <button type="button" className="btn-delete" onClick={handleDisableEpicStore} style={{ marginLeft: '10px' }}>
                {t('disable') || 'Disable'}
              </button>
            )}
          </div>
          {epicStoreTestResult && (
            <div className={`test-result ${epicStoreTestResult.success ? 'success' : 'error'}`}>{epicStoreTestResult.message}</div>
          )}
        </form>
      </div>
    </>
  );
};

export default MetadataProvidersTab;
