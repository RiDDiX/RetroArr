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
    } catch (error) {
      console.error('Error loading metadata provider settings:', error);
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
    </>
  );
};

export default MetadataProvidersTab;
