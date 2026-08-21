import React, { useEffect, useRef, useState } from 'react';
import { lancacheApi, getErrorMessage, type LanCacheSettings, type LanCacheStatus, type LanCacheReconcile, type PrefillSchedule, type PrefillProviderStatus, type SteamAppEntry } from '../../api/client';

interface Props {
  language: string;
  t: (key: string) => string;
}

// Sunday-first, matching DayOfWeek (0=Sunday) used by the backend scheduler.
const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

const DEFAULTS: LanCacheSettings = {
  enabled: false,
  host: '',
  port: 80,
  prefillAllOwned: false,
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
  const [providers, setProviders] = useState<PrefillProviderStatus[]>([]);
  const [startingId, setStartingId] = useState<string | null>(null);
  const [stoppingId, setStoppingId] = useState<string | null>(null);
  const [steamApps, setSteamAppsList] = useState<SteamAppEntry[] | null>(null);
  const [steamLoading, setSteamLoading] = useState(false);
  const [steamSaving, setSteamSaving] = useState(false);
  const [appSearch, setAppSearch] = useState('');
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());

  const loadSteamApps = async () => {
    setSteamLoading(true); setError(null); setNotice(null);
    try {
      const res = await lancacheApi.getSteamApps();
      if (!res.data.steamConfigured) {
        setError('Steam is not connected. Add your Steam API key + ID under Accounts first.');
        return;
      }
      setSteamAppsList(res.data.games);
      setSelectedIds(new Set(res.data.games.filter(g => g.selected).map(g => g.appId)));
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to load Steam games'));
    } finally {
      setSteamLoading(false);
    }
  };

  const toggleApp = (id: number) => setSelectedIds(prev => {
    const next = new Set(prev);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  const saveSelection = async () => {
    setSteamSaving(true); setError(null); setNotice(null);
    try {
      const res = await lancacheApi.setSteamApps([...selectedIds]);
      setNotice(`Saved ${res.data.selectedCount} selected game(s). Run the Steam prefill (with "Prefill ALL owned" off) to warm just these.`);
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to save selection'));
    } finally {
      setSteamSaving(false);
    }
  };

  const filteredApps = (steamApps ?? []).filter(g =>
    !appSearch || g.name.toLowerCase().includes(appSearch.toLowerCase()));
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const anyRunning = providers.some(p => p.running);

  useEffect(() => {
    lancacheApi.getSettings()
      .then(res => setSettings({ ...DEFAULTS, ...res.data }))
      .catch(e => setError(getErrorMessage(e, 'Failed to load LanCache settings')))
      .finally(() => setLoading(false));
    lancacheApi.getPrefillStatus().then(res => setProviders(res.data)).catch(() => {});
    return () => { if (pollRef.current) clearInterval(pollRef.current); };
  }, []);

  // Poll prefill status while any provider run is in progress.
  useEffect(() => {
    if (anyRunning && !pollRef.current) {
      pollRef.current = setInterval(async () => {
        try {
          const res = await lancacheApi.getPrefillStatus();
          setProviders(res.data);
          if (!res.data.some(p => p.running) && pollRef.current) { clearInterval(pollRef.current); pollRef.current = null; }
        } catch { /* keep polling */ }
      }, 3000);
    }
  }, [anyRunning]);

  const runPrefill = async (providerId: string) => {
    setStartingId(providerId); setError(null); setNotice(null);
    try {
      const res = await lancacheApi.runPrefill(providerId);
      setNotice(res.data.message);
      const st = await lancacheApi.getPrefillStatus();
      setProviders(st.data);
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to start prefill'));
    } finally {
      setStartingId(null);
    }
  };

  const stopPrefill = async (providerId: string) => {
    setStoppingId(providerId); setError(null); setNotice(null);
    try {
      const res = await lancacheApi.stopPrefill(providerId);
      setNotice(res.data.message);
      const st = await lancacheApi.getPrefillStatus();
      setProviders(st.data);
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to stop prefill'));
    } finally {
      setStoppingId(null);
    }
  };

  // Per-provider schedule helpers. Schedules live inside LanCacheSettings and are
  // persisted with the normal Save button.
  const scheduleFor = (providerId: string): PrefillSchedule =>
    settings.schedules?.[providerId] ?? { enabled: false, time: '04:00', days: [] };

  const updateSchedule = (providerId: string, patch: Partial<PrefillSchedule>) => {
    setSettings(s => ({
      ...s,
      schedules: { ...(s.schedules ?? {}), [providerId]: { ...scheduleFor(providerId), ...patch } },
    }));
  };

  const toggleDay = (providerId: string, day: number) => {
    const cur = scheduleFor(providerId);
    const days = cur.days.includes(day) ? cur.days.filter(d => d !== day) : [...cur.days, day].sort();
    updateSchedule(providerId, { days });
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

      <h3>Prefill</h3>
      <p className="settings-hint">
        RetroArr orchestrates tpill90's bundled prefill tools to warm your LanCache:{' '}
        <a href="https://github.com/tpill90/steam-lancache-prefill" target="_blank" rel="noopener noreferrer">Steam</a>,{' '}
        <a href="https://github.com/tpill90/battlenet-lancache-prefill" target="_blank" rel="noopener noreferrer">Battle.net</a> and{' '}
        <a href="https://github.com/tpill90/epic-lancache-prefill" target="_blank" rel="noopener noreferrer">Epic</a>.
        Where a login is required it is a one-time interactive step (separate from any Web API key); the exact command is shown per provider.
        Prefill only fills the cache if your network routes that store's CDN traffic through the LanCache.
      </p>
      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={settings.prefillAllOwned}
            onChange={e => setSettings(s => ({ ...s, prefillAllOwned: e.target.checked }))}
            disabled={saving}
          />
          <span>Prefill ALL owned games (ignores the selection below)</span>
        </label>
        <small className="settings-hint">
          Leave this off to prefill only the games you pick below (or via <code>select-apps</code>).
          Turn it on to warm your entire owned library.
        </small>
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

      <div className="settings-section" style={{ marginTop: 16, padding: 0 }}>
        <h3>Choose Steam games to prefill</h3>
        <p className="settings-hint">
          Pick which Steam games to warm. Saved to the same list SteamPrefill&apos;s
          <code>select-apps</code> uses, so both stay in sync. Titles you selected via
          <code>select-apps</code> that are not in your owned library (e.g. Steam
          <strong>Family</strong> shares) also appear here, tagged, so you can see and untick
          them. Adding new Family titles is done in <code>select-apps</code>.
        </p>
        <div className="form-row" style={{ gap: 8 }}>
          <button className="btn-secondary" onClick={loadSteamApps} disabled={steamLoading}>
            {steamLoading ? 'Loading...' : 'Load Steam games'}
          </button>
          {steamApps && (
            <>
              <button className="btn-secondary" onClick={() => setSelectedIds(new Set(filteredApps.map(g => g.appId)))}>
                Select all{appSearch ? ' (filtered)' : ''}
              </button>
              <button className="btn-secondary" onClick={() => setSelectedIds(new Set())}>Select none</button>
              <button className="btn-primary" onClick={saveSelection} disabled={steamSaving}>
                {steamSaving ? 'Saving...' : `Save selection (${selectedIds.size})`}
              </button>
            </>
          )}
        </div>
        {steamApps && (
          <>
            <input
              type="text"
              placeholder="Search games..."
              value={appSearch}
              onChange={e => setAppSearch(e.target.value)}
              style={{ marginTop: 8 }}
            />
            <div style={{ maxHeight: 300, overflow: 'auto', marginTop: 8, border: '1px solid var(--ctp-surface1, #45475a)', borderRadius: 6, padding: 8 }}>
              {filteredApps.map(g => (
                <label key={g.appId} className="checkbox-row" style={{ display: 'flex', gap: 8, padding: '2px 0', alignItems: 'center' }}>
                  <input type="checkbox" checked={selectedIds.has(g.appId)} onChange={() => toggleApp(g.appId)} />
                  <span>{g.name}</span>
                  {g.shared && (
                    <span style={{ fontSize: '0.7em', fontWeight: 600, color: '#1e1e2e', background: 'var(--ctp-mauve, #cba6f7)', padding: '1px 6px', borderRadius: 4 }}>
                      Family
                    </span>
                  )}
                </label>
              ))}
              {filteredApps.length === 0 && <div className="settings-hint">No games match.</div>}
            </div>
          </>
        )}
      </div>

      {providers.map(p => (
        <div key={p.id} className="settings-section" style={{ marginTop: 16, padding: 0 }}>
          <div className="settings-hint" style={{ marginBottom: 8 }}>
            <strong>{p.name}</strong> — {p.available ? 'bundled' : 'not bundled'}
            {p.requiresLogin ? ` · ${p.loggedIn ? 'logged in' : 'not logged in'}` : ' · no login needed'}
            {` · ${p.prefilledCount} prefilled`}
            {p.lastRunUtc ? ` · last run ${new Date(p.lastRunUtc).toLocaleString()}${p.lastExitCode != null ? ` (exit ${p.lastExitCode})` : ''}` : ''}
            {p.nextRunUtc ? ` · next run ${new Date(p.nextRunUtc).toLocaleString()}` : ''}
          </div>
          <div className="form-row" style={{ gap: 8 }}>
            <button
              className="btn-primary"
              onClick={() => runPrefill(p.id)}
              disabled={startingId === p.id || p.running || !p.available || !p.loggedIn}
            >
              {p.running ? 'Prefilling...' : startingId === p.id ? 'Starting...' : `Run ${p.name} prefill`}
            </button>
            {p.running && (
              <button
                className="btn-secondary"
                onClick={() => stopPrefill(p.id)}
                disabled={stoppingId === p.id}
              >
                {stoppingId === p.id ? 'Stopping...' : 'Stop'}
              </button>
            )}
          </div>

          {/* Per-provider schedule (saved with the LanCache Save button) */}
          <div className="form-group" style={{ marginTop: 8 }}>
            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={scheduleFor(p.id).enabled}
                onChange={e => updateSchedule(p.id, { enabled: e.target.checked })}
                disabled={saving}
              />
              <span>Run {p.name} prefill on a schedule</span>
            </label>
            {scheduleFor(p.id).enabled && (
              <div style={{ marginTop: 6, display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
                <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <span>at</span>
                  <input
                    type="time"
                    value={scheduleFor(p.id).time}
                    onChange={e => updateSchedule(p.id, { time: e.target.value || '04:00' })}
                    disabled={saving}
                  />
                </label>
                <span className="settings-hint" style={{ margin: 0 }}>on</span>
                {WEEKDAYS.map((label, idx) => {
                  const active = scheduleFor(p.id).days.includes(idx);
                  return (
                    <button
                      key={idx}
                      type="button"
                      className="btn-secondary"
                      onClick={() => toggleDay(p.id, idx)}
                      disabled={saving}
                      style={{
                        padding: '2px 8px',
                        background: active ? 'var(--ctp-yellow, #f9e2af)' : undefined,
                        color: active ? '#1e1e2e' : undefined,
                      }}
                    >
                      {label}
                    </button>
                  );
                })}
                <span className="settings-hint" style={{ margin: 0 }}>
                  {scheduleFor(p.id).days.length === 0 ? '(no day picked = every day)' : ''}
                </span>
              </div>
            )}
          </div>
          {!p.available && (
            <div className="alert alert-error" style={{ marginTop: 8 }}>
              {p.name}Prefill is not bundled in this image. Rebuild or pull the latest image.
            </div>
          )}
          {p.available && p.requiresLogin && !p.loggedIn && p.loginCommand && (
            <div className="alert alert-info" style={{ marginTop: 8 }}>
              One-time login required: <code>{p.loginCommand}</code>
            </div>
          )}
          {p.recentLog.length > 0 && (
            <pre style={{ marginTop: 8, maxHeight: 220, overflow: 'auto', background: 'var(--ctp-mantle, #181825)', padding: 8, borderRadius: 6, fontSize: '0.78em' }}>
              {p.recentLog.slice(-60).join('\n')}
            </pre>
          )}
        </div>
      ))}

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
