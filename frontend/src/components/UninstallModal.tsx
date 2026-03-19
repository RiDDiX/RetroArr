import React, { useState } from 'react';
import { t } from '../i18n/translations';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faTrash, faMicrochip, faExclamationTriangle, faFolder, faDownload, faFileExport } from '@fortawesome/free-solid-svg-icons';
import './UninstallModal.css';

interface UninstallModalProps {
    isOpen: boolean;
    onClose: () => void;
    onRunUninstaller: () => void;
    onDelete: (deleteLibraryFiles: boolean, deleteDownloadFiles: boolean, targetLibraryPath?: string, targetDownloadPath?: string) => void;
    gameTitle: string;
    gamePath?: string;
    uninstallerPath?: string;
    downloadPath?: string;
}

const UninstallModal: React.FC<UninstallModalProps> = ({
    isOpen,
    onClose,
    onRunUninstaller,
    onDelete,
    gameTitle,
    gamePath,
    uninstallerPath,
    downloadPath
}) => {
    const [deleteLibraryFiles, setDeleteLibraryFiles] = useState(true);
    const [deleteDownloadFiles, setDeleteDownloadFiles] = useState(false);
    const [useContainerFolder, setUseContainerFolder] = useState(true);

    // Smart Path Logic: Detect "Game container" vs specific folder.
    const getSmartPaths = (path?: string) => {
        if (!path) return { deepest: '', container: '' };

        const normalized = path.replace(/\\/g, '/');
        const parts = normalized.split('/').filter(p => p !== '');
        const isFile = parts[parts.length - 1].includes('.');
        const baseParts = isFile ? parts.slice(0, -1) : parts;

        const deepest = '/' + baseParts.join('/');
        let container = deepest;

        // Try to find a logical "Game Container" level (e.g., .../Juegos/Name/)
        const roots = ['Juegos', 'Library', 'Games', 'RetroArr', 'Desktop'];
        for (const root of roots) {
            const rootIndex = baseParts.lastIndexOf(root);
            if (rootIndex !== -1 && baseParts.length > rootIndex + 2) {
                // Suggest 1 level after the root
                container = '/' + baseParts.slice(0, rootIndex + 2).join('/');
                break;
            }
        }

        return { deepest, container };
    };

    const paths = getSmartPaths(gamePath);
    const targetLibraryPath = useContainerFolder ? paths.container : paths.deepest;

    if (!isOpen) return null;

    return (
        <div className="um-overlay" onClick={onClose}>
            <div className="um-modal" onClick={(e) => e.stopPropagation()}>
                <div className="um-header">
                    <h3>{t('uninstallTitle')}</h3>
                    <button className="um-close" onClick={onClose}>×</button>
                </div>

                <div className="um-content">
                    <p className="um-game-title">
                        {t('manageGame')}: <strong>{gameTitle}</strong>
                    </p>

                    {/* SECTION 1: OFFICIAL UNINSTALLER */}
                    <div className="um-section">
                        <div className="um-section-label">
                            <FontAwesomeIcon icon={faMicrochip} style={{ marginRight: '10px', color: 'var(--ctp-blue)' }} />
                            {t('uninstallOption')}
                        </div>
                        <p className="um-section-desc">
                            {uninstallerPath
                                ? t('uninstallerFound') || 'Official uninstaller detected.'
                                : t('noUninstallerFound') || 'No official uninstaller detected.'}
                        </p>
                        {uninstallerPath && (
                            <div className="um-path-preview" style={{ marginBottom: '12px', color: 'var(--ctp-blue)', borderColor: 'rgba(137, 180, 250, 0.2)' }}>
                                <FontAwesomeIcon icon={faFileExport} style={{ marginRight: '8px' }} />
                                {uninstallerPath}
                            </div>
                        )}
                        <button
                            className="um-btn-secondary"
                            onClick={() => {
                                onRunUninstaller();
                                onClose();
                            }}
                            disabled={!uninstallerPath}
                            style={{ opacity: uninstallerPath ? 1 : 0.5 }}
                        >
                            {t('runUninstaller')}
                        </button>
                    </div>

                    {/* SECTION 2: LIBRARY FILES */}
                    <div className="um-delete-section">
                        <div className="um-section-label" style={{ color: 'var(--ctp-red)' }}>
                            <FontAwesomeIcon icon={faTrash} style={{ marginRight: '10px' }} />
                            {t('removeFromLibrary')}
                        </div>

                        {/* LIBRARY FOLDER CHECKBOX */}
                        <label className="um-checkbox-container">
                            <input
                                type="checkbox"
                                checked={deleteLibraryFiles}
                                onChange={(e) => setDeleteLibraryFiles(e.target.checked)}
                            />
                            <div className="um-checkbox-content">
                                <span className="um-checkbox-label">
                                    <FontAwesomeIcon icon={faFolder} style={{ marginRight: '8px', opacity: 0.7 }} />
                                    {t('deleteFilesOption')} ({t('gameFolder')})
                                </span>
                                {deleteLibraryFiles && (
                                    <>
                                        <div className="um-warning">
                                            <FontAwesomeIcon icon={faExclamationTriangle} style={{ marginTop: '2px' }} />
                                            <span>{t('deleteFilesWarning')}</span>
                                        </div>
                                        {targetLibraryPath && (
                                            <div className="um-path-preview">
                                                {targetLibraryPath}
                                            </div>
                                        )}
                                        {/* DEPTH SELECTION */}
                                        {paths.container !== paths.deepest && (
                                            <div className="um-depth-selector">
                                                <div className="um-selector-label">{t('cleanupLevel')}</div>
                                                <div className="um-radio-group">
                                                    <label className={`um-radio-item ${useContainerFolder ? 'active' : ''}`}>
                                                        <input
                                                            type="radio"
                                                            name="cleanup-depth"
                                                            checked={useContainerFolder}
                                                            onChange={() => setUseContainerFolder(true)}
                                                        />
                                                        <span>{t('containerFolder')}</span>
                                                    </label>
                                                    <label className={`um-radio-item ${!useContainerFolder ? 'active' : ''}`}>
                                                        <input
                                                            type="radio"
                                                            name="cleanup-depth"
                                                            checked={!useContainerFolder}
                                                            onChange={() => setUseContainerFolder(false)}
                                                        />
                                                        <span>{t('deepFolder')}</span>
                                                    </label>
                                                </div>
                                            </div>
                                        )}
                                    </>
                                )}
                            </div>
                        </label>

                        {/* DOWNLOAD FOLDER CHECKBOX (IF DETECTED) */}
                        {downloadPath && (
                            <label className="um-checkbox-container" style={{ marginTop: '16px', paddingTop: '16px', borderTop: '1px solid rgba(243, 139, 168, 0.1)' }}>
                                <input
                                    type="checkbox"
                                    checked={deleteDownloadFiles}
                                    onChange={(e) => setDeleteDownloadFiles(e.target.checked)}
                                />
                                <div className="um-checkbox-content">
                                    <span className="um-checkbox-label">
                                        <FontAwesomeIcon icon={faDownload} style={{ marginRight: '8px', opacity: 0.7 }} />
                                        {t('deleteDownloadFilesOption') || 'Delete download folder'}
                                    </span>
                                    {deleteDownloadFiles && (
                                        <>
                                            <div className="um-warning">
                                                <FontAwesomeIcon icon={faExclamationTriangle} style={{ marginTop: '2px' }} />
                                                <span>{t('deleteDownloadFilesWarning') || 'This will delete the original download files.'}</span>
                                            </div>
                                            <div className="um-path-preview" style={{ color: 'var(--ctp-rosewater)' }}>
                                                {downloadPath}
                                            </div>
                                        </>
                                    )}
                                </div>
                            </label>
                        )}

                        <div className="um-actions">
                            <button
                                className="um-btn-delete"
                                onClick={() => {
                                    onDelete(deleteLibraryFiles, deleteDownloadFiles, targetLibraryPath, downloadPath);
                                    onClose();
                                }}
                            >
                                {t('removeFromLibrary')}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default UninstallModal;
