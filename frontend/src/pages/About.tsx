import React from 'react';
import { t } from '../i18n/translations';
import './Settings.css';
import appLogo from '../assets/app_logo.png';
import packageJson from '../../../package.json';

const About: React.FC = () => {
    return (
        <div className="settings">
            <div className="settings-section">
                <div style={{ textAlign: 'center', marginBottom: '1.25rem' }}>
                    <div style={{ display: 'inline-block' }}>
                        <img src={appLogo} alt="RetroArr" style={{ width: '100px', height: 'auto', marginBottom: '0.75rem' }} />
                    </div>
                    <h3>RetroArr v{packageJson.version}</h3>
                    <p style={{ opacity: 0.7, fontSize: '0.9rem', marginTop: '0.5rem' }}>A PVR for Video Games</p>
                </div>

                <div className="settings-section" style={{ border: 'none', padding: 0, backgroundColor: 'transparent' }}>

                    <p className="settings-description" style={{ fontSize: '0.95rem', lineHeight: '1.5', marginBottom: '1.25rem' }}>
                        {t('aboutMainDesc')}
                    </p>

                    <div style={{ marginBottom: '1.25rem' }}>
                        <h4 style={{ color: 'var(--ctp-text)', fontSize: '1rem', marginBottom: '0.75rem', fontWeight: 600 }}>{t('featuresTitle')}</h4>
                        <ul style={{ listStyleType: 'none', padding: 0, color: 'var(--ctp-subtext0)', lineHeight: '1.6', fontSize: '0.9rem' }}>
                            <li>• {t('featureScanning')}</li>
                            <li>• {t('featureMetadata')}</li>
                            <li>• {t('featureDownloadClients')}</li>
                            <li>• {t('featureIndexers')}</li>
                            <li>• {t('featureDownloadTracking')}</li>
                            <li>• {t('featureCrossPlatform')}</li>
                        </ul>
                    </div>

                    <div style={{ marginBottom: '1.25rem' }}>
                        <h4 style={{ color: 'var(--ctp-text)', fontSize: '1rem', marginBottom: '0.75rem', fontWeight: 600 }}>{t('roadmapTitle')}</h4>
                        <ul style={{ listStyleType: 'none', padding: 0, color: 'var(--ctp-subtext0)', lineHeight: '1.6', fontSize: '0.9rem' }}>
                            <li>• {t('roadmapAppStores')}</li>
                            <li>• {t('roadmapExtensibility')}</li>
                            <li>• {t('roadmapLinuxGaming')}</li>
                        </ul>
                    </div>

                    <div style={{ borderTop: '1px solid var(--ctp-surface0)', paddingTop: '1.25rem', marginTop: '1.5rem' }}>
                        <p className="settings-description" style={{ fontSize: '0.8rem', opacity: 0.5, marginBottom: 0 }}>
                            {t('license')}
                        </p>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default About;
