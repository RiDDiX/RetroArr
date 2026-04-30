import React, { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import apiClient, { getErrorMessage } from '../api/client';

interface SaveStateSlot {
  slot: number;
  size: number;
  modified: string;
}

const SLOT_COUNT = 8;
const SAVE_STATE_MAX_MB = 128;

const SaveStates: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const gameId = Number(id);
  const [slots, setSlots] = useState<SaveStateSlot[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!gameId) return;
    setBusy(true);
    setError(null);
    try {
      const resp = await apiClient.get<SaveStateSlot[]>(`/emulator/${gameId}/states`);
      setSlots(resp.data || []);
    } catch (e) {
      setError(getErrorMessage(e, 'Failed to load save states.'));
    } finally {
      setBusy(false);
    }
  }, [gameId]);

  useEffect(() => {
    load();
  }, [load]);

  const download = async (slot: number) => {
    try {
      const resp = await apiClient.get(`/emulator/${gameId}/states/${slot}`, { responseType: 'blob' });
      const blob = resp.data as Blob;
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${gameId}_slot${slot}.state`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError(getErrorMessage(e, 'Download failed.'));
    }
  };

  const remove = async (slot: number) => {
    if (!window.confirm(`Delete save slot ${slot}?`)) return;
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      await apiClient.delete(`/emulator/${gameId}/states/${slot}`);
      setInfo(`Slot ${slot} deleted.`);
      await load();
    } catch (e) {
      setError(getErrorMessage(e, 'Delete failed.'));
    } finally {
      setBusy(false);
    }
  };

  const upload = async (slot: number, file: File) => {
    if (file.size > SAVE_STATE_MAX_MB * 1024 * 1024) {
      setError(`File is ${(file.size / 1024 / 1024).toFixed(1)} MB - the server caps save states at ${SAVE_STATE_MAX_MB} MB.`);
      return;
    }
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const buffer = await file.arrayBuffer();
      await apiClient.post(`/emulator/${gameId}/states/${slot}`, buffer, {
        headers: { 'Content-Type': 'application/octet-stream' },
      });
      setInfo(`Slot ${slot} updated.`);
      await load();
    } catch (e) {
      setError(getErrorMessage(e, 'Upload failed.'));
    } finally {
      setBusy(false);
    }
  };

  const slotData = (s: number) => slots.find((x) => x.slot === s);
  const fmtBytes = (b: number) => (b < 1024 ? `${b} B` : b < 1024 * 1024 ? `${(b / 1024).toFixed(1)} KB` : `${(b / 1024 / 1024).toFixed(1)} MB`);

  return (
    <div className="page-content">
      <h1>Save states - game #{gameId}</h1>
      {error && <div className="alert alert-error">{error}</div>}
      {info && <div className="alert alert-info">{info}</div>}

      <table className="save-states-table">
        <thead>
          <tr>
            <th>Slot</th>
            <th>Size</th>
            <th>Modified</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {Array.from({ length: SLOT_COUNT }, (_, i) => i).map((s) => {
            const data = slotData(s);
            return (
              <tr key={s}>
                <td>{s}</td>
                <td>{data ? fmtBytes(data.size) : '-'}</td>
                <td>{data ? new Date(data.modified).toLocaleString() : '-'}</td>
                <td>
                  {data && (
                    <>
                      <button type="button" className="btn-secondary" onClick={() => download(s)} disabled={busy}>Download</button>
                      <button type="button" className="btn-danger" onClick={() => remove(s)} disabled={busy}>Delete</button>
                    </>
                  )}
                  <label className="btn-secondary" style={{ marginLeft: 8, cursor: 'pointer' }}>
                    Upload
                    <input
                      type="file"
                      accept=".state,application/octet-stream"
                      style={{ display: 'none' }}
                      onChange={(e) => { const f = e.target.files?.[0]; if (f) upload(s, f); e.currentTarget.value = ''; }}
                    />
                  </label>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
};

export default SaveStates;
