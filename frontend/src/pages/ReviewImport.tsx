import React, { useState, useEffect, useCallback } from 'react';
import apiClient, { getErrorMessage } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import {
  faFileImport, faRefresh, faCheck, faEyeSlash,
  faSpinner, faExclamationTriangle, faFolder, faGamepad, faTrash
} from '@fortawesome/free-solid-svg-icons';
import PlatformIcon from '../components/PlatformIcon';
import RegionFlag from '../components/RegionFlag';
import './ReviewImport.css';

interface ReviewItem {
  id: string;
  filePaths: string[];
  detectedPlatformKey: string | null;
  detectedPlatformId: number | null;
  detectedTitle: string | null;
  diskName: string | null;
  region: string | null;
  serial: string | null;
  reason: string;
  reasonDetail: string | null;
  status: string;
  createdAt: string;
  assignedPlatformId: number | null;
  assignedGameId: number | null;
  overrideTitle: string | null;
  overrideDiskName: string | null;
}

interface PlatformOption {
  id: number;
  name: string;
  folderName: string;
  slug: string;
}

// Thousands of rows at once freeze the page for many seconds
const PAGE_SIZE = 200;

const ReviewImport: React.FC = () => {
  const [items, setItems] = useState<ReviewItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [platforms, setPlatforms] = useState<PlatformOption[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editPlatformId, setEditPlatformId] = useState<number | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [shown, setShown] = useState(PAGE_SIZE);

  const loadItems = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [itemsRes, platformsRes] = await Promise.all([
        apiClient.get('/review/items?pendingOnly=true'),
        apiClient.get('/resort/platforms')
      ]);
      setItems(itemsRes.data.items || []);
      setPlatforms(platformsRes.data || []);
    } catch (err) {
      setError(getErrorMessage(err, 'Error loading review items'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadItems(); }, [loadItems]);

  // Each action changes only its own item, so the list is not fetched again
  const act = async (id: string, request: () => Promise<unknown>, done: (list: ReviewItem[]) => ReviewItem[], fallback: string) => {
    setActionLoading(id);
    setError(null);
    try {
      await request();
      setItems(done);
      return true;
    } catch (err) {
      setError(getErrorMessage(err, fallback));
      return false;
    } finally {
      setActionLoading(null);
    }
  };
  const without = (id: string) => (list: ReviewItem[]) => list.filter(i => i.id !== id);

  const handleMap = async (id: string) => {
    const overrideTitle = editTitle || undefined;
    const mapped = await act(id, () => apiClient.post(`/review/items/${id}/map`, { platformId: editPlatformId, overrideTitle }),
      list => list.map(i => i.id === id
        ? { ...i, status: 'Mapped', assignedPlatformId: editPlatformId ?? i.assignedPlatformId, overrideTitle: overrideTitle ?? i.overrideTitle }
        : i),
      'Error mapping item');
    if (mapped) setEditingId(null);
  };

  const handleFinalize = (id: string) =>
    act(id, () => apiClient.post(`/review/items/${id}/finalize`, undefined, { timeout: 600000 }), without(id), 'Error finalizing item');

  const handleIgnore = (id: string) =>
    act(id, () => apiClient.post(`/review/items/${id}/ignore`), without(id), 'Error ignoring item');

  const handleDismiss = (id: string) =>
    act(id, () => apiClient.post(`/review/items/${id}/dismiss`), without(id), 'Error dismissing item');

  const startEditing = (item: ReviewItem) => {
    setEditingId(item.id);
    setEditPlatformId(item.assignedPlatformId || item.detectedPlatformId);
    setEditTitle(item.overrideTitle || item.detectedTitle || '');
  };

  const getReasonLabel = (reason: string) => {
    switch (reason) {
      case 'PlatformAmbiguous': return 'Platform Unknown';
      case 'TitleAmbiguous': return 'Title Ambiguous';
      case 'MultipleMetadataMatches': return 'Multiple Matches';
      case 'UnknownExtension': return 'Unknown File Type';
      case 'LowConfidenceMatch': return 'Low Confidence';
      default: return reason;
    }
  };

  return (
    <div className="review-import-page">
      <div className="review-import-header">
        <h2>
          <FontAwesomeIcon icon={faFileImport} style={{ marginRight: '10px', color: 'var(--ctp-peach)' }} />
          Review Import Files
        </h2>
        <button className="refresh-btn" onClick={loadItems} disabled={loading}>
          <FontAwesomeIcon icon={loading ? faSpinner : faRefresh} spin={loading} />
          Refresh
        </button>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {loading && items.length === 0 && (
        <div className="review-import-loading">
          <FontAwesomeIcon icon={faSpinner} spin style={{ fontSize: '24px' }} />
          <p>Loading review items...</p>
        </div>
      )}

      {!loading && items.length === 0 && (
        <div className="review-import-empty">
          <FontAwesomeIcon icon={faCheck} style={{ fontSize: '32px', color: 'var(--ctp-green)', marginBottom: '12px' }} />
          <p>No items pending review</p>
          <p style={{ fontSize: '13px', color: 'var(--ctp-overlay0)' }}>
            Files that cannot be automatically assigned to a platform during scanning will appear here.
          </p>
        </div>
      )}

      {items.length > 0 && (
        <div className="review-import-list">
          {items.slice(0, shown).map(item => (
            <div key={item.id} className="review-item">
              <div className="review-item-header">
                <div className="review-item-title-row">
                  {item.detectedPlatformId && (
                    <PlatformIcon platformId={item.detectedPlatformId} size={20} />
                  )}
                  <span className="review-item-title">{item.overrideTitle || item.detectedTitle || 'Unknown'}</span>
                  {item.region && <RegionFlag region={item.region} size="small" />}
                  <span className="review-item-reason">
                    <FontAwesomeIcon icon={faExclamationTriangle} style={{ marginRight: '4px' }} />
                    {getReasonLabel(item.reason)}
                  </span>
                </div>
                <div className="review-item-meta">
                  <FontAwesomeIcon icon={faFolder} style={{ opacity: 0.5, marginRight: '4px' }} />
                  <span className="review-item-path" title={item.filePaths[0]}>
                    {item.filePaths[0]?.split('/').pop() || item.filePaths[0]}
                  </span>
                  {item.reasonDetail && (
                    <span className="review-item-detail" title={item.reasonDetail}>
                      - {item.reasonDetail.length > 80 ? item.reasonDetail.substring(0, 80) + '...' : item.reasonDetail}
                    </span>
                  )}
                </div>
              </div>

              {editingId === item.id ? (
                <div className="review-item-edit">
                  <div className="review-item-edit-row">
                    <label>Platform:</label>
                    <select
                      value={editPlatformId || ''}
                      onChange={e => setEditPlatformId(parseInt(e.target.value) || null)}
                    >
                      <option value="">Select platform...</option>
                      {platforms.map(p => (
                        <option key={p.id} value={p.id}>{p.name}</option>
                      ))}
                    </select>
                  </div>
                  <div className="review-item-edit-row">
                    <label>Title:</label>
                    <input
                      type="text"
                      value={editTitle}
                      onChange={e => setEditTitle(e.target.value)}
                      placeholder="Game title"
                    />
                  </div>
                  <div className="review-item-edit-actions">
                    <button
                      className="btn-save"
                      onClick={() => handleMap(item.id)}
                      disabled={!editPlatformId || actionLoading === item.id}
                    >
                      {actionLoading === item.id ? <FontAwesomeIcon icon={faSpinner} spin /> : <FontAwesomeIcon icon={faCheck} />}
                      Save Mapping
                    </button>
                    <button className="btn-cancel" onClick={() => setEditingId(null)}>
                      Cancel
                    </button>
                  </div>
                </div>
              ) : (
                <div className="review-item-actions">
                  <button className="btn-edit" onClick={() => startEditing(item)} title="Map to platform">
                    <FontAwesomeIcon icon={faGamepad} /> Map
                  </button>
                  {item.status === 'Mapped' && (
                    <button
                      className="btn-finalize"
                      onClick={() => handleFinalize(item.id)}
                      disabled={actionLoading === item.id}
                      title="Finalize and add to library"
                    >
                      {actionLoading === item.id ? <FontAwesomeIcon icon={faSpinner} spin /> : <FontAwesomeIcon icon={faCheck} />}
                      Finalize
                    </button>
                  )}
                  <button
                    className="btn-ignore"
                    onClick={() => handleIgnore(item.id)}
                    disabled={actionLoading === item.id}
                    title="Ignore this file"
                  >
                    <FontAwesomeIcon icon={faEyeSlash} /> Ignore
                  </button>
                  <button
                    className="btn-dismiss"
                    onClick={() => handleDismiss(item.id)}
                    disabled={actionLoading === item.id}
                    title="Dismiss permanently"
                  >
                    <FontAwesomeIcon icon={faTrash} /> Dismiss
                  </button>
                </div>
              )}
            </div>
          ))}
          {items.length > shown && (
            <div className="review-item-actions" style={{ justifyContent: 'center' }}>
              <button className="btn-ignore" onClick={() => setShown(n => n + PAGE_SIZE)}>
                Show more ({items.length - shown} left)
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default ReviewImport;
