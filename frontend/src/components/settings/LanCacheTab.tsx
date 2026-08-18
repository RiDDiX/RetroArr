import React, { useEffect, useState } from 'react';
import { lancacheApi, getErrorMessage, type LanCacheSettings, type LanCacheStatus, type LanCacheReconcile } from '../../api/client';

interface Props {
  language: string;
  t: (key: string) => string;
}

const DEFAULTS: LanCacheSettings = {
  enabled: false,
  host: '',
  port: 80,
  prefillAllOwned: true,
  prefillRecent: false,
  prefillOs: 'windows',
};

const LanCacheTab: React.FC<Props> = ({ t }) => {
  const [settings, setSettings] = useState<LanCacheSettings>(DEFAULTS);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [status, setStatus] = useState<LanCacheStatus | null>(null);
  const [checking, setChecking] = useState(false);
  const [reconcile, setReconcile] = useState<LanCacheReconcile | null>(null);
  const [reconciling, setReconciling] = useState(false);

  useEffect(() => {
    lancacheApi.getSettings()
      .then(res => setSettings({ ...DEFAULTS, ...res.data }))
      .catch(e => setError(getErrorMessage(e, 'Failed to load LanCache settings')))
      .finally(() => setLoading(false));
  }, []);

  const save = async () => {
    setSaving(true); setError(null); setNotice(null);
    try {
      const res = await lancacheApi.saveSettings(settings);
      setSettings({ ...DEFAULTS, ...res.data });
      setNotice('LanCache settings saved.');
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to save LanCache settings'));
    } finally {
      setSaving(false);
    }
  };

  const checkStatus = async () => {
    setChecking(true); setError(null); setStatus(null);
    try {
      const res = await lancacheApi.getStatus();
      setStatus(res.data);
    } catch (e) {
      setError(getErrorMessage(e, 'Status check failed'));
    } finally {
      setChecking(false);
    }
  };

  const loadLibrary = async () => {
    setReconciling(true); setError(null); setReconcile(null);
    try {
      const res = await lancacheApi.reconcile();
      setReconcile(res.data);
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to load Steam library'));
    } finally {
      setReconciling(false);
    }
  };

  if (loading) return <div className="settings-section">{t('loading') || 'Loading...'}</div>;

  return (
    <div className="settings-section" id="lancache">
      <h2>LanCache</h2>
      <p className="settings-description">
        Point RetroArr at your <a href="https://lancache.net/" target="_blank" rel="noopener noreferrer">LanCache</a> server
        so its reachability can be checked and, once you connect Steam, its library reconciled and prefilled.
      </p>

      {error && <div className="alert alert-error">{error}</div>}
      {notice && <div className="alert alert-info">{notice}</div>}

      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={settings.enabled}
            onChange={e => setSettings(s => ({ ...s, enabled: e.target.checked }))}
            disabled={saving}
          />
          <span>Enable LanCache integration</span>
        </label>
      </div>

      <div className="form-row">
        <div className="form-group" style={{ flex: 2 }}>
          <label>LanCache host (IP or DNS name)</label>
          <input
            type="text"
            placeholder="e.g. 192.168.1.10 or lancache.lan"
            value={settings.host}
            onChange={e => setSettings(s => ({ ...s, host: e.target.value }))}
            disabled={saving}
          />
          <small className="settings-hint">The address of your LanCache server. Do not include http:// — just the host.</small>
        </div>
        <div className="form-group">
          <label>Port</label>
          <input
            type="number"
            min={1}
            max={65535}
            value={settings.port}
            onChange={e => setSettings(s => ({ ...s, port: Number(e.target.value) || 80 }))}
            disabled={saving}
          />
        </div>
      </div>

      <h3>Steam prefill (coming next)</h3>
      <p className="settings-hint">
        These control the upcoming SteamPrefill run. Prefill itself (login + warming the cache) lands in the next step;
        the options are saved now so they are ready.
      </p>
      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={settings.prefillAllOwned}
            onChange={e => setSettings(s => ({ ...s, prefillAllOwned: e.target.checked }))}
            disabled={saving}
          />
          <span>Prefill all owned games</span>
        </label>
      </div>
      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={settings.prefillRecent}
            onChange={e => setSettings(s => ({ ...s, prefillRecent: e.target.checked }))}
            disabled={saving}
          />
          <span>Also prefill games played in the last 2 weeks</span>
        </label>
      </div>
      <div className="form-group">
        <label>Prefill for operating system(s)</label>
        <select
          value={settings.prefillOs}
          onChange={e => setSettings(s => ({ ...s, prefillOs: e.target.value }))}
          disabled={saving}
        >
          <option value="windows">Windows</option>
          <option value="linux">Linux</option>
          <option value="macos">macOS</option>
          <option value="windows,linux">Windows + Linux</option>
          <option value="windows,linux,macos">All</option>
        </select>
      </div>

      <div className="form-row" style={{ gap: 8, marginTop: 8 }}>
        <button className="btn-primary" onClick={save} disabled={saving}>
          {saving ? (t('saving') || 'Saving...') : (t('save') || 'Save')}
        </button>
        <button className="btn-secondary" onClick={checkStatus} disabled={checking || !settings.host}>
          {checking ? 'Checking...' : 'Check status'}
        </button>
        <button className="btn-secondary" onClick={loadLibrary} disabled={reconciling}>
          {reconciling ? 'Loading...' : 'Load Steam library'}
        </button>
      </div>

      {status && (
        <div className={`alert ${status.reachable ? 'alert-info' : 'alert-error'}`} style={{ marginTop: 12 }}>
          {!status.configured && 'No LanCache host configured.'}
          {status.configured && status.reachable && (
            <>
              LanCache reachable at {status.host}:{status.port}
              {status.isLanCache
                ? ` — confirmed LanCache${status.processedBy ? ` (${status.processedBy})` : ''}.`
                : ' — responded, but no LanCache heartbeat header (is this really a LanCache?).'}
            </>
          )}
          {status.configured && !status.reachable && (
            <>Could not reach {status.host}:{status.port}{status.error ? ` — ${status.error}` : ''}.</>
          )}
        </div>
      )}

      {reconcile && (
        <div className="alert alert-info" style={{ marginTop: 12 }}>
          {!reconcile.steamConfigured && 'Steam is not connected. Add your Steam API key + ID under Accounts first.'}
          {reconcile.steamConfigured && (
            <>
              {reconcile.ownedCount} owned Steam game{reconcile.ownedCount === 1 ? '' : 's'} found.
              {reconcile.games.length > 0 && (
                <ul style={{ margin: '8px 0 0', paddingLeft: 18, maxHeight: 220, overflow: 'auto' }}>
                  {reconcile.games.slice(0, 50).map(g => (
                    <li key={g.appId}>{g.name}</li>
                  ))}
                </ul>
              )}
              {reconcile.games.length > 50 && (
                <small className="settings-hint">Showing first 50 of {reconcile.ownedCount}.</small>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
};

export default LanCacheTab;
