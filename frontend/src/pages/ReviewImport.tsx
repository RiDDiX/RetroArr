import React, { useState, useEffect, useCallback } from 'react';
import apiClient from '../api/client';
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

const ReviewImport: React.FC = () => {
  const [items, setItems] = useState<ReviewItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [platforms, setPlatforms] = useState<PlatformOption[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editPlatformId, setEditPlatformId] = useState<number | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [actionLoading, setActionLoading] = useState<string | null>(null);

  const loadItems = useCallback(async () => {
    setLoading(true);
    try {
      const [itemsRes, platformsRes] = await Promise.all([
        apiClient.get('/review/items?pendingOnly=true'),
        apiClient.get('/resort/platforms')
      ]);
      setItems(itemsRes.data.items || []);
      setPlatforms(platformsRes.data || []);
    } catch (err) {
      console.error('Error loading review items:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadItems(); }, [loadItems]);

  const handleMap = async (id: string) => {
    setActionLoading(id);
    try {
      await apiClient.post(`/review/items/${id}/map`, {
        platformId: editPlatformId,
        overrideTitle: editTitle || undefined
      });
      setEditingId(null);
      await loadItems();
    } catch (err) {
      console.error('Error mapping item:', err);
    } finally {
      setActionLoading(null);
    }
  };

  const handleFinalize = async (id: string) => {
    setActionLoading(id);
    try {
      await apiClient.post(`/review/items/${id}/finalize`);
      await loadItems();
    } catch (err) {
      console.error('Error finalizing item:', err);
    } finally {
      setActionLoading(null);
    }
  };

  const handleIgnore = async (id: string) => {
    setActionLoading(id);
    try {
      await apiClient.post(`/review/items/${id}/ignore`);
      await loadItems();
    } catch (err) {
      console.error('Error ignoring item:', err);
    } finally {
      setActionLoading(null);
    }
  };

  const handleDismiss = async (id: string) => {
    setActionLoading(id);
    try {
      await apiClient.post(`/review/items/${id}/dismiss`);
      await loadItems();
    } catch (err) {
      console.error('Error dismissing item:', err);
    } finally {
      setActionLoading(null);
    }
  };

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
          {items.map(item => (
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
        </div>
      )}
    </div>
  );
};

export default ReviewImport;
