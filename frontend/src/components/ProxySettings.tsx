import React, { useState, useEffect } from 'react';
import { settingsApi, getErrorMessage, ProxyConfig } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faCheckCircle, faExclamationTriangle } from '@fortawesome/free-solid-svg-icons';

const ProxySettings: React.FC = () => {
    const [config, setConfig] = useState<ProxyConfig>({
        enabled: false,
        type: 'http',
        host: '',
        port: 8080,
        username: '',
        password: '',
        bypassLocal: true,
        bypassList: [],
    });
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

    useEffect(() => {
        loadSettings();
    }, []);

    const loadSettings = async () => {
        try {
            const response = await settingsApi.getProxy();
            setConfig(response.data);
        } catch (error) {
            console.error('Error loading proxy settings:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleSave = async () => {
        setSaving(true);
        setMessage(null);
        try {
            const response = await settingsApi.saveProxy(config);
            setMessage({ type: 'success', text: response.data.message || 'Proxy settings saved.' });
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Error saving proxy settings') });
        } finally {
            setSaving(false);
        }
    };

    if (loading) {
        return <div className="settings-section">Loading...</div>;
    }

    return (
        <div className="settings-section" id="proxy">
            <h3>Proxy</h3>
            <p className="settings-description">
                Route all outgoing connections (metadata, indexers, downloaders) through an
                HTTP or SOCKS5 proxy. Useful for isolated hosts or a NordVPN/squid gateway.
            </p>

            {message && (
                <div className={`message ${message.type}`}>
                    <FontAwesomeIcon icon={message.type === 'success' ? faCheckCircle : faExclamationTriangle} />
                    {' '}{message.text}
                </div>
            )}

            <div className="form-group">
                <label>
                    <input
                        type="checkbox"
                        checked={config.enabled}
                        onChange={(e) => setConfig({ ...config, enabled: e.target.checked })}
                    />
                    {' '}Enable proxy
                </label>
            </div>

            <div className="form-group">
                <label>Type</label>
                <select
                    value={config.type}
                    onChange={(e) => setConfig({ ...config, type: e.target.value })}
                    disabled={!config.enabled}
                >
                    <option value="http">HTTP</option>
                    <option value="socks5">SOCKS5</option>
                </select>
            </div>

            <div className="form-group">
                <label>Host</label>
                <input
                    type="text"
                    value={config.host}
                    onChange={(e) => setConfig({ ...config, host: e.target.value })}
                    placeholder="127.0.0.1"
                    disabled={!config.enabled}
                />
            </div>

            <div className="form-group">
                <label>Port</label>
                <input
                    type="number"
                    value={config.port}
                    onChange={(e) => setConfig({ ...config, port: parseInt(e.target.value) || 0 })}
                    min={1}
                    max={65535}
                    disabled={!config.enabled}
                />
            </div>

            <div className="form-group">
                <label>Username (optional)</label>
                <input
                    type="text"
                    value={config.username}
                    onChange={(e) => setConfig({ ...config, username: e.target.value })}
                    disabled={!config.enabled}
                />
            </div>

            <div className="form-group">
                <label>Password (optional)</label>
                <input
                    type="password"
                    value={config.password}
                    onChange={(e) => setConfig({ ...config, password: e.target.value })}
                    disabled={!config.enabled}
                />
            </div>

            <div className="form-group">
                <label>
                    <input
                        type="checkbox"
                        checked={config.bypassLocal}
                        onChange={(e) => setConfig({ ...config, bypassLocal: e.target.checked })}
                        disabled={!config.enabled}
                    />
                    {' '}Bypass proxy for local addresses
                </label>
            </div>

            <div className="form-group">
                <label>Bypass list (comma separated host fragments)</label>
                <input
                    type="text"
                    value={config.bypassList.join(', ')}
                    onChange={(e) => setConfig({
                        ...config,
                        bypassList: e.target.value.split(',').map(s => s.trim()).filter(s => s.length > 0),
                    })}
                    placeholder="example.com, 10.0.0"
                    disabled={!config.enabled}
                />
            </div>

            <button type="button" className="btn-primary" onClick={handleSave} disabled={saving}>
                {saving ? 'Saving...' : 'Save'}
            </button>
        </div>
    );
};

export default ProxySettings;
