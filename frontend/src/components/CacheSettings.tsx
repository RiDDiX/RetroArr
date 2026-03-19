import React, { useState, useEffect } from 'react';
import { settingsApi, getErrorMessage, CacheConfig } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faBolt, faPlug, faTrash, faCheckCircle, faExclamationTriangle } from '@fortawesome/free-solid-svg-icons';
import './CacheSettings.css';

const CacheSettings: React.FC = () => {
    const [config, setConfig] = useState<CacheConfig>({
        enabled: false,
        connectionString: 'localhost:6379',
        libraryListTtlSeconds: 60,
        gameDetailTtlSeconds: 120,
        metadataTtlSeconds: 3600,
        downloadStatusTtlSeconds: 30,
        dbStatsTtlSeconds: 300
    });
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [testing, setTesting] = useState(false);
    const [clearing, setClearing] = useState(false);
    const [message, setMessage] = useState<{ type: 'success' | 'error' | 'info'; text: string } | null>(null);

    useEffect(() => {
        loadSettings();
    }, []);

    const loadSettings = async () => {
        try {
            const response = await settingsApi.getCache();
            setConfig(response.data);
        } catch (error) {
            console.error('Error loading cache settings:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleSave = async () => {
        setSaving(true);
        setMessage(null);
        try {
            const response = await settingsApi.saveCache(config);
            setMessage({ type: 'success', text: response.data.message || 'Cache settings saved.' });
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Error saving cache settings') });
        } finally {
            setSaving(false);
        }
    };

    const handleTest = async () => {
        setTesting(true);
        setMessage(null);
        try {
            const response = await settingsApi.testCache({ connectionString: config.connectionString });
            if (response.data.success) {
                setMessage({ type: 'success', text: response.data.message });
            } else {
                setMessage({ type: 'error', text: response.data.message });
            }
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Connection test failed') });
        } finally {
            setTesting(false);
        }
    };

    const handleClear = async () => {
        setClearing(true);
        setMessage(null);
        try {
            const response = await settingsApi.clearCache();
            setMessage({ type: 'success', text: response.data.message || 'Cache cleared.' });
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Error clearing cache') });
        } finally {
            setClearing(false);
        }
    };

    if (loading) {
        return <div className="cache-settings loading">Loading...</div>;
    }

    return (
        <div className="cache-settings">
            <div className="settings-header">
                <h3><FontAwesomeIcon icon={faBolt} /> Cache Settings (Redis)</h3>
                <p className="settings-description">
                    Optional Redis cache layer to speed up library reads and metadata queries.
                    When disabled, all requests go directly to the database.
                </p>
            </div>

            {message && (
                <div className={`message ${message.type}`}>
                    <FontAwesomeIcon icon={message.type === 'success' ? faCheckCircle : faExclamationTriangle} />
                    {message.text}
                </div>
            )}

            <div className="cache-status">
                <span className={`status-dot ${config.isConnected ? 'connected' : 'disconnected'}`} />
                <span>{config.isConnected ? 'Redis Connected' : 'Redis Not Connected'}</span>
            </div>

            <div className="settings-form">
                <div className="form-group checkbox">
                    <label>
                        <input
                            type="checkbox"
                            checked={config.enabled}
                            onChange={(e) => setConfig({ ...config, enabled: e.target.checked })}
                        />
                        Enable Redis Cache
                    </label>
                </div>

                <div className="form-group">
                    <label>Connection String</label>
                    <input
                        type="text"
                        value={config.connectionString}
                        onChange={(e) => setConfig({ ...config, connectionString: e.target.value })}
                        placeholder="localhost:6379"
                        disabled={!config.enabled}
                    />
                    <span className="hint">Format: host:port (e.g. localhost:6379, redis:6379)</span>
                </div>

                {config.enabled && (
                    <div className="ttl-section">
                        <h4>Cache TTL (Time-to-Live)</h4>
                        <div className="ttl-grid">
                            <div className="form-group">
                                <label>Library List (s)</label>
                                <input
                                    type="number"
                                    value={config.libraryListTtlSeconds}
                                    onChange={(e) => setConfig({ ...config, libraryListTtlSeconds: parseInt(e.target.value) || 60 })}
                                    min={10}
                                    max={600}
                                />
                            </div>
                            <div className="form-group">
                                <label>Game Detail (s)</label>
                                <input
                                    type="number"
                                    value={config.gameDetailTtlSeconds}
                                    onChange={(e) => setConfig({ ...config, gameDetailTtlSeconds: parseInt(e.target.value) || 120 })}
                                    min={10}
                                    max={600}
                                />
                            </div>
                            <div className="form-group">
                                <label>Metadata (s)</label>
                                <input
                                    type="number"
                                    value={config.metadataTtlSeconds}
                                    onChange={(e) => setConfig({ ...config, metadataTtlSeconds: parseInt(e.target.value) || 3600 })}
                                    min={60}
                                    max={86400}
                                />
                            </div>
                            <div className="form-group">
                                <label>Download Status (s)</label>
                                <input
                                    type="number"
                                    value={config.downloadStatusTtlSeconds}
                                    onChange={(e) => setConfig({ ...config, downloadStatusTtlSeconds: parseInt(e.target.value) || 30 })}
                                    min={5}
                                    max={300}
                                />
                            </div>
                            <div className="form-group">
                                <label>DB Stats (s)</label>
                                <input
                                    type="number"
                                    value={config.dbStatsTtlSeconds}
                                    onChange={(e) => setConfig({ ...config, dbStatsTtlSeconds: parseInt(e.target.value) || 300 })}
                                    min={30}
                                    max={3600}
                                />
                            </div>
                        </div>
                    </div>
                )}
            </div>

            <div className="settings-actions">
                {config.enabled && (
                    <button className="btn-test" onClick={handleTest} disabled={testing}>
                        <FontAwesomeIcon icon={faPlug} />
                        {testing ? 'Testing...' : 'Test Connection'}
                    </button>
                )}

                <button className="btn-save" onClick={handleSave} disabled={saving}>
                    {saving ? 'Saving...' : 'Save'}
                </button>

                {config.isConnected && (
                    <button className="btn-clear" onClick={handleClear} disabled={clearing}>
                        <FontAwesomeIcon icon={faTrash} />
                        {clearing ? 'Clearing...' : 'Clear Cache'}
                    </button>
                )}
            </div>

            <div className="restart-notice">
                <FontAwesomeIcon icon={faExclamationTriangle} />
                Enabling or changing the Redis connection requires a restart.
            </div>
        </div>
    );
};

export default CacheSettings;
