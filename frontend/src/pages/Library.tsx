import React, { useState, useEffect, useMemo, useCallback, useRef } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import apiClient, { mediaApi, MetadataRescanStatus } from '../api/client';
import GameCard from '../components/GameCard';
import ContextMenu from '../components/ContextMenu';
import { Modal, ConfirmDialog } from '../components/ui';
import PlatformIcon from '../components/PlatformIcon';
import { t, getLanguage } from '../i18n/translations';
import appLogo from '../assets/app_logo.png';
import './Library.css';
import { useUI } from '../context/UIContext';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faTrash, faThLarge, faBars, faGlobe, faGamepad, faSync, faSearch, faChevronDown, faChevronRight, faFolderOpen, faDatabase } from '@fortawesome/free-solid-svg-icons';

interface Game {
  id: number;
  title: string;
  year: number;
  overview: string;
  images: {
    coverUrl?: string;
  };
  rating: number;
  genres: string[];
  platformId?: number;
  platform: { id?: number; name: string };
  status: string;
  steamId?: number;
  igdbId?: number;
  path?: string;
}

interface SearchResult {
  id: number;
  title: string;
  overview?: string;
  images: {
    coverUrl?: string;
  };
  year?: number;
  igdbId?: number;
  availablePlatforms?: string[];
}

interface Platform {
  id: number;
  name: string;
  slug: string;
  folderName?: string;
  category?: string;
  igdbPlatformId?: number;
  enabled?: boolean;
  preferredMetadataSource?: string;
}

const Library: React.FC = () => {
  const { toggleKofi } = useUI();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [platforms, setPlatforms] = useState<Platform[]>([]);
  const [selectedPlatform, setSelectedPlatform] = useState(searchParams.get('platform') || '');

  const handlePlatformChange = (platformId: string) => {
    setSelectedPlatform(platformId);
    if (platformId) {
      setSearchParams({ platform: platformId }, { replace: true });
    } else {
      setSearchParams({}, { replace: true });
    }
  };

  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>('asc');
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<SearchResult[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [showSearchResults, setShowSearchResults] = useState(false);
  const [games, setGames] = useState<Game[]>([]);
  const [viewMode, setViewMode] = useState<'grid' | 'list'>('grid');
  const [, setForceUpdateCounter] = useState(0); // Force re-render trigger
  const [isIgdbConfigured, setIsIgdbConfigured] = useState(true); // Assume true until check
  const [showClearConfirm, setShowClearConfirm] = useState(false);
  const [showPlatformPicker, setShowPlatformPicker] = useState(false);
  const [pendingGameToAdd, setPendingGameToAdd] = useState<SearchResult | null>(null);
  const [selectedAddPlatform, setSelectedAddPlatform] = useState<number | null>(null);
  const [allPlatforms, setAllPlatforms] = useState<Platform[]>([]);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [collapsedCategories, setCollapsedCategories] = useState<Set<string>>(new Set());
  const [platformScanning, setPlatformScanning] = useState(false);
  const [metadataRescanning, setMetadataRescanning] = useState(false);
  const [rescanStatus, setRescanStatus] = useState<MetadataRescanStatus | null>(null);
  const [showScraperChoice, setShowScraperChoice] = useState<{ platformId: number; missingOnly: boolean } | null>(null);
  const [selectedScraper, setSelectedScraper] = useState<'igdb' | 'screenscraper'>('igdb');
  
  // Pagination state
  const [currentPage, setCurrentPage] = useState(1);
  const [itemsPerPage, setItemsPerPage] = useState(() => {
    const saved = localStorage.getItem('libraryItemsPerPage');
    return saved ? parseInt(saved, 10) : 50;
  });
  const itemsPerPageOptions = [10, 25, 50, 100, 250, 500, 1000];
  const rescanPollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const [contextMenu, setContextMenu] = useState<{ x: number, y: number, visible: boolean, game: Game | null }>({
    x: 0,
    y: 0,
    visible: false,
    game: null
  });

  const language = getLanguage();

  const handleContextMenu = (e: React.MouseEvent, game: Game) => {
    e.preventDefault();
    e.stopPropagation();
    console.log('Right click detected for game:', game.title);
    // alerta temporal para verificar que el clic derecho llega
    // window.alert('Clic derecho en: ' + game.title); 
    setContextMenu({
      x: e.clientX,
      y: e.clientY,
      visible: true,
      game
    });
  };

  const handleDeleteGame = async (game: Game) => {
    try {
      await apiClient.delete(`/game/${game.id}`);
      await loadGames();
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
    } catch (error: unknown) {
      console.error('Error deleting game:', error);
    }
  };

  // Cargar juegos de la biblioteca desde la API
  useEffect(() => {
    loadGames();
    loadPlatforms();
    checkIgdbConfig();

    // Check if a rescan is already running (display-only, does NOT block buttons)
    mediaApi.getMetadataRescanStatus().then(res => {
      if (res.data.isRescanning) {
        setRescanStatus(res.data);
        setMetadataRescanning(true);
        startRescanPolling();
      }
    }).catch(() => {});

    // Listen for global library updates (e.g. from Auto-Scan in Settings)
    const handleLibraryUpdate = () => {
      console.log("[Library] Received update signal (EVENT). Loading games...");
      setForceUpdateCounter(prev => prev + 1); // FORCE React to re-render
      loadGames();
    };

    window.addEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
    return () => {
      window.removeEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
      if (rescanPollRef.current) clearInterval(rescanPollRef.current);
    };
  }, []);

  const loadGames = async () => {
    try {
      const response = await apiClient.get('/game', { params: { t: Date.now() } });
      setGames(response.data);
    } catch (error) {
      console.error('Error loading games:', error);
    }
  };

  const checkIgdbConfig = async () => {
    try {
      const response = await apiClient.get('/settings/igdb');
      if (!response.data.clientId || !response.data.clientSecret) {
        setIsIgdbConfigured(false);
      } else {
        setIsIgdbConfigured(true);
      }
    } catch (error) {
      console.error('Error checking IGDB config:', error);
    }
  };

  const loadPlatforms = async () => {
    try {
      const response = await apiClient.get('/platform', { params: { enabledOnly: true } });
      const sorted = [...response.data].sort((a: Platform, b: Platform) => a.name.localeCompare(b.name));
      setAllPlatforms(sorted);
      setPlatforms(sorted);
    } catch (error) {
      console.error('Error loading platforms:', error);
    }
  };

  const [localResults, setLocalResults] = useState<Game[]>([]);

  const handleSearch = async () => {
    if (!searchQuery.trim()) return;

    setIsSearching(true);
    setShowSearchResults(true);
    
    // First, filter local games
    const query = searchQuery.toLowerCase();
    const localMatches = games.filter(g => 
      g.title?.toLowerCase().includes(query) ||
      g.overview?.toLowerCase().includes(query)
    );
    setLocalResults(localMatches);
    
    // Then search online
    try {
      const response = await apiClient.get('/game/lookup', { params: { term: searchQuery, lang: language } });
      setSearchResults(response.data);
    } catch (error) {
      console.error('Error searching games:', error);
      setSearchResults([]);
    } finally {
      setIsSearching(false);
    }
  };

  const handleClearLibrary = () => {
    setShowClearConfirm(true);
  };

  const confirmClearLibrary = async () => {
    try {
      await apiClient.delete('/game/all');
      await loadGames();
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
    } catch (error) {
      console.error('Error clearing library:', error);
    } finally {
      setShowClearConfirm(false);
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Downloaded':
        return 'var(--ctp-green)';
      case 'Downloading':
        return 'var(--ctp-blue)';
      case 'Missing':
        return 'var(--ctp-red)';
      default:
        return 'var(--ctp-surface0)';
    }
  };

  const translateStatus = (status: string) => {
    switch (status) {
      case 'Downloaded': return t('statusDownloaded');
      case 'Downloading': return t('statusDownloading');
      case 'Missing': return t('statusMissing');
      default: return t('statusUnknown');
    }
  };

  const handleAddGame = async (result: SearchResult) => {
    // Show platform picker if game has multiple platforms
    if (result.availablePlatforms && result.availablePlatforms.length > 0) {
      setPendingGameToAdd(result);
      setSelectedAddPlatform(null);
      setShowPlatformPicker(true);
      return;
    }
    
    // Fallback to default platform if no platforms available
    await addGameWithPlatform(result, 1);
  };

  const addGameWithPlatform = async (result: SearchResult, platformId: number) => {
    try {
      const newGame: Record<string, unknown> = {
        title: result.title,
        year: result.year ?? 0,
        overview: result.overview ?? '',
        igdbId: result.igdbId ?? result.id,
        images: result.images,
        platformId,
        status: 5,
        monitored: true
      };

      await apiClient.post('/game', newGame);
      await loadGames();
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));

      setShowSearchResults(false);
      setSearchQuery('');
      setSearchResults([]);
      setShowPlatformPicker(false);
      setPendingGameToAdd(null);
    } catch (error) {
      console.error('Error adding game:', error);
      alert(t('error'));
    }
  };

  const handleConfirmAddWithPlatform = async () => {
    if (!pendingGameToAdd || !selectedAddPlatform) return;
    await addGameWithPlatform(pendingGameToAdd, selectedAddPlatform);
  };

  const getMatchingPlatforms = (availablePlatformNames: string[]): Platform[] => {
    return allPlatforms.filter(p => 
      availablePlatformNames.some(name => 
        p.name.toLowerCase().includes(name.toLowerCase()) ||
        name.toLowerCase().includes(p.name.toLowerCase()) ||
        p.slug.toLowerCase() === name.toLowerCase()
      )
    );
  };

  // Compute game counts per platform for sidebar badges
  const platformGameCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const game of games) {
      const pid = (game.platformId ?? game.platform?.id ?? 0).toString();
      counts[pid] = (counts[pid] || 0) + 1;
    }
    return counts;
  }, [games]);

  // Group platforms by category for sidebar
  const platformsByCategory = useMemo(() => {
    const groups: Record<string, Platform[]> = {};
    for (const p of platforms) {
      const cat = p.category || 'Other';
      if (!groups[cat]) groups[cat] = [];
      groups[cat].push(p);
    }
    return groups;
  }, [platforms]);

  const categoryOrder = ['Computer', 'Sony', 'Microsoft', 'Nintendo', 'Sega', 'Atari', 'NEC', 'Arcade', 'Handhelds', 'Special'];
  const sortedCategories = useMemo(() => {
    const cats = Object.keys(platformsByCategory);
    return cats.sort((a, b) => {
      const ia = categoryOrder.indexOf(a);
      const ib = categoryOrder.indexOf(b);
      return (ia === -1 ? 999 : ia) - (ib === -1 ? 999 : ib);
    });
  }, [platformsByCategory]);

  const toggleCategory = useCallback((cat: string) => {
    setCollapsedCategories(prev => {
      const next = new Set(prev);
      if (next.has(cat)) next.delete(cat); else next.add(cat);
      return next;
    });
  }, []);

  // Per-platform scan handler
  const handlePlatformScan = useCallback(async (platformSlug: string) => {
    if (platformScanning) return;
    setPlatformScanning(true);
    try {
      await mediaApi.startScan({ platform: platformSlug });
      // Poll scan status until finished, then reload games
      const poll = setInterval(async () => {
        try {
          const res = await mediaApi.getScanStatus();
          if (!res.data.isScanning) {
            clearInterval(poll);
            setPlatformScanning(false);
            loadGames();
            loadPlatforms();
          }
        } catch {
          clearInterval(poll);
          setPlatformScanning(false);
        }
      }, 2000);
    } catch (err) {
      console.error('Platform scan failed:', err);
      setPlatformScanning(false);
    }
  }, [platformScanning]);

  const startRescanPolling = useCallback(() => {
    if (rescanPollRef.current) clearInterval(rescanPollRef.current);
    let failCount = 0;
    rescanPollRef.current = setInterval(async () => {
      try {
        const res = await mediaApi.getMetadataRescanStatus();
        failCount = 0;
        setRescanStatus(res.data);
        if (!res.data.isRescanning) {
          if (rescanPollRef.current) clearInterval(rescanPollRef.current);
          rescanPollRef.current = null;
          setMetadataRescanning(false);
          loadGames();
        }
      } catch {
        failCount++;
        if (failCount >= 5) {
          if (rescanPollRef.current) clearInterval(rescanPollRef.current);
          rescanPollRef.current = null;
          setMetadataRescanning(false);
        }
      }
    }, 2000);
  }, []);

  // Per-platform metadata rescan handler
  const handleMetadataRescan = useCallback(async (platformId: number, missingOnly: boolean, preferredSource: string = 'igdb') => {
    setMetadataRescanning(true);
    setRescanStatus(null);
    try {
      await mediaApi.startMetadataRescan({ platformId, missingOnly, preferredSource });
      startRescanPolling();
    } catch (err: unknown) {
      console.error('Metadata rescan failed:', err);
      const axErr = err as { response?: { data?: { message?: string } }; message?: string };
      const msg = axErr?.response?.data?.message || axErr?.message || 'Unknown error';
      alert(`Metadata rescan failed: ${msg}`);
      setMetadataRescanning(false);
    }
  }, [startRescanPolling]);

  const handleCancelRescan = useCallback(async () => {
    try {
      await mediaApi.cancelMetadataRescan();
      if (rescanPollRef.current) clearInterval(rescanPollRef.current);
      rescanPollRef.current = null;
      setMetadataRescanning(false);
      setRescanStatus(null);
    } catch {
      // Force-reset frontend state even if cancel endpoint fails
      if (rescanPollRef.current) clearInterval(rescanPollRef.current);
      rescanPollRef.current = null;
      setMetadataRescanning(false);
      setRescanStatus(null);
    }
  }, []);

  const openScraperChoice = (platformId: number, missingOnly: boolean) => {
    const plat = allPlatforms.find(p => p.id === platformId);
    const defaultSource = (plat?.preferredMetadataSource || 'igdb') as 'igdb' | 'screenscraper';
    setSelectedScraper(defaultSource);
    setShowScraperChoice({ platformId, missingOnly });
  };

  const startRescanWithChoice = () => {
    if (!showScraperChoice) return;
    handleMetadataRescan(showScraperChoice.platformId, showScraperChoice.missingOnly, selectedScraper);
    setShowScraperChoice(null);
  };

  const selectedPlatformData = useMemo(() => {
    if (!selectedPlatform) return null;
    return platforms.find(p => p.id.toString() === selectedPlatform) || null;
  }, [selectedPlatform, platforms]);

  const filteredGames = useMemo(() => {
    const filtered = games.filter(game => {
      if (!selectedPlatform) return true;
      const pid = selectedPlatform.toString();
      return game.platform?.id?.toString() === pid || game.platformId?.toString() === pid;
    });
    filtered.sort((a, b) => {
      const titleA = a.title?.toLowerCase() || '';
      const titleB = b.title?.toLowerCase() || '';
      if (sortOrder === 'asc') return titleA.localeCompare(titleB);
      return titleB.localeCompare(titleA);
    });
    return filtered;
  }, [games, selectedPlatform, sortOrder]);

  // Pagination calculations
  const totalPages = Math.ceil(filteredGames.length / itemsPerPage);
  const startIndex = (currentPage - 1) * itemsPerPage;
  const endIndex = startIndex + itemsPerPage;
  const paginatedGames = filteredGames.slice(startIndex, endIndex);

  // Reset to page 1 when filter changes
  useEffect(() => {
    setCurrentPage(1);
  }, [selectedPlatform, searchQuery, sortOrder]);

  // Save items per page preference
  const handleItemsPerPageChange = (value: number) => {
    setItemsPerPage(value);
    setCurrentPage(1);
    localStorage.setItem('libraryItemsPerPage', value.toString());
  };

  return (
    <div className={`library library-with-sidebar ${sidebarCollapsed ? 'sidebar-collapsed' : ''}`}>
      {/* ===== Platform Sidebar ===== */}
      <aside className="library-sidebar">
        <div className="sidebar-header">
          {!sidebarCollapsed && <h3>{t('platforms') || 'Platforms'}</h3>}
          <button className="sidebar-toggle-btn" onClick={() => setSidebarCollapsed(p => !p)} title={sidebarCollapsed ? 'Expand' : 'Collapse'}>
            <FontAwesomeIcon icon={sidebarCollapsed ? faChevronRight : faChevronDown} />
          </button>
        </div>
        {!sidebarCollapsed && (
          <div className="sidebar-platforms">
            <button
              className={`sidebar-platform-item ${selectedPlatform === '' ? 'active' : ''}`}
              onClick={() => handlePlatformChange('')}
            >
              <span className="sidebar-platform-name">{t('allPlatforms') || 'All Platforms'}</span>
              <span className="sidebar-platform-count">{games.length}</span>
            </button>

            {sortedCategories.map(cat => (
              <div key={cat} className="sidebar-category">
                <button className="sidebar-category-header" onClick={() => toggleCategory(cat)}>
                  <FontAwesomeIcon icon={collapsedCategories.has(cat) ? faChevronRight : faChevronDown} className="cat-chevron" />
                  <span>{cat}</span>
                </button>
                {!collapsedCategories.has(cat) && platformsByCategory[cat]?.map(p => {
                  const count = platformGameCounts[p.id.toString()] || 0;
                  return (
                    <button
                      key={p.id}
                      className={`sidebar-platform-item ${selectedPlatform === p.id.toString() ? 'active' : ''}`}
                      onClick={() => handlePlatformChange(p.id.toString())}
                      title={`${p.name} — /${p.folderName}/`}
                    >
                      <PlatformIcon platformSlug={p.slug} platformName={p.name} platformId={p.id} size={16} className="sidebar-platform-icon" />
                      <span className="sidebar-platform-name">{p.name}</span>
                      <span className="sidebar-platform-count">{count}</span>
                    </button>
                  );
                })}
              </div>
            ))}
          </div>
        )}
      </aside>

      {/* ===== Main Content ===== */}
      <div className="library-main">
        <div className="library-header">
          <div className="header-left">
            <div className="library-stats">
              <span style={{ textTransform: 'uppercase', fontWeight: 'bold' }}>
                {selectedPlatformData ? selectedPlatformData.name : t('allPlatforms') || 'All Platforms'}
                {' '}&mdash;{' '}{filteredGames.length} {t('gamesCount')}
              </span>
            </div>
          </div>
          <div className="header-right">
            <div className="search-bar-mini">
              <input
                type="text"
                placeholder={t('searchPlaceholder')}
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onKeyPress={(e) => e.key === 'Enter' && handleSearch()}
                size={Math.max((t('searchPlaceholder') || '').length, searchQuery.length) + 2}
              />
              <button onClick={handleSearch} disabled={isSearching || !searchQuery.trim()} className="search-btn-mini">
                <FontAwesomeIcon icon={faSearch} /> {t('search')}
              </button>
            </div>
          </div>
        </div>

        {/* Per-Platform Action Bar */}
        {selectedPlatformData && (
          <div className="platform-action-bar">
            <div className="platform-action-info">
              <PlatformIcon platformSlug={selectedPlatformData.slug} platformName={selectedPlatformData.name} platformId={selectedPlatformData.id} size={20} />
              <span className="platform-action-folder">/{selectedPlatformData.folderName}/</span>
            </div>
            <div className="platform-action-buttons">
              <button
                className="platform-action-btn scan-btn"
                onClick={() => handlePlatformScan(selectedPlatformData.slug)}
                disabled={platformScanning}
                title={t('scanPlatform') || 'Scan this platform for new games'}
              >
                <FontAwesomeIcon icon={faFolderOpen} spin={platformScanning} />
                {platformScanning ? (t('scanning') || 'Scanning...') : (t('scanPlatform') || 'Scan Platform')}
              </button>
              <button
                className="platform-action-btn rescan-btn"
                onClick={() => openScraperChoice(selectedPlatformData.id, true)}
                title={t('rescanMetadataMissing') || 'Re-fetch metadata for games missing info'}
              >
                <FontAwesomeIcon icon={faDatabase} spin={metadataRescanning} />
                {metadataRescanning ? `${rescanStatus?.progress || 0}/${rescanStatus?.total || '?'}` : (t('rescanMetadata') || 'Rescan Metadata')}
              </button>
              <button
                className="platform-action-btn rescan-btn force"
                onClick={() => openScraperChoice(selectedPlatformData.id, false)}
                title={t('rescanMetadataForce') || 'Force re-fetch all metadata for this platform'}
              >
                <FontAwesomeIcon icon={faSync} spin={metadataRescanning} />
                {t('forceRefresh') || 'Force Refresh'}
              </button>
            </div>
            {metadataRescanning && rescanStatus && (
              <div className="rescan-progress">
                <div className="rescan-progress-bar" style={{ width: `${rescanStatus.total > 0 ? (rescanStatus.progress / rescanStatus.total) * 100 : 0}%` }} />
                <span className="rescan-progress-text">
                  {rescanStatus.currentGame && <>{rescanStatus.currentGame} &mdash; </>}
                  {rescanStatus.progress}/{rescanStatus.total} ({rescanStatus.updated} {t('updated') || 'updated'})
                </span>
                <button className="platform-action-btn" onClick={handleCancelRescan} style={{ marginLeft: 8, padding: '2px 8px', fontSize: '0.8em' }} title="Cancel rescan">✕</button>
              </div>
            )}
          </div>
        )}

        <div className="library-controls-bar">
          <div className="control-group right" style={{ marginLeft: 'auto' }}>
            <button className="control-btn sort-btn" onClick={() => setSortOrder(prev => prev === 'asc' ? 'desc' : 'asc')} title={`Sort: ${sortOrder === 'asc' ? 'A-Z' : 'Z-A'}`}>
              {sortOrder === 'asc' ? 'A-Z' : 'Z-A'}
            </button>
            <button className="control-btn clear-btn" onClick={handleClearLibrary} title={t('clearLibrary')}>
              <FontAwesomeIcon icon={faTrash} />
            </button>
            <div className="view-toggle">
              <button className={`view-btn ${viewMode === 'grid' ? 'active' : ''}`} onClick={() => setViewMode('grid')} title={t('grid')}>
                <FontAwesomeIcon icon={faThLarge} />
              </button>
              <button className={`view-btn ${viewMode === 'list' ? 'active' : ''}`} onClick={() => setViewMode('list')} title={t('list')}>
                <FontAwesomeIcon icon={faBars} />
              </button>
            </div>
          </div>
        </div>

      {showSearchResults && (
        <div className="search-results-overlay">
          <div className="search-results-modal">
            <div className="search-results-header">
              <h3>{t('searchResults')}</h3>
              <button className="close-btn" onClick={() => setShowSearchResults(false)}>×</button>
            </div>
            <div className="search-results-list">
              {/* Local Library Results */}
              {localResults.length > 0 && (
                <div className="local-results-section">
                  <h4 style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px', color: 'var(--ctp-green)' }}>
                    <FontAwesomeIcon icon={faGamepad} />
                    {t('inYourLibrary') || 'In Your Library'} ({localResults.length})
                  </h4>
                  {localResults.slice(0, 5).map(game => (
                    <div key={game.id} className="search-result-item local-result" 
                      onClick={() => { navigate(`/game/${game.id}`); setShowSearchResults(false); }}
                      style={{ cursor: 'pointer', borderLeft: '3px solid var(--ctp-green)' }}>
                      {game.images?.coverUrl && (
                        <img src={game.images.coverUrl} alt={game.title} className="result-cover" />
                      )}
                      <div className="result-info">
                        <h4>{game.title}</h4>
                        <div className="result-meta-row" style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
                          <span className="result-year">{game.year}</span>
                          {game.platform?.name && <span className="platform-badge" style={{ fontSize: '0.7rem', padding: '2px 6px', borderRadius: '4px', backgroundColor: 'var(--ctp-surface1)', color: 'var(--ctp-text)' }}>{game.platform.name}</span>}
                        </div>
                      </div>
                      <span style={{ color: 'var(--ctp-green)', fontSize: '0.8rem' }}>{t('viewDetails') || 'View'}</span>
                    </div>
                  ))}
                  {localResults.length > 5 && (
                    <p style={{ color: 'var(--ctp-overlay0)', fontSize: '0.8rem', marginTop: '8px' }}>+{localResults.length - 5} {t('moreResults') || 'more'}</p>
                  )}
                </div>
              )}

              {/* Online Search Results */}
              {searchResults.length > 0 && (
                <h4 style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '16px', marginBottom: '12px', color: 'var(--ctp-blue)' }}>
                  <FontAwesomeIcon icon={faGlobe} />
                  {t('addFromOnline') || 'Add from Online'} ({searchResults.length})
                </h4>
              )}

              {isSearching ? (
                <div className="search-loading">
                  <p>{t('searching')}</p>
                </div>
              ) : searchResults.length === 0 && localResults.length === 0 ? (
                <div className="no-results">
                  <p>{t('noGamesFound')} &quot;{searchQuery}&quot;</p>
                </div>
              ) : (
                searchResults.map((result, index) => {
                  // Check if this online result matches an existing game in library
                  const existingGame = games.find(g => 
                    g.title?.toLowerCase() === result.title?.toLowerCase() ||
                    (g.igdbId && g.igdbId === result.id)
                  );
                  const isExisting = !!existingGame;

                  return (
                    <div key={`search-${result.id}-${index}`} className={`search-result-item ${isExisting ? 'existing-match' : ''}`}
                      style={isExisting ? { borderLeft: '3px solid var(--ctp-yellow)', backgroundColor: 'rgba(249, 226, 175, 0.05)' } : {}}>
                      {result.images?.coverUrl && (
                        <img src={result.images.coverUrl} alt={result.title} className="result-cover" />
                      )}
                      <div className="result-info">
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                          <h4>{result.title}</h4>
                          {isExisting && (
                            <span style={{
                              fontSize: '0.7rem',
                              padding: '2px 8px',
                              borderRadius: '4px',
                              backgroundColor: 'var(--ctp-green)',
                              color: 'var(--ctp-base)',
                              fontWeight: 600
                            }}>
                              {t('inLibrary') || 'In Library'}
                            </span>
                          )}
                        </div>
                        <div className="result-meta-row" style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', marginBottom: '0.5rem' }}>
                          {typeof result.year === 'number' && result.year > 0 && (
                            <span className="result-year">
                              {result.year}
                            </span>
                          )}
                          {result.availablePlatforms && result.availablePlatforms.map(p => (
                            <span key={p} className="platform-badge" style={{
                              fontSize: '0.7rem',
                              padding: '2px 6px',
                              borderRadius: '4px',
                              backgroundColor: 'var(--ctp-surface1)',
                              color: 'var(--ctp-text)'
                            }}>
                              {p}
                            </span>
                          ))}
                        </div>
                        {result.overview && (
                          <p className="result-summary">{result.overview.substring(0, 150)}...</p>
                        )}
                      </div>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        {isExisting ? (
                          <>
                            <button
                              className="add-game-btn"
                              onClick={() => { navigate(`/game/${existingGame.id}`); setShowSearchResults(false); }}
                              style={{ backgroundColor: 'var(--ctp-green)', color: 'var(--ctp-base)' }}
                            >
                              {t('viewGame') || 'View'}
                            </button>
                            <button
                              className="add-game-btn"
                              onClick={() => handleAddGame(result)}
                              style={{ backgroundColor: 'var(--ctp-yellow)', color: 'var(--ctp-base)', fontSize: '0.75rem' }}
                              title={t('reAddOnlineHint') || 'Add this as a new entry with fresh metadata from online'}
                            >
                              {t('reAddOnline') || 'Re-add Online'}
                            </button>
                          </>
                        ) : (
                          <button
                            className="add-game-btn"
                            onClick={() => handleAddGame(result)}
                          >
                            {t('addToLibrary')}
                          </button>
                        )}
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        </div>
      )
      }


      {
        filteredGames.length === 0 ? (
          <div className="empty-library">
            <div className="empty-icon" onClick={toggleKofi} style={{ cursor: 'pointer' }}>
              <img src={appLogo} alt="RetroArr" className="empty-lib-logo" />
            </div>
            {selectedPlatformData ? (
              <>
                <h3>{t('noGamesForPlatform') || 'No games found for this platform'}</h3>
                <p>{t('scanPlatformHint') || 'Use "Scan Platform" above to scan for games in this folder.'}</p>
                <button
                  className="platform-action-btn scan-btn"
                  style={{ marginTop: '1rem' }}
                  onClick={() => handlePlatformScan(selectedPlatformData.slug)}
                  disabled={platformScanning}
                >
                  <FontAwesomeIcon icon={faFolderOpen} spin={platformScanning} />
                  {platformScanning ? (t('scanning') || 'Scanning...') : (t('scanPlatform') || 'Scan Platform')}
                </button>
              </>
            ) : (
              <>
                <h3>
                  {searchQuery
                    ? t('noGamesFound')
                    : (!isIgdbConfigured ? t('configureIgdbToStart') : t('noGamesInLibrary'))
                  }
                </h3>
                <p>
                  {searchQuery ? '' : (isIgdbConfigured ? t('useSearchBar') : '')}
                </p>
              </>
            )}
          </div>
        ) : viewMode === 'grid' ? (
          <div className="game-grid">
            {paginatedGames.map(game => (
              <GameCard
                key={game.id}
                game={game}
                onClick={() => {
                  console.log('Navigating to game details', game.id);
                  const fromPlatform = selectedPlatform ? platforms.find(p => p.id.toString() === selectedPlatform) : null;
                  navigate(`/game/${game.id}`, { state: { fromPlatformId: selectedPlatform, fromPlatformName: fromPlatform?.name } });
                }}
                onContextMenu={(e) => handleContextMenu(e, game)}
                onDelete={() => handleDeleteGame(game)}
              />
            ))}
          </div>
        ) : (
          <div className="game-list">
            {paginatedGames.map(game => (
              <div
                key={game.id}
                className="game-list-item"
                onClick={() => {
                  const fromPlatform = selectedPlatform ? platforms.find(p => p.id.toString() === selectedPlatform) : null;
                  navigate(`/game/${game.id}`, { state: { fromPlatformId: selectedPlatform, fromPlatformName: fromPlatform?.name } });
                }}
                onContextMenu={(e) => handleContextMenu(e, game)}
              >
                {game.images?.coverUrl ? (
                  <img src={game.images.coverUrl} alt={game.title} className="list-cover" />
                ) : (
                  <div className="list-cover-placeholder">?</div>
                )}
                <div className="list-info">
                  <h3>{game.title || 'Untitled'}</h3>
                  <div className="list-meta">
                    <span>{game.year || 'N/A'}</span>
                    {game.platform?.name && <span>{game.platform.name}</span>}
                    <span
                      className="list-status-badge"
                      style={{
                        backgroundColor: getStatusColor(game.status),
                        color: ['Missing', 'Downloading', 'Downloaded'].includes(game.status) ? 'var(--ctp-crust)' : 'var(--ctp-text)'
                      }}
                    >
                      {translateStatus(game.status)}
                    </span>
                  </div>
                </div>
                <div className="list-rating">
                  {typeof game.rating === 'number' && game.rating > 0 ? (
                    <span>{Math.round(game.rating)}%</span>
                  ) : (
                    <span className="no-rating">N/A</span>
                  )}
                  <button
                    className="list-delete-btn"
                    title={t('deleteFromLibrary')}
                    onClick={(e) => {
                      e.stopPropagation();
                      handleDeleteGame(game);
                    }}
                  >
                    ×
                  </button>
                </div>
              </div>
            ))}
          </div>
        )
      }

      {/* Pagination Controls */}
      {filteredGames.length > 0 && totalPages > 0 && (
        <div className="pagination-controls">
          <div className="pagination-info">
            {t('showing') || 'Showing'} {startIndex + 1}-{Math.min(endIndex, filteredGames.length)} {t('of') || 'of'} {filteredGames.length}
          </div>
          
          <div className="pagination-buttons">
            <button 
              className="pagination-btn"
              onClick={() => setCurrentPage(1)}
              disabled={currentPage === 1}
              title={t('firstPage') || 'First'}
            >
              ««
            </button>
            <button 
              className="pagination-btn"
              onClick={() => setCurrentPage(p => Math.max(1, p - 1))}
              disabled={currentPage === 1}
              title={t('previousPage') || 'Previous'}
            >
              «
            </button>
            
            <span className="pagination-current">
              {t('page') || 'Page'} {currentPage} / {totalPages}
            </span>
            
            <button 
              className="pagination-btn"
              onClick={() => setCurrentPage(p => Math.min(totalPages, p + 1))}
              disabled={currentPage === totalPages}
              title={t('nextPage') || 'Next'}
            >
              »
            </button>
            <button 
              className="pagination-btn"
              onClick={() => setCurrentPage(totalPages)}
              disabled={currentPage === totalPages}
              title={t('lastPage') || 'Last'}
            >
              »»
            </button>
          </div>

          <div className="pagination-per-page">
            <label>{t('perPage') || 'Per page'}:</label>
            <select 
              value={itemsPerPage} 
              onChange={(e) => handleItemsPerPageChange(parseInt(e.target.value, 10))}
              className="items-per-page-select"
            >
              {itemsPerPageOptions.map(opt => (
                <option key={opt} value={opt}>{opt}</option>
              ))}
            </select>
          </div>
        </div>
      )}

      <ContextMenu
        x={contextMenu.x}
        y={contextMenu.y}
        visible={contextMenu.visible}
        options={[
          {
            label: t('deleteFromLibrary'),
            icon: <FontAwesomeIcon icon={faTrash} />,
            danger: true,
            onClick: () => contextMenu.game && handleDeleteGame(contextMenu.game)
          }
        ]}
        onClose={() => setContextMenu({ ...contextMenu, visible: false })}
      />

      <ConfirmDialog
        isOpen={showClearConfirm}
        onConfirm={confirmClearLibrary}
        onCancel={() => setShowClearConfirm(false)}
        title={t('clearLibrary')}
        message={t('clearLibraryConfirm')}
        confirmLabel={t('delete')}
        cancelLabel={t('cancel')}
        variant="danger"
      />

      <Modal
        isOpen={showPlatformPicker && !!pendingGameToAdd}
        onClose={() => { setShowPlatformPicker(false); setPendingGameToAdd(null); }}
        title={t('selectPlatform') || 'Select Platform'}
        className="platform-picker-modal"
        footer={
          <>
            <button className="btn-secondary" onClick={() => { setShowPlatformPicker(false); setPendingGameToAdd(null); }}>
              {t('cancel')}
            </button>
            <button
              className="btn-primary"
              disabled={!selectedAddPlatform}
              onClick={handleConfirmAddWithPlatform}
              style={{
                backgroundColor: selectedAddPlatform ? 'var(--ctp-blue)' : 'var(--ctp-surface1)',
                color: selectedAddPlatform ? 'var(--ctp-base)' : 'var(--ctp-overlay0)',
                cursor: selectedAddPlatform ? 'pointer' : 'not-allowed'
              }}
            >
              {t('addToLibrary')}
            </button>
          </>
        }
      >
        {pendingGameToAdd && (
          <>
            <div className="pending-game-info" style={{ display: 'flex', gap: '1rem', marginBottom: '1rem', alignItems: 'center' }}>
              {pendingGameToAdd.images?.coverUrl && (
                <img 
                  src={pendingGameToAdd.images.coverUrl} 
                  alt={pendingGameToAdd.title} 
                  style={{ width: '60px', height: '80px', objectFit: 'cover', borderRadius: '4px' }}
                />
              )}
              <div>
                <h4 style={{ margin: 0 }}>{pendingGameToAdd.title}</h4>
                {pendingGameToAdd.year && <span style={{ color: 'var(--ctp-overlay0)' }}>{pendingGameToAdd.year}</span>}
              </div>
            </div>
            
            <p style={{ marginBottom: '0.5rem', color: 'var(--ctp-subtext0)' }}>
              {t('choosePlatformForGame') || 'Choose which platform to add this game for:'}
            </p>
            
            <div className="platform-picker-grid" style={{ 
              display: 'grid', 
              gridTemplateColumns: 'repeat(auto-fill, minmax(140px, 1fr))', 
              gap: '0.5rem',
              maxHeight: '300px',
              overflowY: 'auto',
              padding: '0.5rem'
            }}>
              {pendingGameToAdd.availablePlatforms && getMatchingPlatforms(pendingGameToAdd.availablePlatforms).map(platform => (
                <button
                  key={platform.id}
                  className={`platform-picker-btn ${selectedAddPlatform === platform.id ? 'selected' : ''}`}
                  onClick={() => setSelectedAddPlatform(platform.id)}
                  style={{
                    padding: '0.75rem',
                    border: selectedAddPlatform === platform.id ? '2px solid var(--ctp-blue)' : '1px solid var(--ctp-surface1)',
                    borderRadius: '8px',
                    backgroundColor: selectedAddPlatform === platform.id ? 'var(--ctp-surface0)' : 'var(--ctp-base)',
                    color: 'var(--ctp-text)',
                    cursor: 'pointer',
                    textAlign: 'left',
                    transition: 'all 0.2s'
                  }}
                >
                  <div style={{ fontWeight: 'bold', fontSize: '0.9rem' }}>{platform.name}</div>
                  {platform.folderName && (
                    <div style={{ fontSize: '0.7rem', color: 'var(--ctp-overlay0)', marginTop: '2px' }}>
                      /{platform.folderName}/
                    </div>
                  )}
                </button>
              ))}
              
              {pendingGameToAdd.availablePlatforms && getMatchingPlatforms(pendingGameToAdd.availablePlatforms).length === 0 && (
                <div style={{ gridColumn: '1 / -1', padding: '1rem', textAlign: 'center', color: 'var(--ctp-overlay0)' }}>
                  <p>{t('noPlatformsAvailable') || 'No matching platforms found. Enable more platforms in Settings.'}</p>
                  <div style={{ marginTop: '1rem' }}>
                    <label style={{ display: 'block', marginBottom: '0.5rem' }}>{t('selectManually') || 'Select manually:'}</label>
                    <select 
                      value={selectedAddPlatform || ''} 
                      onChange={(e) => setSelectedAddPlatform(parseInt(e.target.value))}
                      style={{
                        width: '100%',
                        padding: '0.5rem',
                        backgroundColor: 'var(--ctp-surface0)',
                        border: '1px solid var(--ctp-surface1)',
                        borderRadius: '4px',
                        color: 'var(--ctp-text)'
                      }}
                    >
                      <option value="">{t('selectPlatform') || 'Select Platform...'}</option>
                      {allPlatforms.map(p => (
                        <option key={p.id} value={p.id}>{p.name}</option>
                      ))}
                    </select>
                  </div>
                </div>
              )}
            </div>
          </>
        )}
      </Modal>

      {/* Scraper Choice Dialog */}
      <Modal
        isOpen={!!showScraperChoice}
        onClose={() => setShowScraperChoice(null)}
        title={t('selectScraper') || 'Select Metadata Source'}
        maxWidth="420px"
        footer={
          <>
            <button className="btn-secondary" onClick={() => setShowScraperChoice(null)}>{t('cancel') || 'Cancel'}</button>
            <button className="btn-primary" onClick={startRescanWithChoice}>
              <FontAwesomeIcon icon={faDatabase} /> {t('startRescan') || 'Start Rescan'}
            </button>
          </>
        }
      >
        <p style={{ color: 'var(--ctp-subtext0)', marginBottom: 16, fontSize: '0.9em' }}>
          {showScraperChoice?.missingOnly
            ? (t('rescanMissingHint') || 'Re-fetch metadata for games missing info.')
            : (t('rescanForceHint') || 'Force re-fetch all metadata for this platform.')}
        </p>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '12px 14px', borderRadius: 8, background: selectedScraper === 'igdb' ? 'var(--ctp-surface1)' : 'var(--ctp-surface0)', border: selectedScraper === 'igdb' ? '2px solid var(--accent)' : '2px solid transparent', cursor: 'pointer' }}>
            <input type="radio" name="scraper" checked={selectedScraper === 'igdb'} onChange={() => setSelectedScraper('igdb')} />
            <div>
              <strong style={{ color: 'var(--ctp-text)' }}>IGDB</strong>
              <div style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)' }}>
                {t('igdbPrimaryHint') || 'Best for PC, modern consoles. ScreenScraper as fallback.'}
              </div>
            </div>
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '12px 14px', borderRadius: 8, background: selectedScraper === 'screenscraper' ? 'var(--ctp-surface1)' : 'var(--ctp-surface0)', border: selectedScraper === 'screenscraper' ? '2px solid var(--accent)' : '2px solid transparent', cursor: 'pointer' }}>
            <input type="radio" name="scraper" checked={selectedScraper === 'screenscraper'} onChange={() => setSelectedScraper('screenscraper')} />
            <div>
              <strong style={{ color: 'var(--ctp-text)' }}>ScreenScraper</strong>
              <div style={{ fontSize: '0.85em', color: 'var(--ctp-subtext0)' }}>
                {t('screenScraperPrimaryHint') || 'Best for retro consoles, ROMs. IGDB as fallback.'}
              </div>
            </div>
          </label>
        </div>
      </Modal>
      </div>{/* end .library-main */}
    </div>
  );
};

export default Library;
