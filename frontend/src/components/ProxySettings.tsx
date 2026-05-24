import React, { useState, useEffect } from 'react';
import { settingsApi, getErrorMessage, ProxyConfig } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faCheckCircle, faExclamationTriangle } from '@fortawesome/free-solid-svg-icons';
import { useTranslation } from '../i18n/translations';

const ProxySettings: React.FC = () => {
    const { t: translate } = useTranslation();
    const t = (key: string) => translate(key as Parameters<typeof translate>[0]) || key;

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
            setMessage({ type: 'success', text: response.data.message || t('proxySettingsSaved') });
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, t('proxyErrorSaving')) });
        } finally {
            setSaving(false);
        }
    };

    if (loading) {
        return <div className="settings-section">{t('loading')}</div>;
    }

    return (
        <div className="settings-section" id="proxy">
            <h3>{t('proxyTitle')}</h3>
            <p className="settings-description">{t('proxyDesc')}</p>

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
                    {' '}{t('proxyEnable')}
                </label>
            </div>

            <div className="form-group">
                <label>{t('proxyType')}</label>
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
                <label>{t('host')}</label>
                <input
                    type="text"
                    value={config.host}
                    onChange={(e) => setConfig({ ...config, host: e.target.value })}
                    placeholder="127.0.0.1"
                    disabled={!config.enabled}
                />
            </div>

            <div className="form-group">
                <label>{t('port')}</label>
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
                <label>{t('proxyUsernameOptional')}</label>
                <input
                    type="text"
                    value={config.username}
                    onChange={(e) => setConfig({ ...config, username: e.target.value })}
                    disabled={!config.enabled}
                />
            </div>

            <div className="form-group">
                <label>{t('proxyPasswordOptional')}</label>
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
                    {' '}{t('proxyBypassLocal')}
                </label>
            </div>

            <div className="form-group">
                <label>{t('proxyBypassList')}</label>
                <input
                    type="text"
                    value={config.bypassList.join(', ')}
                    onChange={(e) => setConfig({
                        ...config,
                        bypassList: e.target.value.split(',').map(s => s.trim()).filter(s => s.length > 0),
                    })}
                    placeholder={t('proxyBypassPlaceholder')}
                    disabled={!config.enabled}
                />
            </div>

            <button type="button" className="btn-primary" onClick={handleSave} disabled={saving}>
                {saving ? t('saving') : t('save')}
            </button>
        </div>
    );
};

export default ProxySettings;
