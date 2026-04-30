import React, { useState, useEffect } from 'react';
import apiClient, { getErrorMessage } from '../../api/client';
import { Modal, ConfirmDialog } from '../ui';
import HydraSourceModal from '../HydraSourceModal';
import prowlarrLogo from '../../assets/prowlarr_logo.png';
import jackettLogo from '../../assets/jackett_logo.png';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faPlus, faTimes } from '@fortawesome/free-solid-svg-icons';

interface HydraConfiguration {
  id: number;
  name: string;
  url: string;
  enabled: boolean;
}

interface IndexersTabProps {
  language: string;
  t: (key: string) => string;
}

const IndexersTab: React.FC<IndexersTabProps> = ({ t }) => {
  // Prowlarr
  const [prowlarrUrl, setProwlarrUrl] = useState('');
  const [prowlarrApiKey, setProwlarrApiKey] = useState('');
  const [prowlarrEnabled, setProwlarrEnabled] = useState(true);
  const [showProwlarrModal, setShowProwlarrModal] = useState(false);
  const [prowlarrTesting, setProwlarrTesting] = useState(false);
  const [prowlarrTestResult, setProwlarrTestResult] = useState<{ success: boolean; message: string } | null>(null);

  // Jackett
  const [jackettUrl, setJackettUrl] = useState('');
  const [jackettApiKey, setJackettApiKey] = useState('');
  const [jackettEnabled, setJackettEnabled] = useState(true);
  const [showJackettModal, setShowJackettModal] = useState(false);
  const [jackettTesting, setJackettTesting] = useState(false);
  const [jackettTestResult, setJackettTestResult] = useState<{ success: boolean; message: string } | null>(null);

  // Hydra (external JSON sources)
  const [hydraSources, setHydraSources] = useState<HydraConfiguration[]>([]);
  const [showHydraModal, setShowHydraModal] = useState(false);
  const [editingHydraSource, setEditingHydraSource] = useState<HydraConfiguration | null>(null);
  const [deleteConfirmation, setDeleteConfirmation] = useState<{ id: number; name: string } | null>(null);

  useEffect(() => {
    loadSettings();
    loadHydraSources();
  }, []);

  const loadSettings = async () => {
    try {
      const [prowlarrRes, jackettRes] = await Promise.all([
        apiClient.get('/settings/prowlarr'),
        apiClient.get('/settings/jackett'),
      ]);
      setProwlarrUrl(prowlarrRes.data.url);
      setProwlarrApiKey(prowlarrRes.data.apiKey);
      setProwlarrEnabled(prowlarrRes.data.enabled !== false);
      setJackettUrl(jackettRes.data.url);
      setJackettApiKey(jackettRes.data.apiKey);
      setJackettEnabled(jackettRes.data.enabled !== false);
    } catch (error) {
      console.error('Error loading indexer settings:', error);
    }
  };

  const loadHydraSources = async () => {
    try {
      const response = await apiClient.get('/hydra');
      setHydraSources(Array.isArray(response.data) ? response.data : []);
    } catch (error) {
      console.error('Error loading Hydra sources:', error);
      setHydraSources([]);
    }
  };

  // Prowlarr handlers
  const toggleProwlarr = async () => {
    const newState = !prowlarrEnabled;
    setProwlarrEnabled(newState);
    try {
      await apiClient.post('/settings/prowlarr', { url: prowlarrUrl, apiKey: prowlarrApiKey, enabled: newState });
    } catch {
      setProwlarrEnabled(!newState);
      alert('Failed to update Prowlarr status');
    }
  };

  const handleSaveProwlarr = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/settings/prowlarr', { url: prowlarrUrl, apiKey: prowlarrApiKey, enabled: prowlarrEnabled });
      alert(t('prowlarrSettingsSaved'));
      setShowProwlarrModal(false);
    } catch (error: unknown) {
      alert(`${t('error')} ${t('saveProwlarr')}: ${getErrorMessage(error)}`);
    }
  };

  const handleTestProwlarr = async () => {
    setProwlarrTesting(true);
    setProwlarrTestResult(null);
    try {
      const response = await apiClient.post('/search/test', { url: prowlarrUrl, apiKey: prowlarrApiKey });
      setProwlarrTestResult({ success: response.data.connected, message: response.data.connected ? t('connectionSuccessful') : t('connectionFailed') });
    } catch (error: unknown) {
      setProwlarrTestResult({ success: false, message: `✗ ${t('error')}: ${getErrorMessage(error)}` });
    } finally {
      setProwlarrTesting(false);
    }
  };

  // Jackett handlers
  const toggleJackett = async () => {
    const newState = !jackettEnabled;
    setJackettEnabled(newState);
    try {
      await apiClient.post('/settings/jackett', { url: jackettUrl, apiKey: jackettApiKey, enabled: newState });
    } catch {
      setJackettEnabled(!newState);
      alert('Failed to update Jackett status');
    }
  };

  const handleSaveJackett = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await apiClient.post('/settings/jackett', { url: jackettUrl, apiKey: jackettApiKey, enabled: jackettEnabled });
      alert(t('jackettSettingsSaved'));
      setShowJackettModal(false);
    } catch (error: unknown) {
      alert(`${t('error')} ${t('saveJackett')}: ${getErrorMessage(error)}`);
    }
  };

  const handleTestJackett = async () => {
    setJackettTesting(true);
    setJackettTestResult(null);
    try {
      const response = await apiClient.post('/search/test', { url: jackettUrl, apiKey: jackettApiKey, type: 'jackett' });
      setJackettTestResult({ success: response.data.connected, message: response.data.connected ? t('connectionSuccessful') : t('connectionFailed') });
    } catch (error: unknown) {
      setJackettTestResult({ success: false, message: `✗ ${t('error')}: ${getErrorMessage(error)}` });
    } finally {
      setJackettTesting(false);
    }
  };

  // Hydra handlers
  const toggleHydra = async (source: HydraConfiguration) => {
    const newState = !source.enabled;
    const prev = [...hydraSources];
    setHydraSources(hydraSources.map(s => s.id === source.id ? { ...s, enabled: newState } : s));
    try {
      await apiClient.put(`/hydra/${source.id}`, { ...source, enabled: newState });
    } catch {
      setHydraSources(prev);
      alert('Failed to update source status');
    }
  };

  const confirmDeleteHydra = async () => {
    if (!deleteConfirmation) return;
    try {
      await apiClient.delete(`/hydra/${deleteConfirmation.id}`);
      loadHydraSources();
      setDeleteConfirmation(null);
    } catch {
      alert('Error deleting source');
    }
  };

  return (
    <>
      <div className="settings-section" id="indexers">
        <div className="section-header-with-logo">
          <h3>INDEXERS</h3>
        </div>
        <p className="settings-description">
          Manage your indexers (Prowlarr, Jackett, and External JSON Sources).
        </p>

        <div className="clients-list">
          <div className={`client-card ${!prowlarrEnabled ? 'disabled' : ''}`}>
            <div className="client-info">
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                <img src={prowlarrLogo} alt="Prowlarr" style={{ height: '24px' }} />
                <h4>Prowlarr</h4>
              </div>
              <p>{prowlarrUrl}</p>
            </div>
            <div className="client-actions">
              <div className="checkbox-group" style={{ marginBottom: 0 }}>
                <label><input type="checkbox" checked={prowlarrEnabled} onChange={toggleProwlarr} /></label>
              </div>
              <button className="btn-edit" onClick={() => setShowProwlarrModal(true)}>{t('edit')}</button>
            </div>
          </div>

          <div className={`client-card ${!jackettEnabled ? 'disabled' : ''}`}>
            <div className="client-info">
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                <img src={jackettLogo} alt="Jackett" style={{ height: '24px' }} />
                <h4>Jackett</h4>
              </div>
              <p>{jackettUrl}</p>
            </div>
            <div className="client-actions">
              <div className="checkbox-group" style={{ marginBottom: 0 }}>
                <label><input type="checkbox" checked={jackettEnabled} onChange={toggleJackett} /></label>
              </div>
              <button className="btn-edit" onClick={() => setShowJackettModal(true)}>{t('edit')}</button>
            </div>
          </div>

          {hydraSources.map(source => (
            <div key={source.id} className={`client-card ${!source.enabled ? 'disabled' : ''}`}>
              <div className="client-info">
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                  <span className="category-badge" style={{ backgroundColor: 'rgba(250, 179, 135, 0.15)', color: 'var(--ctp-peach)', border: '1px solid var(--ctp-peach)', fontWeight: 'bold' }}>JSON</span>
                  <h4>{source.name}</h4>
                </div>
                <p style={{ maxWidth: '300px', overflow: 'hidden', textOverflow: 'ellipsis' }}>{source.url}</p>
              </div>
              <div className="client-actions">
                <button className="btn-square-action" onClick={() => setDeleteConfirmation({ id: source.id, name: source.name })} title={t('delete')} style={{ marginRight: 'auto' }}>
                  <FontAwesomeIcon icon={faTimes} />
                </button>
                <div className="checkbox-group" style={{ marginBottom: 0 }}>
                  <label><input type="checkbox" checked={source.enabled} onChange={() => toggleHydra(source)} /></label>
                </div>
                <button className="btn-edit" onClick={() => { setEditingHydraSource(source); setShowHydraModal(true); }}>{t('edit')}</button>
              </div>
            </div>
          ))}
        </div>

        <button className="btn-secondary" onClick={() => { setEditingHydraSource(null); setShowHydraModal(true); }} style={{ marginTop: '15px' }}>
          <FontAwesomeIcon icon={faPlus} /> Add JSON Source
        </button>
      </div>

      <HydraSourceModal
        isOpen={showHydraModal}
        onClose={() => setShowHydraModal(false)}
        onSave={loadHydraSources}
        source={editingHydraSource}
      />

      <Modal isOpen={showProwlarrModal} onClose={() => setShowProwlarrModal(false)} title="Configure Prowlarr">
        <form onSubmit={handleSaveProwlarr}>
          <div className="form-group">
            <label><input type="checkbox" checked={prowlarrEnabled} onChange={(e) => setProwlarrEnabled(e.target.checked)} /> Enable Prowlarr</label>
          </div>
          <div className="form-group">
            <label htmlFor="prowlarr-url">{t('prowlarrUrl')}</label>
            <input type="text" id="prowlarr-url" value={prowlarrUrl} onChange={(e) => setProwlarrUrl(e.target.value)} placeholder={t('prowlarrUrlPlaceholder')} required />
          </div>
          <div className="form-group">
            <label htmlFor="prowlarr-api">{t('prowlarrApiKey')}</label>
            <input type="password" id="prowlarr-api" value={prowlarrApiKey} onChange={(e) => setProwlarrApiKey(e.target.value)} placeholder={t('prowlarrApiKeyPlaceholder')} required />
          </div>
          {prowlarrTestResult && (
            <div className={`test-result ${prowlarrTestResult?.success ? 'success' : 'error'}`}>{prowlarrTestResult?.message}</div>
          )}
          <div className="modal-actions">
            <button type="button" className="btn-secondary" onClick={handleTestProwlarr} disabled={prowlarrTesting || !prowlarrUrl || !prowlarrApiKey}>
              {prowlarrTesting ? t('testing') : t('testConnection')}
            </button>
            <button type="submit" className="btn-primary">{t('save')}</button>
          </div>
        </form>
      </Modal>

      <Modal isOpen={showJackettModal} onClose={() => setShowJackettModal(false)} title="Configure Jackett">
        <form onSubmit={handleSaveJackett}>
          <div className="form-group">
            <label><input type="checkbox" checked={jackettEnabled} onChange={(e) => setJackettEnabled(e.target.checked)} /> Enable Jackett</label>
          </div>
          <div className="form-group">
            <label htmlFor="jackett-url">{t('jackettUrl')}</label>
            <input type="text" id="jackett-url" value={jackettUrl} onChange={(e) => setJackettUrl(e.target.value)} placeholder={t('jackettUrlPlaceholder')} required />
          </div>
          <div className="form-group">
            <label htmlFor="jackett-api">{t('jackettApiKey')}</label>
            <input type="password" id="jackett-api" value={jackettApiKey} onChange={(e) => setJackettApiKey(e.target.value)} placeholder={t('jackettApiKeyPlaceholder')} required />
          </div>
          {jackettTestResult && (
            <div className={`test-result ${jackettTestResult?.success ? 'success' : 'error'}`}>{jackettTestResult?.message}</div>
          )}
          <div className="modal-actions">
            <button type="button" className="btn-secondary" onClick={handleTestJackett} disabled={jackettTesting || !jackettUrl || !jackettApiKey}>
              {jackettTesting ? t('testing') : t('testConnection')}
            </button>
            <button type="submit" className="btn-primary">{t('save')}</button>
          </div>
        </form>
      </Modal>

      <ConfirmDialog
        isOpen={!!deleteConfirmation}
        onConfirm={confirmDeleteHydra}
        onCancel={() => setDeleteConfirmation(null)}
        title={t('deleteSource') || 'Delete Source'}
        message={`${t('confirmDeleteSource') || 'Are you sure you want to delete this source?'}: ${deleteConfirmation?.name || ''}`}
        confirmLabel={t('delete')}
        cancelLabel={t('cancel')}
        variant="danger"
      />
    </>
  );
};

export default IndexersTab;
