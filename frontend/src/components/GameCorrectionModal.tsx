import React, { useState } from 'react';
import apiClient from '../api/client';
import { t as translate } from '../i18n/translations';
import FolderExplorerModal from './FolderExplorerModal';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faSearch } from '@fortawesome/free-solid-svg-icons';
import './GameCorrectionModal.css';

interface GameCorrectionGame {
    title: string;
    installPath?: string;
    path?: string;
    executablePath?: string;
    igdbId?: number;
    platform?: { id?: number; name: string; slug?: string };
}

interface MetadataResult {
    igdbId?: number;
    title: string;
    year?: number;
    overview?: string;
    developer?: string;
    publisher?: string;
    genres?: string[];
    rating?: number;
    images?: { coverUrl?: string; coverLargeUrl?: string; backgroundUrl?: string; bannerUrl?: string; screenshots?: string[] };
    metadataSource?: string;
}

interface GameCorrectionModalProps {
    game: GameCorrectionGame;
    onClose: () => void;
    onSave: (updates: Record<string, unknown>) => void;
    language?: string;
}

const GameCorrectionModal: React.FC<GameCorrectionModalProps> = ({ game, onClose, onSave, language = 'es' }) => {
    const [activeTab, setActiveTab] = useState<'metadata' | 'path' | 'playPath'>('metadata');

    // Metadata State
    const [searchTerm, setSearchTerm] = useState(game.title);
    const [results, setResults] = useState<MetadataResult[]>([]);
    const [searching, setSearching] = useState(false);
    const [selectedMetadata, setSelectedMetadata] = useState<MetadataResult | null>(null);
    const [searchSource, setSearchSource] = useState<'igdb' | 'screenscraper'>('igdb');
    const [searchStatus, setSearchStatus] = useState<string | null>(null);
    const [searchMessage, setSearchMessage] = useState<string | null>(null);

    // Path State
    const [installPath, setInstallPath] = useState(game.installPath || game.path || '');
    const [executablePath, setExecutablePath] = useState(game.executablePath || '');
    const [showFileExplorer, setShowFileExplorer] = useState(false);
    const [explorerMode, setExplorerMode] = useState<'install' | 'executable'>('install');

    const t = (key: string) => translate(key as Parameters<typeof translate>[0], language as Parameters<typeof translate>[1]);

    const handleSearch = async () => {
        if (!searchTerm) return;
        setSearching(true);
        setSearchStatus(null);
        setSearchMessage(null);
        try {
            const response = await apiClient.get('/game/lookup', {
                params: {
                    term: searchTerm,
                    lang: language,
                    source: searchSource === 'screenscraper' ? 'screenscraper' : undefined,
                    platformKey: game.platform?.slug || undefined
                }
            });
            // backend wraps results in { games, source, status, message }
            const payload = response.data;
            const list = Array.isArray(payload) ? payload : (payload?.games ?? []);
            setResults(list);
            if (payload && !Array.isArray(payload)) {
                setSearchStatus(payload.status ?? null);
                setSearchMessage(payload.message ?? null);
            }
        } catch (error) {
            console.error(error);
        } finally {
            setSearching(false);
        }
    };

    const handleSave = () => {
        const updates: Record<string, unknown> = {};
        if (selectedMetadata) {
            if (selectedMetadata.metadataSource === 'ScreenScraper') {
                updates.title = selectedMetadata.title;
                updates.metadataSource = 'ScreenScraper';
                if (selectedMetadata.year) updates.year = selectedMetadata.year;
                if (selectedMetadata.overview) updates.overview = selectedMetadata.overview;
                if (selectedMetadata.developer) updates.developer = selectedMetadata.developer;
                if (selectedMetadata.publisher) updates.publisher = selectedMetadata.publisher;
                if (selectedMetadata.genres?.length) updates.genres = selectedMetadata.genres;
                if (selectedMetadata.rating) updates.rating = selectedMetadata.rating;
                if (selectedMetadata.images) updates.images = selectedMetadata.images;
            } else {
                updates.igdbId = selectedMetadata.igdbId;
                updates.title = selectedMetadata.title;
            }
        }
        if (installPath !== game.installPath) {
            updates.installPath = installPath;
        }
        if (executablePath !== game.executablePath) {
            updates.executablePath = executablePath;
        }

        onSave(updates);
    };

    const openExplorer = (mode: 'install' | 'executable') => {
        setExplorerMode(mode);
        setShowFileExplorer(true);
    };

    return (
        <div className="correction-modal-mask">
            <div className="correction-modal">
                <div className="modal-header">
                    <h3>{t('editGame') || 'Edit Game'}</h3>
                    <button className="close-btn" onClick={onClose}>&times;</button>
                </div>

                <div className="modal-tabs">
                    <button
                        className={`tab-btn ${activeTab === 'metadata' ? 'active' : ''}`}
                        onClick={() => setActiveTab('metadata')}
                    >
                        {t('metadataSearch') || 'Metadata Search'}
                    </button>
                    <button
                        className={`tab-btn ${activeTab === 'path' ? 'active' : ''}`}
                        onClick={() => setActiveTab('path')}
                    >
                        {t('installPathTab') || 'Install Path'}
                    </button>
                    <button
                        className={`tab-btn ${activeTab === 'playPath' ? 'active' : ''}`}
                        onClick={() => setActiveTab('playPath')}
                    >
                        {t('executablePathTab') || 'Executable'}
                    </button>
                </div>

                <div className="modal-content">
                    {activeTab === 'metadata' && (
                        <div className="metadata-correction">
                            <div className="source-selector">
                                <label>{t('searchSource') || 'Search Source'}:</label>
                                <select 
                                    value={searchSource} 
                                    onChange={(e) => setSearchSource(e.target.value as 'igdb' | 'screenscraper')}
                                >
                                    <option value="igdb">IGDB</option>
                                    <option value="screenscraper">ScreenScraper</option>
                                </select>
                            </div>
                            <div className="search-bar">
                                <input
                                    type="text"
                                    value={searchTerm}
                                    onChange={(e) => setSearchTerm(e.target.value)}
                                    placeholder={t('searchGame') || 'Search game...'}
                                    onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                                />
                                <button onClick={handleSearch} disabled={searching} className="search-btn">
                                    {searching ? '...' : <FontAwesomeIcon icon={faSearch} />}
                                </button>
                            </div>

                            {searchStatus && searchStatus !== 'ok' && (
                                <div
                                    role="status"
                                    style={{
                                        padding: '8px 12px',
                                        margin: '8px 0',
                                        borderRadius: '6px',
                                        fontSize: '13px',
                                        background: searchStatus === 'empty' ? 'rgba(127,132,156,0.15)' : 'rgba(243,139,168,0.18)',
                                        color: searchStatus === 'empty' ? 'var(--ctp-subtext1, #a6adc8)' : 'var(--ctp-pink, #f38ba8)',
                                        border: `1px solid ${searchStatus === 'empty' ? 'rgba(127,132,156,0.3)' : 'rgba(243,139,168,0.4)'}`
                                    }}
                                >
                                    {searchMessage || (searchStatus === 'empty'
                                        ? (t('searchEmpty') || 'No matches found.')
                                        : (t('searchProblem') || 'Search did not complete.'))}
                                </div>
                            )}
                            <div className="search-results">
                                {results.map((res: MetadataResult, index: number) => (
                                    <div
                                        key={res.igdbId || `ss-${index}`}
                                        className={`search-result-item ${selectedMetadata === res ? 'selected' : ''}`}
                                        onClick={() => setSelectedMetadata(res)}
                                    >
                                        <div className="poster">
                                            {res.images?.coverUrl ? <img src={res.images.coverUrl} alt="" /> : '📷'}
                                        </div>
                                        <div className="info">
                                            <div className="title">{res.title}</div>
                                            <div className="year">{res.year}</div>
                                            <div className="id">
                                                {res.metadataSource === 'ScreenScraper' ? 'ScreenScraper' : `IGDB: ${res.igdbId}`}
                                            </div>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {activeTab === 'path' && (
                        <div className="path-correction">
                            <label>{t('currentPath') || 'Ruta Actual'}:</label>
                            <div className="path-input-group">
                                <input
                                    type="text"
                                    value={installPath}
                                    onChange={(e) => setInstallPath(e.target.value)}
                                />
                                <button onClick={() => openExplorer('install')}>📁</button>
                            </div>
                            <p className="hint">
                                {t('pathHint') || 'Selecciona la carpeta donde está instalado el juego.'}
                            </p>
                        </div>
                    )}

                    {activeTab === 'playPath' && (
                        <div className="path-correction">
                            <label>{t('executablePath') || 'Ejecutable'}:</label>
                            <div className="path-input-group">
                                <input
                                    type="text"
                                    value={executablePath}
                                    onChange={(e) => setExecutablePath(e.target.value)}
                                    placeholder="/path/to/game.exe"
                                />
                                <button onClick={() => openExplorer('executable')}>📁</button>
                            </div>
                            <p className="hint">
                                {t('playPathHint') || 'Selecciona el archivo ejecutable del juego.'}
                            </p>
                        </div>
                    )}
                </div>

                <div className="modal-footer">
                    <button className="btn-secondary" onClick={onClose}>{t('cancel')}</button>
                    <button className="btn-primary" onClick={handleSave}>{t('save')}</button>
                </div>
            </div>

            {showFileExplorer && (
                <FolderExplorerModal
                    initialPath={explorerMode === 'executable' ? (executablePath || installPath || '/') : (installPath || '/')}
                    language={language}
                    onClose={() => setShowFileExplorer(false)}
                    onSelect={(path) => {
                        if (explorerMode === 'install') {
                            setInstallPath(path);
                        } else {
                            setExecutablePath(path);
                        }
                        setShowFileExplorer(false);
                    }}
                />
            )}
        </div>
    );
};

export default GameCorrectionModal;
