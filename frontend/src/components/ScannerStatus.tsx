import React, { useState, useEffect, useRef } from 'react';
import { mediaApi, ScanStatus } from '../api/client';
import { progressHub } from '../api/progressHub';
import { useTranslation, t } from '../i18n/translations';
import './ScannerStatus.css';

const ScannerStatus: React.FC = () => {
    useTranslation(); // Subscribe to language changes
    const [status, setStatus] = useState<ScanStatus>({
        isScanning: false,
        gamesAddedCount: 0,
        filesScannedCount: 0
    });
    const [showFinished, setShowFinished] = useState(false);
    const prevIsScanning = useRef(false);

    const fetchStatus = async () => {
        try {
            const response = await mediaApi.getScanStatus();
            const newStatus = response.data;

            // Trigger library refresh if new games were added during this poll
            if (newStatus.isScanning && newStatus.gamesAddedCount > status.gamesAddedCount) {
                console.log(`ScannerStatus: Detected ${newStatus.gamesAddedCount - status.gamesAddedCount} new games. Triggering refresh...`);
                window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
            }

            // If it just finished scanning
            if (prevIsScanning.current && !newStatus.isScanning) {
                console.log("ScannerStatus: Scan finished detected via polling");
                setShowFinished(true);
                // Auto-hide after 10 seconds (longer for finished message)
                setTimeout(() => setShowFinished(false), 10000);
            }

            // If it just started scanning
            if (!prevIsScanning.current && newStatus.isScanning) {
                console.log("ScannerStatus: Scan started detected via polling");
                setShowFinished(false);
            }

            setStatus(newStatus);
            prevIsScanning.current = newStatus.isScanning;
        } catch (error) {
            console.error("Error polling scanner status:", error);
        }
    };

    useEffect(() => {
        // Prefer push updates via SignalR; keep a slow poll (30s) as a safety net
        // for transient disconnects.
        const interval = setInterval(fetchStatus, 30000);
        fetchStatus();

        const offStarted = progressHub.on('scanStarted', () => {
            prevIsScanning.current = true;
            setShowFinished(false);
            fetchStatus();
        });
        const offFinished = progressHub.on('scanFinished', () => {
            setShowFinished(true);
            setTimeout(() => setShowFinished(false), 10000);
            fetchStatus();
        });
        const offLibrary = progressHub.on('libraryUpdated', () => {
            window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
            fetchStatus();
        });

        return () => {
            clearInterval(interval);
            offStarted();
            offFinished();
            offLibrary();
        };
    }, []);

    const handleBannerClick = async () => {
        if (showFinished) {
            console.log("ScannerStatus: Refreshing library via banner click");
            window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
            setShowFinished(false);
        } else if (status.isScanning) {
            if (window.confirm(t('stopScanConfirm'))) {
                try {
                    await mediaApi.stopScan();
                    console.log("ScannerStatus: Scan stop requested");
                } catch (error) {
                    console.error("Error stopping scan:", error);
                }
            }
        }
    };

    // If it's not scanning and we are not showing the finished message, don't render
    if (!status.isScanning && !showFinished) return null;

    return (
        <div className={`scanner-status ${showFinished ? 'finished' : 'scanning'}`}
            style={{ cursor: 'pointer' }}
            onClick={handleBannerClick}
            title={status.isScanning ? t('stopScanTitle') : t('refreshListTitle')}
        >
            <div className="scanner-status-content">
                {status.isScanning ? (
                    <>
                        <div className="scanner-spinner"></div>
                        <div className="scanner-text">
                            <span className="status-label">{t('scanningLibrary')}</span>
                            {status.lastGameFound && (
                                <span className="game-label">{t('latest')} <strong>{status.lastGameFound}</strong></span>
                            )}
                            <span className="count-label">({status.gamesAddedCount} {t('newGames')})</span>
                            <span className="status-hint">{t('clickToCancel')}</span>
                        </div>
                    </>
                ) : (
                    <div className="scanner-text">
                        <span className="status-label">{t('scanComplete')}</span>
                        <span className="count-label">{status.gamesAddedCount} {t('gamesAdded')}</span>
                        <span className="status-hint">{t('clickToUpdate')}</span>
                    </div>
                )}
            </div>
        </div>
    );
};

export default ScannerStatus;
