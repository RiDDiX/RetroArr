import React, { useState, useEffect, useCallback } from 'react';
import { settingsApi } from '../../api/client';

interface LoggingSettings {
  enabled: boolean;
  logDirectory: string;
  logLevel: string;
  perFeatureFiles: boolean;
  maxDays: number;
  maxTotalSizeMb: number;
  rotateSizeMb: number;
  redactTokens: boolean;
  redactHeaders: string[];
}

interface LogFile {
  name: string;
  sizeMb: number;
  lastModified: string;
}

interface LoggingResponse {
  settings: LoggingSettings;
  effectiveLogDirectory: string;
  defaultLogDirectory: string;
  logFiles: LogFile[];
}

interface Props {
  language: string;
  t: (key: string) => string;
}

const LoggingTab: React.FC<Props> = ({ t }) => {
  const [settings, setSettings] = useState<LoggingSettings>({
    enabled: true,
    logDirectory: '',
    logLevel: 'Info',
    perFeatureFiles: true,
    maxDays: 14,
    maxTotalSizeMb: 500,
    rotateSizeMb: 50,
    redactTokens: true,
    redactHeaders: ['Authorization', 'Cookie', 'X-Api-Key'],
  });
  const [effectiveDir, setEffectiveDir] = useState('');
  const [defaultDir, setDefaultDir] = useState('');
  const [logFiles, setLogFiles] = useState<LogFile[]>([]);
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [exportRange, setExportRange] = useState('7d');

  const loadSettings = useCallback(async () => {
    try {
      const res = await settingsApi.getLogging();
      const data = res.data as LoggingResponse;
      setSettings(data.settings);
      setEffectiveDir(data.effectiveLogDirectory);
      setDefaultDir(data.defaultLogDirectory);
      setLogFiles(data.logFiles);
    } catch {
      setStatus('Failed to load logging settings');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadSettings();
  }, [loadSettings]);

  const handleSave = async () => {
    setStatus('');
    try {
      await settingsApi.saveLogging(settings);
      setStatus('Logging settings saved');
      await loadSettings();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Save failed';
      setStatus(msg);
    }
  };

  const handleExport = async () => {
    setStatus('Preparing export...');
    try {
      const res = await settingsApi.exportLogs({ timeRange: exportRange });
      const blob = new Blob([res.data], { type: 'application/zip' });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `retroarr_logs_${new Date().toISOString().slice(0, 10)}.zip`;
      a.click();
      window.URL.revokeObjectURL(url);
      setStatus('Export downloaded');
    } catch {
      setStatus('Export failed');
    }
  };

  const copyPath = () => {
    navigator.clipboard.writeText(effectiveDir).then(() => setStatus('Path copied'));
  };

  if (loading) return <div className="settings-section"><p>Loading...</p></div>;

  return (
    <div className="settings-section" id="logging">
      <h2>Logging</h2>
      <p className="settings-description">
        Configure persistent logging for diagnostics and error analysis. Logs are written to disk with rotation and optional per-feature files.
      </p>

      {/* Enabled */}
      <div className="form-group">
        <label className="toggle-label">
          <input
            type="checkbox"
            checked={settings.enabled}
            onChange={(e) => setSettings({ ...settings, enabled: e.target.checked })}
          />
          <span>Logging Enabled</span>
        </label>
      </div>

      {/* Log Directory */}
      <div className="form-group">
        <label htmlFor="log-directory">Log Directory</label>
        <input
          id="log-directory"
          type="text"
          value={settings.logDirectory}
          onChange={(e) => setSettings({ ...settings, logDirectory: e.target.value })}
          placeholder={defaultDir || 'Default directory'}
        />
        <small className="form-hint">
          Effective: <code>{effectiveDir}</code>
          <button type="button" className="btn-link" onClick={copyPath} style={{ marginLeft: 8 }}>Copy</button>
        </small>
      </div>

      {/* Log Level */}
      <div className="form-group">
        <label htmlFor="log-level">Log Level</label>
        <select
          id="log-level"
          value={settings.logLevel}
          onChange={(e) => setSettings({ ...settings, logLevel: e.target.value })}
        >
          <option value="Debug">Debug</option>
          <option value="Info">Info</option>
          <option value="Warn">Warning</option>
          <option value="Error">Error</option>
        </select>
      </div>

      {/* Per-Feature Files */}
      <div className="form-group">
        <label className="toggle-label">
          <input
            type="checkbox"
            checked={settings.perFeatureFiles}
            onChange={(e) => setSettings({ ...settings, perFeatureFiles: e.target.checked })}
          />
          <span>Per-Feature Log Files</span>
        </label>
        <small className="form-hint">Write separate log files for scanner, search, downloads, etc.</small>
      </div>

      {/* Rotation Settings */}
      <div className="form-row" style={{ display: 'flex', gap: 16 }}>
        <div className="form-group" style={{ flex: 1 }}>
          <label htmlFor="max-days">Retention (days)</label>
          <input
            id="max-days"
            type="number"
            min={1}
            max={365}
            value={settings.maxDays}
            onChange={(e) => setSettings({ ...settings, maxDays: parseInt(e.target.value) || 14 })}
          />
        </div>
        <div className="form-group" style={{ flex: 1 }}>
          <label htmlFor="rotate-size">Rotate at (MB)</label>
          <input
            id="rotate-size"
            type="number"
            min={1}
            max={1000}
            value={settings.rotateSizeMb}
            onChange={(e) => setSettings({ ...settings, rotateSizeMb: parseInt(e.target.value) || 50 })}
          />
        </div>
        <div className="form-group" style={{ flex: 1 }}>
          <label htmlFor="max-total">Max Total (MB)</label>
          <input
            id="max-total"
            type="number"
            min={10}
            max={10000}
            value={settings.maxTotalSizeMb}
            onChange={(e) => setSettings({ ...settings, maxTotalSizeMb: parseInt(e.target.value) || 500 })}
          />
        </div>
      </div>

      {/* Privacy / Redaction */}
      <div className="form-group">
        <label className="toggle-label">
          <input
            type="checkbox"
            checked={settings.redactTokens}
            onChange={(e) => setSettings({ ...settings, redactTokens: e.target.checked })}
          />
          <span>Redact Sensitive Data</span>
        </label>
        <small className="form-hint">Redact API keys, tokens, and sensitive headers from log files and exports.</small>
      </div>

      <div className="form-group">
        <label htmlFor="redact-headers">Redacted Headers</label>
        <input
          id="redact-headers"
          type="text"
          value={settings.redactHeaders.join(', ')}
          onChange={(e) => setSettings({ ...settings, redactHeaders: e.target.value.split(',').map(h => h.trim()).filter(Boolean) })}
        />
        <small className="form-hint">Comma-separated list of HTTP headers to redact.</small>
      </div>

      <button type="button" className="btn-primary" onClick={handleSave}>
        {t('save') || 'Save'}
      </button>

      {status && <p className="settings-status" style={{ marginTop: 8 }}>{status}</p>}

      {/* Log Files */}
      {logFiles.length > 0 && (
        <div style={{ marginTop: 24 }}>
          <h3>Log Files</h3>
          <table className="settings-table" style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th style={{ textAlign: 'left', padding: '6px 8px' }}>File</th>
                <th style={{ textAlign: 'right', padding: '6px 8px' }}>Size</th>
                <th style={{ textAlign: 'right', padding: '6px 8px' }}>Last Modified</th>
              </tr>
            </thead>
            <tbody>
              {logFiles.map((f) => (
                <tr key={f.name}>
                  <td style={{ padding: '4px 8px', fontFamily: 'monospace', fontSize: 13 }}>{f.name}</td>
                  <td style={{ textAlign: 'right', padding: '4px 8px' }}>{f.sizeMb} MB</td>
                  <td style={{ textAlign: 'right', padding: '4px 8px', fontSize: 12 }}>{new Date(f.lastModified).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Export */}
      <div style={{ marginTop: 16, display: 'flex', alignItems: 'center', gap: 12 }}>
        <select value={exportRange} onChange={(e) => setExportRange(e.target.value)} style={{ padding: '6px 10px' }}>
          <option value="24h">Last 24 hours</option>
          <option value="7d">Last 7 days</option>
          <option value="30d">Last 30 days</option>
        </select>
        <button type="button" className="btn-secondary" onClick={handleExport}>
          Export Diagnostics (.zip)
        </button>
      </div>
    </div>
  );
};

export default LoggingTab;
