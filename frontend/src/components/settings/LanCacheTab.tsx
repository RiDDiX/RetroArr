import React, { useEffect, useRef, useState } from 'react';
import { lancacheApi, getErrorMessage, type LanCacheSettings, type LanCacheStatus, type LanCacheReconcile, type PrefillStatus } from '../../api/client';

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
  const [prefill, setPrefill] = useState<PrefillStatus | null>(null);
  const [starting, setStarting] = useState(false);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    lancacheApi.getSettings()
      .then(res => setSettings({ ...DEFAULTS, ...res.data }))
      .catch(e => setError(getErrorMessage(e, 'Failed to load LanCache settings')))
      .finally(() => setLoading(false));
    lancacheApi.getPrefillStatus().then(res => setPrefill(res.data)).catch(() => {});
    return () => { if (pollRef.current) clearInterval(pollRef.current); };
  }, []);

  // Poll prefill status while a run is in progress.
  useEffect(() => {
    if (prefill?.running && !pollRef.current) {
      pollRef.current = setInterval(async () => {
        try {
          const res = await lancacheApi.getPrefillStatus();
          setPrefill(res.data);
          if (!res.data.running && pollRef.current) { clearInterval(pollRef.current); pollRef.current = null; }
        } catch { /* keep polling */ }
      }, 3000);
    }
  }, [prefill?.running]);

  const runPrefill = async () => {
    setStarting(true); setError(null); setNotice(null);
    try {
      const res = await lancacheApi.runPrefill();
      setNotice(res.data.message);
      const st = await lancacheApi.getPrefillStatus();
      setPrefill(st.data);
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to start prefill'));
    } finally {
      setStarting(false);
    }
  };

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

      <h3>Steam prefill</h3>
      <p className="settings-hint">
        RetroArr orchestrates the bundled <a href="https://github.com/tpill90/steam-lancache-prefill" target="_blank" rel="noopener noreferrer">SteamPrefill</a> tool
        to warm your LanCache. A one-time interactive Steam login is required (it is separate from the Steam Web API key):
        run <code>docker exec -it retroarr /opt/steamprefill/SteamPrefill select-apps</code> once, then use the button below.
        Prefill only fills the cache if your network routes Steam CDN traffic through the LanCache.
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

      {prefill && (
        <div className="settings-section" style={{ marginTop: 16, padding: 0 }}>
          <div className="settings-hint" style={{ marginBottom: 8 }}>
            SteamPrefill: {prefill.available ? 'bundled' : 'not bundled'} ·{' '}
            {prefill.loggedIn ? 'logged in' : 'not logged in'} ·{' '}
            {prefill.prefilledCount} prefilled
            {prefill.lastRunUtc ? ` · last run ${new Date(prefill.lastRunUtc).toLocaleString()}${prefill.lastExitCode != null ? ` (exit ${prefill.lastExitCode})` : ''}` : ''}
          </div>
          <button
            className="btn-primary"
            onClick={runPrefill}
            disabled={starting || prefill.running || !prefill.available || !prefill.loggedIn}
          >
            {prefill.running ? 'Prefilling...' : starting ? 'Starting...' : 'Run prefill now'}
          </button>
          {!prefill.available && (
            <div className="alert alert-error" style={{ marginTop: 8 }}>
              SteamPrefill is not bundled in this image. Rebuild or pull the latest image.
            </div>
          )}
          {prefill.available && !prefill.loggedIn && (
            <div className="alert alert-info" style={{ marginTop: 8 }}>
              One-time login required: <code>docker exec -it retroarr /opt/steamprefill/SteamPrefill select-apps</code>
            </div>
          )}
          {prefill.recentLog.length > 0 && (
            <pre style={{ marginTop: 8, maxHeight: 220, overflow: 'auto', background: 'var(--ctp-mantle, #181825)', padding: 8, borderRadius: 6, fontSize: '0.78em' }}>
              {prefill.recentLog.slice(-60).join('\n')}
            </pre>
          )}
        </div>
      )}

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
              {reconcile.ownedCount} owned Steam game{reconcile.ownedCount === 1 ? '' : 's'} found
              {typeof reconcile.prefilledCount === 'number' ? `, ${reconcile.prefilledCount} already prefilled` : ''}.
              {reconcile.games.length > 0 && (
                <ul style={{ margin: '8px 0 0', paddingLeft: 18, maxHeight: 220, overflow: 'auto' }}>
                  {reconcile.games.slice(0, 50).map(g => (
                    <li key={g.appId}>{g.prefilled ? '✓ ' : ''}{g.name}</li>
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
