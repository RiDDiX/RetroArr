import React, { useEffect, useState } from 'react';
import apiClient, { getApiKey, setApiKey, getErrorMessage } from '../../api/client';

interface Props {
  language: string;
  t: (key: string) => string;
}

const maskKey = (k: string | null) => {
  if (!k) return '';
  if (k.length <= 8) return '••••••••';
  return `${k.slice(0, 4)}${'•'.repeat(Math.max(4, k.length - 8))}${k.slice(-4)}`;
};

const ApiAccessTab: React.FC<Props> = ({ t }) => {
  const [current, setCurrent] = useState<string | null>(getApiKey());
  const [revealed, setRevealed] = useState(false);
  const [manualKey, setManualKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setCurrent(getApiKey());
  }, []);

  const rotate = async () => {
    if (!window.confirm(t('apiKeyConfirmRotate') || 'Rotate the API key? Other clients will need the new key.')) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const resp = await apiClient.post<{ apiKey: string }>('/system/apikey/rotate');
      if (resp.data?.apiKey) {
        setApiKey(resp.data.apiKey);
        setCurrent(resp.data.apiKey);
        setRevealed(true);
        setNotice(t('apiKeyRotated') || 'API key rotated. Copy it now — this is the only time it is shown in full.');
      }
    } catch (e) {
      setError(getErrorMessage(e, 'Rotation failed'));
    } finally {
      setBusy(false);
    }
  };

  const useManualKey = () => {
    if (!manualKey.trim()) return;
    setApiKey(manualKey.trim());
    setCurrent(manualKey.trim());
    setManualKey('');
    setNotice(t('apiKeyStoredLocally') || 'Key stored in this browser. The server was not modified.');
  };

  const copy = async () => {
    if (!current) return;
    try {
      await navigator.clipboard.writeText(current);
      setNotice(t('apiKeyCopied') || 'Key copied to clipboard.');
    } catch (e) {
      setError(getErrorMessage(e, 'Clipboard write failed.'));
    }
  };

  return (
    <div className="settings-section" id="api-access">
      <h2>{t('apiAccessTitle') || 'API access'}</h2>
      <p className="settings-description">
        {t('apiAccessDesc') ||
          'RetroArr accepts calls from localhost without authentication. LAN and remote clients must send this key as X-Api-Key. Rotate to lock out an old device.'}
      </p>

      {error && <div className="alert alert-error">{error}</div>}
      {notice && <div className="alert alert-info">{notice}</div>}

      <div className="form-group">
        <label>{t('apiKeyCurrent') || 'Current API key'}</label>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <code style={{ flex: 1, padding: '6px 10px', background: 'var(--surface-1)', borderRadius: 4 }}>
            {revealed && current ? current : maskKey(current)}
          </code>
          <button type="button" className="btn-secondary" onClick={() => setRevealed((v) => !v)} disabled={!current}>
            {revealed ? (t('hide') || 'Hide') : (t('reveal') || 'Reveal')}
          </button>
          <button type="button" className="btn-secondary" onClick={copy} disabled={!current || busy}>
            {t('copy') || 'Copy'}
          </button>
          <button type="button" className="btn-danger" onClick={rotate} disabled={busy}>
            {t('apiKeyRotate') || 'Rotate'}
          </button>
        </div>
      </div>

      <div className="form-group" style={{ marginTop: 16 }}>
        <label htmlFor="manual-api-key">{t('apiKeyPaste') || 'Paste a key issued by another install'}</label>
        <div style={{ display: 'flex', gap: 8 }}>
          <input
            id="manual-api-key"
            type="text"
            value={manualKey}
            onChange={(e) => setManualKey(e.target.value)}
            placeholder="paste key here"
            style={{ flex: 1 }}
            disabled={busy}
          />
          <button type="button" className="btn-secondary" onClick={useManualKey} disabled={!manualKey.trim() || busy}>
            {t('save') || 'Save'}
          </button>
        </div>
        <small style={{ display: 'block', marginTop: 4, color: 'var(--ctp-overlay0)' }}>
          {t('apiKeyPasteHint') || 'The key is stored in this browser only. It does not change the server key.'}
        </small>
      </div>
    </div>
  );
};

export default ApiAccessTab;
