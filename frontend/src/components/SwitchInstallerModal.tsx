import React, { useState, useEffect } from 'react';
import apiClient, { getErrorMessage } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faDownload, faCheckCircle, faSpinner, faExclamationTriangle, faInfoCircle } from '@fortawesome/free-solid-svg-icons';
import { Modal } from './ui';
import '../pages/Settings.css';

interface SwitchInstallerModalProps {
    isOpen: boolean;
    onClose: () => void;
    filePath: string;
    fileName: string;
}

const SwitchInstallerModal: React.FC<SwitchInstallerModalProps> = ({ isOpen, onClose, filePath, fileName }) => {
    const [step, setStep] = useState<'scan' | 'confirm' | 'installing' | 'finished' | 'aborted' | 'error'>('scan');
    const [, setIsInstalling] = useState(false);
    const [devices, setDevices] = useState<string[]>([]);
    const [selectedDevice, setSelectedDevice] = useState<string>('');
    const [progress, setProgress] = useState(0);
    const [log, setLog] = useState<string>('');

    useEffect(() => {
        if (isOpen) {
            setStep('scan');
            setDevices([]);
            setLog('');
            setIsInstalling(false);
            scanDevices();
        }
    }, [isOpen]);

    const handleClose = async () => {
        onClose();
    };

    const scanDevices = async () => {
        try {
            setLog('Scanning for USB devices...');
            const res = await apiClient.get('/nsw/devices');
            const foundDevices = res.data;
            setDevices(foundDevices);
            if (foundDevices.length > 0) {
                setLog(`Found ${foundDevices.length} device(s).`);
                setSelectedDevice(foundDevices[0]);
                setStep('confirm');
            } else {
                setLog('No Nintendo Switch detected defined by VID 057E.');
            }
        } catch (err: unknown) {
            setLog(`Error scanning: ${getErrorMessage(err)}`);
        }
    };

    const startInstall = async () => {
        if (!selectedDevice) return;
        setStep('installing');
        setIsInstalling(true);
        setProgress(0);
        setLog('Starting handshake...');

        try {
            // Trigger install
            await apiClient.post('/nsw/install', {
                filePath,
                deviceId: selectedDevice
            });

            // Poll real progress from backend
            const interval = setInterval(async () => {
                try {
                    const statusRes = await apiClient.get('/nsw/progress');
                    const { progress: currentP, status: currentS } = statusRes.data;

                    setProgress(Math.round(currentP));
                    setLog(currentS);

                    if (currentS === 'Installation Complete' || currentP >= 100) {
                        clearInterval(interval);
                        setStep('finished');
                        setIsInstalling(false);
                    } else if (currentS === 'Installation Aborted by Console') {
                        clearInterval(interval);
                        setStep('aborted');
                        setLog('Installation was cancelled from the Switch.');
                        setIsInstalling(false);
                        // Auto close after 2 seconds
                        setTimeout(() => {
                            onClose();
                        }, 2000);
                    } else if (currentS.startsWith('Error')) {
                        clearInterval(interval);
                        setStep('error');
                        setLog(currentS);
                        setIsInstalling(false);
                    }
                } catch (pollErr: unknown) {
                    console.error('Progress polling error:', pollErr);
                }
            }, 1000);

        } catch (err: unknown) {
            setStep('error');
            setLog('Installation failed: ' + getErrorMessage(err));
            setIsInstalling(false);
        }
    };

    return (
        <Modal isOpen={isOpen} onClose={handleClose} title="Install to Switch" maxWidth="500px">
            {step === 'scan' && (
                <div style={{ textAlign: 'center', padding: '20px' }}>
                    <FontAwesomeIcon icon={faSpinner} spin size="3x" />
                    <p style={{ marginTop: '15px' }}>Scanning for Nintendo Switch via USB...</p>
                    <small>{log}</small>
                    <button className="btn-secondary" onClick={scanDevices} style={{ marginTop: '10px' }}>Retry</button>
                </div>
            )}

            {step === 'confirm' && (
                <div>
                    <p><strong>File:</strong> {fileName}</p>
                    <div className="form-group">
                        <label>Target Device:</label>
                        <select
                            className="form-control"
                            value={selectedDevice}
                            onChange={e => setSelectedDevice(e.target.value)}
                        >
                            {devices.map((d, i) => <option key={i} value={d}>{d}</option>)}
                        </select>
                    </div>
                    <p className="info-text">
                        Ensure Tinfoil or DBI is running on the Switch and connected via USB.
                    </p>
                    <div className="modal-actions">
                        <button className="btn-primary" onClick={startInstall}>
                            <FontAwesomeIcon icon={faDownload} /> Install Now
                        </button>
                    </div>
                </div>
            )}

            {step === 'installing' && (
                <div style={{ textAlign: 'center' }}>
                    <h4>Installing...</h4>
                    <div className="progress-bar-container" style={{ background: 'var(--ctp-surface0)', height: '20px', borderRadius: '10px', margin: '20px 0', overflow: 'hidden' }}>
                        <div style={{ width: `${progress}%`, background: 'var(--ctp-green)', height: '100%', transition: 'width 0.3s' }}></div>
                    </div>
                    <small>{log}</small>
                </div>
            )}

            {step === 'finished' && (
                <div style={{ textAlign: 'center', color: 'var(--ctp-green)' }}>
                    <FontAwesomeIcon icon={faCheckCircle} size="4x" />
                    <h3>Success!</h3>
                    <p>Game installed successfully.</p>
                    <button className="btn-secondary" onClick={onClose}>Close</button>
                </div>
            )}

            {step === 'aborted' && (
                <div style={{ textAlign: 'center', color: 'var(--ctp-subtext0)' }}>
                    <FontAwesomeIcon icon={faInfoCircle} size="3x" />
                    <h3>Cancelled</h3>
                    <p>{log}</p>
                </div>
            )}

            {step === 'error' && (
                <div style={{ textAlign: 'center', color: 'var(--ctp-red)' }}>
                    <FontAwesomeIcon icon={faExclamationTriangle} size="3x" />
                    <h3>Error</h3>
                    <p>{log}</p>
                    <button className="btn-secondary" onClick={onClose}>Close</button>
                </div>
            )}
        </Modal>
    );
};

export default SwitchInstallerModal;
