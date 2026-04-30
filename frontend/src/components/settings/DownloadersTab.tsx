import React, { useState, useEffect } from 'react';
import apiClient, { getErrorMessage } from '../../api/client';
import { Modal } from '../ui';
import FolderExplorerModal from '../FolderExplorerModal';
import torrentNzbIcon from '../../assets/TORRENT_NZB_icon.png';

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

interface DownloadersTabProps {
  language: string;
  t: (key: string) => string;
}

const DownloadersTab: React.FC<DownloadersTabProps> = ({ language, t }) => {
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
    loadDownloadClients();
  }, []);

  const loadDownloadClients = async () => {
    try {
      const response = await apiClient.get('/downloadclient');
      setDownloadClients(response.data);
    } catch (error) {
      console.error('Error loading download clients:', error);
    }
  };

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
      <div className="settings-section" id="downloaders">
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

export default DownloadersTab;
