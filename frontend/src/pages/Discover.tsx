import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import apiClient, { getErrorMessage } from '../api/client';
import { t as translate, getLanguage } from '../i18n/translations';
import './Discover.css';

interface DiscoveredItem {
  id: number;
  title: string;
  path: string;
  platformKey?: string | null;
  platformId?: number | null;
  serial?: string | null;
  isInstaller: boolean;
  isExternal: boolean;
  region?: string | null;
  languages?: string | null;
  revision?: string | null;
  discoveredAt: string;
}

const PROVIDERS = [
  { value: '', label: 'Default (per platform)' },
  { value: 'igdb', label: 'IGDB' },
  { value: 'screenscraper', label: 'ScreenScraper' },
  { value: 'thegamesdb', label: 'TheGamesDB' },
  { value: 'steamgriddb', label: 'SteamGridDB' },
];

const Discover: React.FC = () => {
  const navigate = useNavigate();
  const lang = getLanguage();
  const t = (k: Parameters<typeof translate>[0]) => translate(k, lang);

  const [items, setItems] = useState<DiscoveredItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [scanning, setScanning] = useState(false);
  const [busy, setBusy] = useState(false);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [perItemSource, setPerItemSource] = useState<Record<number, string>>({});
  const [globalSource, setGlobalSource] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const loadList = useCallback(async () => {
    try {
      setError(null);
      const res = await apiClient.get<DiscoveredItem[]>('/discovery');
      setItems(res.data);
      setSelected(prev => {
        const next = new Set<number>();
        const valid = new Set(res.data.map(d => d.id));
        prev.forEach(id => { if (valid.has(id)) next.add(id); });
        return next;
      });
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadList(); }, [loadList]);

  // when a discovery scan is running on the backend, refresh every 2s
  useEffect(() => {
    if (!scanning) return;
    const interval = setInterval(loadList, 2000);
    return () => clearInterval(interval);
  }, [scanning, loadList]);

  const triggerScan = async () => {
    setScanning(true);
    setInfo(null);
    setError(null);
    try {
      await apiClient.post('/discovery/scan');
      setInfo('Discovery scan started, results will appear as they are found.');
      // optimistic poll for ~3 minutes max
      let elapsed = 0;
      const stopPoll = setInterval(async () => {
        elapsed += 3000;
        await loadList();
        if (elapsed > 180_000) { clearInterval(stopPoll); setScanning(false); }
      }, 3000);
      // stop spinner after 60s in case the backend doesn't expose progress; user sees the list grow regardless
      setTimeout(() => setScanning(false), 60_000);
    } catch (err) {
      setError(getErrorMessage(err));
      setScanning(false);
    }
  };

  const toggleOne = (id: number) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const toggleAll = () => {
    if (selected.size === items.length) setSelected(new Set());
    else setSelected(new Set(items.map(d => d.id)));
  };

  const importSelected = async () => {
    if (selected.size === 0) return;
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const ids = Array.from(selected);
      // group by per-item source so we can issue one request per source bucket
      const buckets: Record<string, number[]> = {};
      for (const id of ids) {
        const src = perItemSource[id] ?? globalSource ?? '';
        const key = src || '__default__';
        if (!buckets[key]) buckets[key] = [];
        buckets[key].push(id);
      }
      let imported = 0;
      let failed = 0;
      for (const [src, bucket] of Object.entries(buckets)) {
        const payload = src === '__default__'
          ? { ids: bucket }
          : { ids: bucket, metadataSource: src };
        const res = await apiClient.post('/discovery/import', payload);
        imported += res.data.imported || 0;
        failed += res.data.failed || 0;
      }
      setInfo(`Imported ${imported}${failed > 0 ? `, ${failed} failed` : ''}.`);
      await loadList();
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const dropSelected = async () => {
    if (selected.size === 0) return;
    if (!window.confirm(`Discard ${selected.size} item(s) without importing?`)) return;
    setBusy(true);
    try {
      await apiClient.post('/discovery/bulk-delete', { ids: Array.from(selected) });
      await loadList();
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  const clearAll = async () => {
    if (items.length === 0) return;
    if (!window.confirm(`Discard all ${items.length} discovery item(s)?`)) return;
    setBusy(true);
    try {
      await apiClient.delete('/discovery');
      await loadList();
    } catch (err) {
      setError(getErrorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="discover-page">
      <div className="discover-header">
        <div>
          <h1>Discover</h1>
          <p className="discover-subtitle">
            Scan your library folders without fetching metadata. Pick which games to import and which provider to use.
          </p>
        </div>
        <div className="discover-toolbar">
          <button className="btn-secondary" onClick={() => navigate('/settings#media')}>{t('settingsMedia')}</button>
          <button className="btn-primary" onClick={triggerScan} disabled={scanning || busy}>
            {scanning ? 'Scanning...' : 'Run discovery scan'}
          </button>
          {items.length > 0 && (
            <button className="btn-delete" onClick={clearAll} disabled={busy}>
              Discard all
            </button>
          )}
        </div>
      </div>

      <div className="discover-summary">
        <span className="discover-counter">{items.length} pending</span>
        <span style={{ color: 'var(--ctp-subtext0)' }}>{selected.size} selected</span>
        <span style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <label htmlFor="global-source" style={{ fontSize: '0.85rem' }}>Apply provider to all selected:</label>
          <select
            id="global-source"
            className="discover-source-select"
            style={{ width: '180px' }}
            value={globalSource}
            onChange={e => setGlobalSource(e.target.value)}
          >
            {PROVIDERS.map(p => <option key={p.value || 'default'} value={p.value}>{p.label}</option>)}
          </select>
        </span>
      </div>

      {error && <div className="test-result error" style={{ marginBottom: '1rem' }}>{error}</div>}
      {info && <div className="test-result success" style={{ marginBottom: '1rem' }}>{info}</div>}

      {loading ? (
        <div className="discover-empty">Loading...</div>
      ) : items.length === 0 ? (
        <div className="discover-empty">
          No discoveries pending. Click "Run discovery scan" to find games without importing them yet.
        </div>
      ) : (
        <table className="discover-table">
          <thead>
            <tr>
              <th className="col-checkbox">
                <input
                  type="checkbox"
                  checked={items.length > 0 && selected.size === items.length}
                  onChange={toggleAll}
                  aria-label="Toggle all"
                />
              </th>
              <th>Title</th>
              <th>Path</th>
              <th className="col-platform">Platform</th>
              <th className="col-source">Provider override</th>
            </tr>
          </thead>
          <tbody>
            {items.map(d => (
              <tr key={d.id}>
                <td className="col-checkbox">
                  <input
                    type="checkbox"
                    checked={selected.has(d.id)}
                    onChange={() => toggleOne(d.id)}
                  />
                </td>
                <td>
                  <div style={{ fontWeight: 500 }}>{d.title}</div>
                  {(d.region || d.languages || d.revision) && (
                    <div style={{ fontSize: '0.75rem', color: 'var(--ctp-subtext0)' }}>
                      {[d.region, d.languages, d.revision].filter(Boolean).join(' / ')}
                    </div>
                  )}
                </td>
                <td className="discover-path" title={d.path}>{d.path}</td>
                <td>{d.platformKey || '-'}</td>
                <td>
                  <select
                    className="discover-source-select"
                    value={perItemSource[d.id] ?? ''}
                    onChange={e => setPerItemSource(prev => ({ ...prev, [d.id]: e.target.value }))}
                  >
                    {PROVIDERS.map(p => <option key={p.value || 'default'} value={p.value}>{p.label}</option>)}
                  </select>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {selected.size > 0 && (
        <div className="discover-import-bar">
          <span>{selected.size} item(s) ready to import</span>
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button className="btn-delete" onClick={dropSelected} disabled={busy}>
              Discard selected
            </button>
            <button className="btn-primary" onClick={importSelected} disabled={busy}>
              {busy ? 'Importing...' : `Import ${selected.size}`}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default Discover;
