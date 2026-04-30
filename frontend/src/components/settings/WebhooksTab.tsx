import React, { useCallback, useEffect, useState } from 'react';
import apiClient, { getErrorMessage } from '../../api/client';

interface WebhookDto {
  id: number;
  name: string;
  url: string;
  method: string;
  events: number;
  eventsList: string[];
  enabled: boolean;
  lastTriggeredAt?: string | null;
  lastResponseCode?: number | null;
  lastError?: string | null;
}

interface WebhookEventDef {
  value: number;
  name: string;
  description: string;
}

interface Props {
  language: string;
  t: (key: string) => string;
}

const emptyForm = {
  id: 0 as number,
  name: '',
  url: '',
  method: 'POST',
  events: 0,
  enabled: true,
};

const WebhooksTab: React.FC<Props> = ({ t }) => {
  const [webhooks, setWebhooks] = useState<WebhookDto[]>([]);
  const [events, setEvents] = useState<WebhookEventDef[]>([]);
  const [form, setForm] = useState(emptyForm);
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      const [listResp, eventsResp] = await Promise.all([
        apiClient.get<WebhookDto[]>('/webhook'),
        apiClient.get<WebhookEventDef[]>('/webhook/events'),
      ]);
      setWebhooks(listResp.data);
      setEvents(eventsResp.data);
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to load webhooks'));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const toggleEvent = (value: number) => {
    setForm((prev) => ({ ...prev, events: prev.events ^ value }));
  };

  const resetForm = () => {
    setForm(emptyForm);
    setEditing(false);
  };

  const submit = async () => {
    if (!form.name.trim() || !form.url.trim()) {
      setError(t('webhooksFillNameUrl') || 'Name and URL are required.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      if (editing && form.id) {
        await apiClient.put(`/webhook/${form.id}`, {
          name: form.name,
          url: form.url,
          method: form.method,
          events: form.events,
          enabled: form.enabled,
        });
      } else {
        await apiClient.post('/webhook', {
          name: form.name,
          url: form.url,
          method: form.method,
          events: form.events,
          enabled: form.enabled,
        });
      }
      resetForm();
      await load();
    } catch (e) {
      setError(getErrorMessage(e, 'Save failed'));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (id: number) => {
    if (!window.confirm(t('webhooksConfirmDelete') || 'Delete this webhook?')) return;
    setBusy(true);
    setError(null);
    try {
      await apiClient.delete(`/webhook/${id}`);
      await load();
    } catch (e) {
      setError(getErrorMessage(e, 'Delete failed'));
    } finally {
      setBusy(false);
    }
  };

  const test = async (id: number) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const resp = await apiClient.post<{ success: boolean; message: string }>(`/webhook/${id}/test`);
      setNotice(resp.data.message);
    } catch (e) {
      setError(getErrorMessage(e, 'Test failed'));
    } finally {
      setBusy(false);
    }
  };

  const editRow = (w: WebhookDto) => {
    setForm({
      id: w.id,
      name: w.name,
      url: w.url,
      method: w.method || 'POST',
      events: w.events,
      enabled: w.enabled,
    });
    setEditing(true);
  };

  return (
    <div className="settings-section" id="webhooks">
      <h2>{t('webhooks') || 'Webhooks'}</h2>
      <p className="settings-description">
        {t('webhooksDesc') || 'Fire outbound HTTP calls when events happen in RetroArr.'}
      </p>

      {error && <div className="alert alert-error">{error}</div>}
      {notice && <div className="alert alert-info">{notice}</div>}

      <div className="form-group">
        <label htmlFor="webhook-name">{t('webhooksName') || 'Name'}</label>
        <input
          id="webhook-name"
          type="text"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          disabled={busy}
        />
      </div>

      <div className="form-group">
        <label htmlFor="webhook-url">{t('webhooksUrl') || 'URL'}</label>
        <input
          id="webhook-url"
          type="url"
          placeholder="https://example.com/hook"
          value={form.url}
          onChange={(e) => setForm({ ...form, url: e.target.value })}
          disabled={busy}
        />
      </div>

      <div className="form-group">
        <label htmlFor="webhook-method">{t('webhooksMethod') || 'Method'}</label>
        <select
          id="webhook-method"
          value={form.method}
          onChange={(e) => setForm({ ...form, method: e.target.value })}
          disabled={busy}
        >
          <option value="POST">POST</option>
          <option value="PUT">PUT</option>
        </select>
      </div>

      <div className="form-group">
        <label>{t('webhooksEvents') || 'Events'}</label>
        <div className="checkbox-list">
          {events.map((ev) => (
            <label key={ev.value} className="checkbox-row">
              <input
                type="checkbox"
                checked={(form.events & ev.value) !== 0}
                onChange={() => toggleEvent(ev.value)}
                disabled={busy}
              />
              <span title={ev.description}>{ev.name}</span>
            </label>
          ))}
        </div>
      </div>

      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={form.enabled}
            onChange={(e) => setForm({ ...form, enabled: e.target.checked })}
            disabled={busy}
          />
          <span>{t('webhooksEnabled') || 'Enabled'}</span>
        </label>
      </div>

      <div className="form-actions">
        <button type="button" className="btn-primary" onClick={submit} disabled={busy}>
          {editing ? (t('save') || 'Save') : (t('webhooksAdd') || 'Add webhook')}
        </button>
        {editing && (
          <button type="button" className="btn-secondary" onClick={resetForm} disabled={busy}>
            {t('cancel') || 'Cancel'}
          </button>
        )}
      </div>

      <h3>{t('webhooksConfigured') || 'Configured webhooks'}</h3>
      {webhooks.length === 0 && <p>{t('webhooksNone') || 'No webhooks configured yet.'}</p>}
      <ul className="webhook-list">
        {webhooks.map((w) => (
          <li key={w.id} className="webhook-row">
            <div>
              <strong>{w.name}</strong>
              <span className={`badge ${w.enabled ? 'badge-on' : 'badge-off'}`}>
                {w.enabled ? (t('enabled') || 'enabled') : (t('disabled') || 'disabled')}
              </span>
              <div className="webhook-meta">
                <code>{w.method} {w.url}</code>
              </div>
              <div className="webhook-meta">
                {w.eventsList.length > 0 ? w.eventsList.join(', ') : (t('webhooksNoEvents') || 'no events selected')}
              </div>
              {w.lastTriggeredAt && (
                <div className="webhook-meta">
                  {t('webhooksLastTriggered') || 'Last triggered'}: {new Date(w.lastTriggeredAt).toLocaleString()}
                  {w.lastResponseCode != null ? ` - HTTP ${w.lastResponseCode}` : ''}
                </div>
              )}
              {w.lastError && <div className="webhook-error">{w.lastError}</div>}
            </div>
            <div className="webhook-actions">
              <button type="button" className="btn-secondary" onClick={() => test(w.id)} disabled={busy}>
                {t('webhooksTest') || 'Test'}
              </button>
              <button type="button" className="btn-secondary" onClick={() => editRow(w)} disabled={busy}>
                {t('edit') || 'Edit'}
              </button>
              <button type="button" className="btn-danger" onClick={() => remove(w.id)} disabled={busy}>
                {t('delete') || 'Delete'}
              </button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
};

export default WebhooksTab;
