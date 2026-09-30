import React, { useEffect, useState } from 'react';
import apiClient, { getErrorMessage } from '../api/client';
import { t } from '../i18n/translations';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faTrash, faMicrochip, faExclamationTriangle, faFolder, faDownload, faFileExport } from '@fortawesome/free-solid-svg-icons';
import './UninstallModal.css';

interface UninstallModalProps {
    isOpen: boolean;
    onClose: () => void;
    onRunUninstaller: () => void;
    onDelete: (deleteLibraryFiles: boolean, deleteDownloadFiles: boolean, plan: DeletePlan) => void;
    gameId: number;
    gameTitle: string;
    uninstallerPath?: string;
}

// What the server moves to the trash: the game's files (null, with the reason, when they can't go) and its downloads
export interface DeletePlan {
    paths: string[] | null;
    refused: string | null;
    downloads: string[];
}

const UninstallModal: React.FC<UninstallModalProps> = ({
    isOpen,
    onClose,
    onRunUninstaller,
    onDelete,
    gameId,
    gameTitle,
    uninstallerPath
}) => {
    const [deleteLibraryFiles, setDeleteLibraryFiles] = useState(true);
    const [deleteDownloadFiles, setDeleteDownloadFiles] = useState(false);
    // Asked for when the dialog opens, so it never shows anything but what the server will move
    const [plan, setPlan] = useState<DeletePlan | null>(null);

    useEffect(() => {
        if (!isOpen) return;
        let current = true;
        setPlan(null);
        apiClient.get<DeletePlan>(`/game/${gameId}/delete-plan`)
            .then(res => { if (current) setPlan(res.data); })
            .catch(err => { if (current) setPlan({ paths: null, refused: getErrorMessage(err), downloads: [] }); });
        return () => { current = false; };
    }, [isOpen, gameId]);

    const downloads = plan?.downloads ?? [];
    // Files only go when the server can say which
    const filesMovable = plan?.paths != null;

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
                                checked={deleteLibraryFiles && filesMovable}
                                disabled={!filesMovable}
                                onChange={(e) => setDeleteLibraryFiles(e.target.checked)}
                            />
                            <div className="um-checkbox-content">
                                <span className="um-checkbox-label">
                                    <FontAwesomeIcon icon={faFolder} style={{ marginRight: '8px', opacity: 0.7 }} />
                                    {t('deleteFilesOption')}
                                </span>
                                {plan?.refused && (
                                    <div className="um-warning">
                                        <span>{plan.refused}</span>
                                    </div>
                                )}
                                {deleteLibraryFiles && filesMovable && (
                                    <>
                                        <div className="um-warning">
                                            <FontAwesomeIcon icon={faExclamationTriangle} style={{ marginTop: '2px' }} />
                                            <span>{t('deleteFilesWarning')}</span>
                                        </div>
                                        {plan?.paths && plan.paths.length > 0 && (
                                            <div className="um-path-preview">
                                                {plan.paths.map(p => <div key={p}>{p}</div>)}
                                            </div>
                                        )}
                                    </>
                                )}
                            </div>
                        </label>

                        {/* DOWNLOAD FOLDER CHECKBOX (IF THE SERVER FOUND THE GAME'S DOWNLOAD) */}
                        {downloads.length > 0 && (
                            <label className="um-checkbox-container" style={{ marginTop: '16px', paddingTop: '16px', borderTop: '1px solid rgba(243, 139, 168, 0.1)' }}>
                                <input
                                    type="checkbox"
                                    checked={deleteDownloadFiles}
                                    onChange={(e) => setDeleteDownloadFiles(e.target.checked)}
                                />
                                <div className="um-checkbox-content">
                                    <span className="um-checkbox-label">
                                        <FontAwesomeIcon icon={faDownload} style={{ marginRight: '8px', opacity: 0.7 }} />
                                        {t('deleteDownloadFilesOption')}
                                    </span>
                                    {deleteDownloadFiles && (
                                        <>
                                            <div className="um-warning">
                                                <FontAwesomeIcon icon={faExclamationTriangle} style={{ marginTop: '2px' }} />
                                                <span>{t('deleteDownloadFilesWarning')}</span>
                                            </div>
                                            <div className="um-path-preview" style={{ color: 'var(--ctp-rosewater)' }}>
                                                {downloads.map(p => <div key={p}>{p}</div>)}
                                            </div>
                                        </>
                                    )}
                                </div>
                            </label>
                        )}

                        <div className="um-actions">
                            <button
                                className="um-btn-delete"
                                disabled={!plan}
                                onClick={() => {
                                    if (!plan) return;
                                    onDelete(deleteLibraryFiles && filesMovable, deleteDownloadFiles && downloads.length > 0, plan);
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
