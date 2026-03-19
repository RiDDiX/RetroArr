import React, { useState, useEffect } from 'react';
import apiClient, { getErrorMessage } from '../../api/client';
import { Modal, ConfirmDialog } from '../ui';
import HydraSourceModal from '../HydraSourceModal';
import FolderExplorerModal from '../FolderExplorerModal';
import prowlarrLogo from '../../assets/prowlarr_logo.png';
import jackettLogo from '../../assets/jackett_logo.png';
import torrentNzbIcon from '../../assets/TORRENT_NZB_icon.png';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faPlus, faTimes } from '@fortawesome/free-solid-svg-icons';

interface DownloadClient {
  id?: number;
  name: string;
  implementation: string;
  host: string;
  port: number;
  username?: string;
  password?: string;
  category?: string;
  urlBase?: string;
  apiKey?: string;
  enable: boolean;
  useSsl?: boolean;
  priority: number;
  remotePathMapping?: string;
  localPathMapping?: string;
}

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

const IndexersTab: React.FC<IndexersTabProps> = ({ language, t }) => {
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

  // Hydra
  const [hydraSources, setHydraSources] = useState<HydraConfiguration[]>([]);
  const [showHydraModal, setShowHydraModal] = useState(false);
  const [editingHydraSource, setEditingHydraSource] = useState<HydraConfiguration | null>(null);
  const [deleteConfirmation, setDeleteConfirmation] = useState<{ id: number; name: string } | null>(null);

  // Download Clients
  const [downloadClients, setDownloadClients] = useState<DownloadClient[]>([]);
  const [showClientModal, setShowClientModal] = useState(false);
  const [editingClient, setEditingClient] = useState<DownloadClient | null>(null);
  const [clientForm, setClientForm] = useState<DownloadClient>({
    name: '', implementation: 'qBittorrent', host: 'localhost', port: 8080,
    username: 'admin', password: '', category: 'RetroArr', urlBase: '',
    apiKey: '', enable: true, useSsl: false, priority: 1
  });
  const [clientTesting, setClientTesting] = useState(false);
  const [clientTestResult, setClientTestResult] = useState<{ success: boolean; message: string; version?: string } | null>(null);
  const [showFolderExplorer, setShowFolderExplorer] = useState(false);

  useEffect(() => {
    loadSettings();
    loadHydraSources();
    loadDownloadClients();
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

  const loadDownloadClients = async () => {
    try {
      const response = await apiClient.get('/downloadclient');
      setDownloadClients(response.data);
    } catch (error) {
      console.error('Error loading download clients:', error);
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

  // Download Client handlers
  const resetClientForm = () => {
    setClientForm({
      name: '', implementation: 'qBittorrent', host: 'localhost', port: 8080,
      username: 'admin', password: '', category: 'RetroArr', urlBase: '',
      apiKey: '', enable: true, useSsl: false, priority: 1
    });
    setEditingClient(null);
    setClientTestResult(null);
  };

  const openAddClientModal = () => { resetClientForm(); setShowClientModal(true); };
  const openEditClientModal = (client: DownloadClient) => { setEditingClient(client); setClientForm({ ...client }); setShowClientModal(true); };

  const toggleDownloadClient = async (client: DownloadClient) => {
    const newState = !client.enable;
    const prev = [...downloadClients];
    setDownloadClients(downloadClients.map(c => c.id === client.id ? { ...c, enable: newState } : c));
    try {
      await apiClient.put(`/downloadclient/${client.id}`, { ...client, enable: newState });
    } catch {
      setDownloadClients(prev);
      alert('Failed to update client status');
    }
  };

  const handleDeleteClient = async (id: number) => {
    try {
      await apiClient.delete(`/downloadclient/${id}`);
      await loadDownloadClients();
    } catch {
      alert(t('failedToDeleteClient'));
    }
  };

  const handleTestDownloadClient = async () => {
    setClientTesting(true);
    setClientTestResult(null);
    try {
      const response = await apiClient.post('/downloadclient/test', {
        implementation: clientForm.implementation, host: clientForm.host, port: clientForm.port,
        username: clientForm.username, password: clientForm.password, urlBase: clientForm.urlBase, apiKey: clientForm.apiKey
      });
      setClientTestResult({ success: response.data.connected, message: response.data.message, version: response.data.version });
    } catch (error: unknown) {
      setClientTestResult({ success: false, message: `${t('error')}: ${getErrorMessage(error)}` });
    } finally {
      setClientTesting(false);
    }
  };

  const handleSaveDownloadClient = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    try {
      const payload = { ...clientForm };
      payload.port = parseInt(String(payload.port));
      payload.priority = parseInt(String(payload.priority));
      if (editingClient?.id) {
        await apiClient.put(`/downloadclient/${editingClient.id}`, payload);
      } else {
        await apiClient.post('/downloadclient', payload);
      }
      await loadDownloadClients();
      setShowClientModal(false);
      resetClientForm();
    } catch (error: unknown) {
      alert(`${t('failedToSaveClient')}: ${getErrorMessage(error, 'Unknown error')}`);
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
          {/* Prowlarr Card */}
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

          {/* Jackett Card */}
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

          {/* Hydra Sources */}
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

      <div className="settings-section" id="download-clients">
        <div className="section-header-with-logo">
          <img src={torrentNzbIcon} alt="Download Clients" style={{ height: '60px' }} />
        </div>
        <p className="settings-description">{t('downloadClientsDesc')}</p>

        {downloadClients.length > 0 && (
          <div className="clients-list">
            {downloadClients.map(client => (
              <div key={client.id} className={`client-card ${!client.enable ? 'disabled' : ''}`}>
                <div className="client-info">
                  <h4>{client.name}</h4>
                  <p>{client.implementation} - {client.host}:{client.port}</p>
                  {client.category && <span className="category-badge">{client.category}</span>}
                </div>
                <div className="client-actions">
                  <div className="checkbox-group" style={{ marginBottom: 0 }}>
                    <label><input type="checkbox" checked={client.enable} onChange={() => toggleDownloadClient(client)} /></label>
                  </div>
                  <button className="btn-edit" onClick={() => openEditClientModal(client)}>{t('edit')}</button>
                  <button className="btn-delete" onClick={() => handleDeleteClient(client.id!)}>{t('delete')}</button>
                </div>
              </div>
            ))}
          </div>
        )}

        <button className="btn-secondary" onClick={openAddClientModal}>{t('addClientButton')}</button>
      </div>

      {/* Download Client Modal */}
      <Modal
        isOpen={showClientModal}
        onClose={() => setShowClientModal(false)}
        title={editingClient ? t('editDownloadClient') : t('addDownloadClient')}
      >
        <form onSubmit={handleSaveDownloadClient}>
          <div className="form-group">
            <label><input type="checkbox" checked={clientForm.enable} onChange={(e) => setClientForm({ ...clientForm, enable: e.target.checked })} /> {t('enable')}</label>
          </div>
          <div className="form-group">
            <label>{t('name')}</label>
            <input type="text" className="form-control" value={clientForm.name} onChange={(e) => setClientForm({ ...clientForm, name: e.target.value })} placeholder="e.g. Deluge" />
          </div>
          <div className="form-group">
            <label>{t('implementation')}</label>
            <select className="form-control" value={clientForm.implementation} onChange={(e) => setClientForm({ ...clientForm, implementation: e.target.value })}>
              <option value="qBittorrent">qBittorrent</option>
              <option value="Transmission">Transmission</option>
              <option value="Deluge">Deluge (WebUI)</option>
              <option value="SABnzbd">SABnzbd</option>
              <option value="NZBGet">NZBGet</option>
            </select>
          </div>
          <div className="form-group">
            <label>{t('host')}</label>
            <input type="text" className="form-control" value={clientForm.host} onChange={(e) => setClientForm({ ...clientForm, host: e.target.value })} placeholder="localhost" />
          </div>
          <div className="form-group">
            <label>{t('port')}</label>
            <input type="number" className="form-control" value={clientForm.port} onChange={(e) => setClientForm({ ...clientForm, port: parseInt(e.target.value) })} />
          </div>

          {clientForm.implementation === 'Deluge' && (
            <div className="form-group">
              <label><input type="checkbox" checked={clientForm.useSsl || false} onChange={(e) => setClientForm({ ...clientForm, useSsl: e.target.checked })} /> Use SSL</label>
            </div>
          )}

          {(clientForm.implementation === 'qBittorrent' || clientForm.implementation === 'Deluge' || clientForm.implementation === 'Transmission' || clientForm.implementation === 'NZBGet') && (
            <>
              <div className="form-group">
                <label>{t('username')}</label>
                <input type="text" className="form-control" value={clientForm.username || ''} onChange={(e) => setClientForm({ ...clientForm, username: e.target.value })} placeholder={clientForm.implementation === 'Deluge' ? 'Optional (WebUI usually only needs pass)' : ''} />
              </div>
              <div className="form-group">
                <label>{t('password')}</label>
                <input type="password" className="form-control" value={clientForm.password || ''} onChange={(e) => setClientForm({ ...clientForm, password: e.target.value })} />
              </div>
            </>
          )}

          {clientForm.implementation === 'SABnzbd' && (
            <div className="form-group">
              <label>{t('apiKey')}</label>
              <input type="text" className="form-control" value={clientForm.apiKey || ''} onChange={(e) => setClientForm({ ...clientForm, apiKey: e.target.value })} />
            </div>
          )}

          <div className="form-group">
            <label>{t('category')}</label>
            <input type="text" className="form-control" value={clientForm.category || ''} onChange={(e) => setClientForm({ ...clientForm, category: e.target.value })} placeholder="RetroArr" />
            <small className="form-text text-muted">Optional, but recommended.</small>
          </div>

          {clientForm.implementation !== 'Deluge' && (
            <div className="form-group">
              <label>URL Base</label>
              <input type="text" className="form-control" value={clientForm.urlBase || ''} onChange={(e) => setClientForm({ ...clientForm, urlBase: e.target.value })} placeholder="e.g. /qbittorrent" />
            </div>
          )}

          <div className="form-group">
            <label>Priority</label>
            <select className="form-control" value={clientForm.priority} onChange={(e) => setClientForm({ ...clientForm, priority: parseInt(e.target.value) })}>
              <option value={1}>High (Primary)</option>
              <option value={50}>Last (Fallback)</option>
            </select>
          </div>

          <div className="form-group">
            <label>{t('remotePath')}</label>
            <input type="text" value={clientForm.remotePathMapping || ''} onChange={(e) => setClientForm({ ...clientForm, remotePathMapping: e.target.value })} placeholder="/downloads/" />
          </div>

          <div className="form-group">
            <label>{t('localPath')}</label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <input type="text" value={clientForm.localPathMapping || ''} onChange={(e) => setClientForm({ ...clientForm, localPathMapping: e.target.value })} placeholder="/Volumes/downloads/" style={{ flex: 1 }} />
              <button type="button" className="btn-secondary" onClick={() => setShowFolderExplorer(true)}>📂</button>
            </div>
          </div>

          {clientTestResult && (
            <div className={`test-result ${clientTestResult?.success === true ? 'success' : 'error'}`}>
              {clientTestResult?.message}
              {clientTestResult?.version && <div>{t('versionHeader')}: {clientTestResult.version}</div>}
            </div>
          )}
          <div className="modal-actions">
            <button type="button" className="btn-secondary" onClick={handleTestDownloadClient} disabled={clientTesting}>
              {clientTesting ? t('testing') : t('testConnection')}
            </button>
            <button type="button" className="btn-primary" onClick={() => handleSaveDownloadClient()}>
              {editingClient ? t('updateClient') : t('addClientButton')}
            </button>
          </div>
          <div className="button-group"></div>
        </form>
      </Modal>

      {/* Hydra Modal */}
      <HydraSourceModal
        isOpen={showHydraModal}
        onClose={() => setShowHydraModal(false)}
        onSave={loadHydraSources}
        source={editingHydraSource}
      />

      {/* Prowlarr Modal */}
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

      {/* Jackett Modal */}
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

      {/* Delete Hydra Confirm */}
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

      {showFolderExplorer && (
        <FolderExplorerModal
          initialPath={clientForm.localPathMapping || ''}
          onSelect={(path) => {
            setClientForm({ ...clientForm, localPathMapping: path });
            setShowFolderExplorer(false);
          }}
          onClose={() => setShowFolderExplorer(false)}
          language={language}
        />
      )}
    </>
  );
};

export default IndexersTab;
