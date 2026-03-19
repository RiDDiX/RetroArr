import React, { useState, useEffect } from 'react';
import apiClient, { getErrorMessage } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faDownload, faSync, faTrash, faCheckCircle, faTimesCircle, faGamepad, faBug, faChevronDown, faChevronUp } from '@fortawesome/free-solid-svg-icons';
import { Language } from '../i18n/translations';

interface EmulatorJsSettingsProps {
    language: Language;
}

interface EmulatorStatus {
    installed: boolean;
    version: string | null;
    path: string;
    assetsUrl: string;
}

interface UpdateInfo {
    currentVersion: string | null;
    latestVersion: string | null;
    updateAvailable: boolean;
}

const EmulatorJsSettings: React.FC<EmulatorJsSettingsProps> = () => {
    const [status, setStatus] = useState<EmulatorStatus | null>(null);
    const [updateInfo, setUpdateInfo] = useState<UpdateInfo | null>(null);
    const [loading, setLoading] = useState(true);
    const [installing, setInstalling] = useState(false);
    const [checkingUpdate, setCheckingUpdate] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [success, setSuccess] = useState<string | null>(null);
    const [debugEnabled, setDebugEnabled] = useState(false);
    const [showCoresDetails, setShowCoresDetails] = useState(false);

    useEffect(() => {
        loadStatus();
    }, []);

    const loadStatus = async () => {
        try {
            setLoading(true);
            const response = await apiClient.get('/emulator/status');
            setStatus(response.data);
        } catch (err: unknown) {
            console.error('Error loading EmulatorJS status:', err);
        } finally {
            setLoading(false);
        }
    };

    const checkForUpdates = async () => {
        try {
            setCheckingUpdate(true);
            setError(null);
            const response = await apiClient.get('/emulator/check-update');
            setUpdateInfo(response.data);
        } catch (err: unknown) {
            setError(`Failed to check for updates: ${getErrorMessage(err)}`);
        } finally {
            setCheckingUpdate(false);
        }
    };

    const installEmulatorJs = async () => {
        try {
            setInstalling(true);
            setError(null);
            setSuccess(null);
            const response = await apiClient.post('/emulator/install');
            setSuccess(response.data.message);
            await loadStatus();
            setUpdateInfo(null);
        } catch (err: unknown) {
            setError(`Failed to install EmulatorJS: ${getErrorMessage(err)}`);
        } finally {
            setInstalling(false);
        }
    };

    const uninstallEmulatorJs = async () => {
        if (!window.confirm('Are you sure you want to uninstall EmulatorJS? This will remove all emulator files.')) {
            return;
        }
        
        try {
            setInstalling(true);
            setError(null);
            setSuccess(null);
            const response = await apiClient.delete('/emulator/uninstall');
            setSuccess(response.data.message);
            await loadStatus();
            setUpdateInfo(null);
        } catch (err: unknown) {
            setError(`Failed to uninstall EmulatorJS: ${getErrorMessage(err)}`);

        } finally {
            setInstalling(false);
        }
    };

    if (loading) {
        return (
            <div className="settings-section">
                <div className="section-header-with-logo">
                    <FontAwesomeIcon icon={faGamepad} style={{ fontSize: '24px', marginRight: '10px' }} />
                    <h3>EmulatorJS</h3>
                </div>
                <p>Loading...</p>
            </div>
        );
    }

    return (
        <div className="settings-section">
            <div className="section-header-with-logo">
                <FontAwesomeIcon icon={faGamepad} style={{ fontSize: '24px', marginRight: '10px', color: 'var(--ctp-blue)' }} />
                <h3>EmulatorJS</h3>
            </div>
            <p className="settings-description">
                EmulatorJS enables web-based emulation for retro gaming platforms directly in your browser.
                Install EmulatorJS locally to avoid CDN issues and enable SharedArrayBuffer support.
            </p>

            {error && (
                <div className="alert alert-danger" style={{ 
                    padding: '10px', 
                    marginBottom: '15px', 
                    backgroundColor: 'var(--ctp-red)', 
                    color: 'var(--ctp-base)',
                    borderRadius: '8px'
                }}>
                    <FontAwesomeIcon icon={faTimesCircle} /> {error}
                </div>
            )}

            {success && (
                <div className="alert alert-success" style={{ 
                    padding: '10px', 
                    marginBottom: '15px', 
                    backgroundColor: 'var(--ctp-green)', 
                    color: 'var(--ctp-base)',
                    borderRadius: '8px'
                }}>
                    <FontAwesomeIcon icon={faCheckCircle} /> {success}
                </div>
            )}

            <div className="form-group" style={{ marginBottom: '20px' }}>
                <label style={{ fontWeight: 'bold', marginBottom: '10px', display: 'block' }}>Status</label>
                <div style={{ 
                    display: 'flex', 
                    alignItems: 'center', 
                    gap: '10px',
                    padding: '15px',
                    backgroundColor: 'var(--surface0, var(--ctp-surface0))',
                    borderRadius: '8px'
                }}>
                    <FontAwesomeIcon 
                        icon={status?.installed ? faCheckCircle : faTimesCircle} 
                        style={{ 
                            color: status?.installed ? 'var(--ctp-green)' : 'var(--ctp-red)',
                            fontSize: '20px'
                        }} 
                    />
                    <span>
                        {status?.installed 
                            ? `Installed (Version: ${status.version || 'Unknown'})` 
                            : 'Not installed'}
                    </span>
                </div>
            </div>

            {status?.installed && (
                <div className="form-group" style={{ marginBottom: '20px' }}>
                    <label style={{ fontWeight: 'bold', marginBottom: '10px', display: 'block' }}>Installation Path</label>
                    <code style={{ 
                        display: 'block',
                        padding: '10px',
                        backgroundColor: 'var(--surface0, var(--ctp-surface0))',
                        borderRadius: '8px',
                        fontSize: '12px'
                    }}>
                        {status.path}
                    </code>
                </div>
            )}

            {updateInfo && (
                <div className="form-group" style={{ marginBottom: '20px' }}>
                    <label style={{ fontWeight: 'bold', marginBottom: '10px', display: 'block' }}>Version Info</label>
                    <div style={{ 
                        padding: '15px',
                        backgroundColor: 'var(--surface0, var(--ctp-surface0))',
                        borderRadius: '8px'
                    }}>
                        <p style={{ margin: '0 0 5px 0' }}>Current: {updateInfo.currentVersion || 'Not installed'}</p>
                        <p style={{ margin: '0 0 5px 0' }}>Latest: {updateInfo.latestVersion}</p>
                        {updateInfo.updateAvailable && (
                            <p style={{ margin: '10px 0 0 0', color: 'var(--ctp-yellow)' }}>
                                <FontAwesomeIcon icon={faSync} /> Update available!
                            </p>
                        )}
                    </div>
                </div>
            )}

            <div className="form-group" style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
                {!status?.installed ? (
                    <button
                        className="btn-primary"
                        onClick={installEmulatorJs}
                        disabled={installing}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            padding: '10px 20px',
                            backgroundColor: 'var(--ctp-green)',
                            color: 'var(--ctp-base)',
                            border: 'none',
                            borderRadius: '8px',
                            cursor: installing ? 'wait' : 'pointer',
                            fontWeight: 'bold'
                        }}
                    >
                        <FontAwesomeIcon icon={faDownload} spin={installing} />
                        {installing ? 'Installing...' : 'Install EmulatorJS'}
                    </button>
                ) : (
                    <>
                        <button
                            className="btn-secondary"
                            onClick={checkForUpdates}
                            disabled={checkingUpdate || installing}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                padding: '10px 20px',
                                backgroundColor: 'var(--ctp-blue)',
                                color: 'var(--ctp-base)',
                                border: 'none',
                                borderRadius: '8px',
                                cursor: (checkingUpdate || installing) ? 'wait' : 'pointer',
                                fontWeight: 'bold'
                            }}
                        >
                            <FontAwesomeIcon icon={faSync} spin={checkingUpdate} />
                            {checkingUpdate ? 'Checking...' : 'Check for Updates'}
                        </button>

                        {updateInfo?.updateAvailable && (
                            <button
                                className="btn-primary"
                                onClick={installEmulatorJs}
                                disabled={installing}
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '8px',
                                    padding: '10px 20px',
                                    backgroundColor: 'var(--ctp-green)',
                                    color: 'var(--ctp-base)',
                                    border: 'none',
                                    borderRadius: '8px',
                                    cursor: installing ? 'wait' : 'pointer',
                                    fontWeight: 'bold'
                                }}
                            >
                                <FontAwesomeIcon icon={faDownload} spin={installing} />
                                {installing ? 'Updating...' : 'Update EmulatorJS'}
                            </button>
                        )}

                        <button
                            className="btn-danger"
                            onClick={uninstallEmulatorJs}
                            disabled={installing}
                            style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: '8px',
                                padding: '10px 20px',
                                backgroundColor: 'var(--ctp-red)',
                                color: 'var(--ctp-base)',
                                border: 'none',
                                borderRadius: '8px',
                                cursor: installing ? 'wait' : 'pointer',
                                fontWeight: 'bold'
                            }}
                        >
                            <FontAwesomeIcon icon={faTrash} />
                            Uninstall
                        </button>
                    </>
                )}
            </div>

            {/* Debug Settings */}
            <div style={{ marginTop: '30px', padding: '15px', backgroundColor: 'var(--surface0, var(--ctp-surface0))', borderRadius: '8px' }}>
                <h4 style={{ margin: '0 0 15px 0', display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <FontAwesomeIcon icon={faBug} style={{ color: 'var(--ctp-yellow)' }} />
                    Debug Settings
                </h4>
                <label style={{ display: 'flex', alignItems: 'center', gap: '10px', cursor: 'pointer' }}>
                    <input
                        type="checkbox"
                        checked={debugEnabled}
                        onChange={(e) => {
                            setDebugEnabled(e.target.checked);
                            localStorage.setItem('emulatorjs_debug', e.target.checked ? 'true' : 'false');
                        }}
                        style={{ width: '18px', height: '18px' }}
                    />
                    <span style={{ color: 'var(--ctp-text)' }}>Enable EmulatorJS Debug Mode</span>
                </label>
                <p style={{ margin: '10px 0 0 28px', fontSize: '12px', color: 'var(--subtext0, var(--ctp-subtext0))' }}>
                    Shows debug information in browser console when running emulator.
                </p>
            </div>

            {/* Supported Platforms & Cores */}
            <div style={{ marginTop: '20px', padding: '15px', backgroundColor: 'var(--surface0, var(--ctp-surface0))', borderRadius: '8px' }}>
                <button
                    onClick={() => setShowCoresDetails(!showCoresDetails)}
                    style={{
                        background: 'none',
                        border: 'none',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '10px',
                        width: '100%',
                        padding: 0,
                        color: 'var(--ctp-text)'
                    }}
                >
                    <FontAwesomeIcon icon={showCoresDetails ? faChevronUp : faChevronDown} />
                    <h4 style={{ margin: 0 }}>Supported Platforms & Cores</h4>
                </button>
                
                {showCoresDetails && (
                    <div style={{ marginTop: '15px' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
                            <thead>
                                <tr style={{ borderBottom: '1px solid var(--ctp-surface1)' }}>
                                    <th style={{ textAlign: 'left', padding: '8px', color: 'var(--ctp-subtext0)' }}>Platform</th>
                                    <th style={{ textAlign: 'left', padding: '8px', color: 'var(--ctp-subtext0)' }}>Core</th>
                                    <th style={{ textAlign: 'left', padding: '8px', color: 'var(--ctp-subtext0)' }}>Extensions</th>
                                </tr>
                            </thead>
                            <tbody>
                                {[
                                    { platform: 'Nintendo Entertainment System', core: 'fceumm / nestopia', ext: '.nes, .fds' },
                                    { platform: 'Super Nintendo', core: 'snes9x', ext: '.sfc, .smc' },
                                    { platform: 'Nintendo 64', core: 'mupen64plus_next', ext: '.n64, .z64, .v64' },
                                    { platform: 'Game Boy', core: 'gambatte', ext: '.gb' },
                                    { platform: 'Game Boy Color', core: 'gambatte', ext: '.gbc' },
                                    { platform: 'Game Boy Advance', core: 'mgba', ext: '.gba' },
                                    { platform: 'Nintendo DS', core: 'desmume / melonds', ext: '.nds' },
                                    { platform: 'Sega Master System', core: 'genesis_plus_gx', ext: '.sms' },
                                    { platform: 'Sega Mega Drive / Genesis', core: 'genesis_plus_gx', ext: '.md, .gen, .bin' },
                                    { platform: 'Sega Game Gear', core: 'genesis_plus_gx', ext: '.gg' },
                                    { platform: 'Sega Saturn', core: 'yabause', ext: '.iso, .cue, .bin' },
                                    { platform: 'Sega 32X', core: 'picodrive', ext: '.32x' },
                                    { platform: 'Sega CD', core: 'genesis_plus_gx', ext: '.iso, .cue, .bin' },
                                    { platform: 'PlayStation', core: 'pcsx_rearmed', ext: '.bin, .cue, .iso, .pbp' },
                                    { platform: 'PlayStation Portable', core: 'ppsspp', ext: '.iso, .cso' },
                                    { platform: 'Atari 2600', core: 'stella2014', ext: '.a26' },
                                    { platform: 'Atari 7800', core: 'prosystem', ext: '.a78' },
                                    { platform: 'Atari Lynx', core: 'handy', ext: '.lnx' },
                                    { platform: 'Atari Jaguar', core: 'virtualjaguar', ext: '.j64, .jag' },
                                    { platform: 'PC Engine / TurboGrafx-16', core: 'mednafen_pce', ext: '.pce, .cue' },
                                    { platform: '3DO', core: 'opera', ext: '.iso, .cue' },
                                    { platform: 'Arcade (MAME)', core: 'mame2003', ext: '.zip' },
                                    { platform: 'Neo Geo', core: 'fbalpha2012_neogeo', ext: '.zip' },
                                    { platform: 'WonderSwan / Color', core: 'mednafen_wswan', ext: '.ws, .wsc' },
                                    { platform: 'Virtual Boy', core: 'mednafen_vb', ext: '.vb' },
                                ].map((item, idx) => (
                                    <tr key={idx} style={{ borderBottom: '1px solid var(--ctp-surface0)' }}>
                                        <td style={{ padding: '8px', color: 'var(--ctp-text)' }}>{item.platform}</td>
                                        <td style={{ padding: '8px', color: 'var(--ctp-blue)', fontFamily: 'monospace' }}>{item.core}</td>
                                        <td style={{ padding: '8px', color: 'var(--ctp-subtext0)', fontFamily: 'monospace' }}>{item.ext}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                        <p style={{ margin: '15px 0 0 0', fontSize: '12px', color: 'var(--ctp-subtext0)' }}>
                            Note: Some cores require BIOS files. Check the EmulatorJS documentation for details.
                        </p>
                    </div>
                )}
            </div>
        </div>
    );
};

export default EmulatorJsSettings;
