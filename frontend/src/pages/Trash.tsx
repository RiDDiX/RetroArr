import { useCallback, useEffect, useState } from 'react';
import apiClient, { getErrorMessage } from '../api/client';
import { t } from '../i18n/translations';
import './Trash.css';

interface TrashEntry {
  id: string;
  gameId?: number | null;
  gameTitle?: string | null;
  originalPath: string;
  trashPath: string;
  deletedAt: string;
  sizeBytes: number;
  isDirectory: boolean;
}

const fmtBytes = (b: number) =>
  b < 1024 ? `${b} B`
  : b < 1024 * 1024 ? `${(b / 1024).toFixed(1)} KB`
  : b < 1024 ** 3 ? `${(b / 1024 / 1024).toFixed(1)} MB`
  : `${(b / 1024 ** 3).toFixed(2)} GB`;

export default function Trash() {
  const [entries, setEntries] = useState<TrashEntry[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      const resp = await apiClient.get<TrashEntry[]>('/trash');
      setEntries(resp.data || []);
    } catch (e) {
      setError(getErrorMessage(e, 'Could not load trash.'));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    load();
    const handleLibraryUpdate = () => { load(); };
    window.addEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
    return () => window.removeEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
  }, [load]);

  const restore = async (id: string) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await apiClient.post(`/trash/${id}/restore`);
      setNotice(t('trashRestored') || 'Restored to the original path.');
      await load();
    } catch (e) {
      setError(getErrorMessage(e, 'Restore failed.'));
    } finally {
      setBusy(false);
    }
  };

  const purgeOne = async (id: string) => {
    if (!window.confirm(t('trashConfirmPurgeOne') || 'Delete this entry permanently? This cannot be undone.')) return;
    setBusy(true);
    setError(null);
    try {
      await apiClient.delete(`/trash/${id}`);
      await load();
    } catch (e) {
      setError(getErrorMessage(e, 'Delete failed.'));
    } finally {
      setBusy(false);
    }
  };

  const purgeAll = async () => {
    if (!window.confirm(t('trashConfirmPurgeAll') || 'Permanently delete EVERYTHING in the trash? This cannot be undone.')) return;
    setBusy(true);
    setError(null);
    try {
      const resp = await apiClient.delete<{ purged: number }>('/trash');
      setNotice(`${resp.data?.purged ?? 0} ${t('trashPurged') || 'entries purged.'}`);
      await load();
    } catch (e) {
      setError(getErrorMessage(e, 'Empty-trash failed.'));
    } finally {
      setBusy(false);
    }
  };

  const totalSize = entries.reduce((s, e) => s + (e.sizeBytes || 0), 0);

  return (
    <div className="page trash-page">
      <header className="page-header">
        <div>
          <h1>{t('trashTitle') || 'Trash'}</h1>
          <p className="trash-page__summary">
            {entries.length} {t('trashEntries') || 'entries'} · {fmtBytes(totalSize)}
          </p>
        </div>
        <div className="page-header__actions">
          <button className="retro-btn retro-btn--secondary" onClick={load} disabled={busy}>
            {t('refresh') || 'Refresh'}
          </button>
          <button className="retro-btn retro-btn--danger" onClick={purgeAll} disabled={busy || entries.length === 0}>
            {t('trashEmpty') || 'Empty trash'}
          </button>
        </div>
      </header>

      {error && <div className="alert alert-error">{error}</div>}
      {notice && <div className="alert alert-info">{notice}</div>}

      {!busy && entries.length === 0 && (
        <p className="trash-page__empty">
          {t('trashEmptyState') || 'Nothing in the trash. Deleted games land here first.'}
        </p>
      )}

      {entries.length > 0 && (
        <table className="trash-table">
          <thead>
            <tr>
              <th>{t('trashItem') || 'Item'}</th>
              <th>{t('trashOriginalPath') || 'Original path'}</th>
              <th>{t('trashDeletedAt') || 'Deleted'}</th>
              <th>{t('trashSize') || 'Size'}</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {entries.map((e) => (
              <tr key={e.id}>
                <td>
                  <div className="trash-table__title">{e.gameTitle || '(no title)'}</div>
                  <div className="trash-table__meta pixel">
                    {e.isDirectory ? 'DIR' : 'FILE'} · id {e.id}
                  </div>
                </td>
                <td><code>{e.originalPath}</code></td>
                <td>{new Date(e.deletedAt).toLocaleString()}</td>
                <td>{fmtBytes(e.sizeBytes)}</td>
                <td className="trash-table__actions">
                  <button className="retro-btn retro-btn--secondary" onClick={() => restore(e.id)} disabled={busy}>
                    {t('trashRestore') || 'Restore'}
                  </button>
                  <button className="retro-btn retro-btn--danger" onClick={() => purgeOne(e.id)} disabled={busy}>
                    {t('delete') || 'Delete'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
