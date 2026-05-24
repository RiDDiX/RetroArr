import React, { useEffect, useState } from 'react';
import { getApiKey, setApiKey, getErrorMessage } from '../api/client';
import apiClient from '../api/client';
import { useTranslation } from '../i18n/translations';
import Modal from './ui/Modal';

// Shown automatically on first launch when bootstrap couldn't reach the
// server over loopback (e.g. user opens the LAN URL in a browser). Lets
// the user paste the key without hunting through Settings -> API Access.
//
// The bootstrap call at module-load time in api/client.ts may still be in
// flight on mount, so we wait a short grace period before deciding the
// user really has no key.
const BOOTSTRAP_GRACE_MS = 1500;

const ApiKeyGate: React.FC = () => {
  const { t } = useTranslation();
  const [show, setShow] = useState(false);
  const [manualKey, setManualKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Already have a key on mount? Nothing to do.
    if (getApiKey()) return;

    const decideTimer = window.setTimeout(() => {
      if (!getApiKey()) setShow(true);
    }, BOOTSTRAP_GRACE_MS);

    // Hide the modal if a key arrives via bootstrap, manual paste in
    // settings, or anywhere else.
    const onKeyChange = () => {
      if (getApiKey()) {
        setShow(false);
        window.clearTimeout(decideTimer);
      }
    };
    window.addEventListener('RetroArr_apiKey_changed', onKeyChange);

    return () => {
      window.clearTimeout(decideTimer);
      window.removeEventListener('RetroArr_apiKey_changed', onKeyChange);
    };
  }, []);

  const save = async () => {
    const trimmed = manualKey.trim();
    if (!trimmed) return;
    setBusy(true);
    setError(null);
    try {
      // Verify the key works before persisting it, otherwise the gate
      // happily closes and the user thinks they're set when every
      // subsequent request will 401.
      await apiClient.get('/system/status', { headers: { 'X-Api-Key': trimmed }, timeout: 8000 });
      setApiKey(trimmed); // fires RetroArr_apiKey_changed -> hub reconnect + this modal closes
      setManualKey('');
    } catch (e) {
      setError(getErrorMessage(e, t('apiKeyVerifyFailed')));
    } finally {
      setBusy(false);
    }
  };

  if (!show) return null;

  return (
    <Modal
      isOpen={show}
      onClose={() => { /* gate is intentionally undismissable until a valid key is set */ }}
      title={t('apiKeyNoneTitle')}
      maxWidth="540px"
    >
      <div style={{ display: 'grid', gap: 12 }}>
        <p style={{ margin: 0, lineHeight: 1.55 }}>
          {t('apiKeyNoneBody')}
        </p>

        <code style={{ display: 'block', padding: '8px 12px', background: 'var(--ctp-surface0, #313244)', borderRadius: 4, fontSize: 12, overflowX: 'auto' }}>
          docker exec -it retroarr cat /app/config/apikey.json
        </code>

        <small style={{ color: 'var(--ctp-overlay0, #6c7086)', lineHeight: 1.5 }}>
          {t('apiKeyNoneHint')}
        </small>

        <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
          <input
            type="text"
            value={manualKey}
            onChange={(e) => setManualKey(e.target.value)}
            placeholder={t('apiKeyPaste')}
            style={{ flex: 1, padding: '8px 10px', background: 'var(--ctp-base, #1e1e2e)', color: 'var(--ctp-text, #cdd6f4)', border: '1px solid var(--ctp-overlay0, #6c7086)', borderRadius: 4 }}
            disabled={busy}
            onKeyDown={(e) => { if (e.key === 'Enter') save(); }}
            autoFocus
          />
          <button
            type="button"
            className="btn-primary"
            onClick={save}
            disabled={busy || !manualKey.trim()}
          >
            {busy ? t('apiKeyVerifying') : t('save')}
          </button>
        </div>

        {error && <div className="alert alert-error" style={{ margin: 0 }}>{error}</div>}

        <small style={{ color: 'var(--ctp-overlay0, #6c7086)' }}>
          {t('apiKeyPasteHint')}
        </small>
      </div>
    </Modal>
  );
};

export default ApiKeyGate;
