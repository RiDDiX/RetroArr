import React, { useEffect, useState } from 'react';
import { useParams, Link, useLocation, useNavigate } from 'react-router-dom';
import apiClient, { getErrorMessage, isTimeoutError } from '../api/client';
import { t, getLanguage, useTranslation } from '../i18n/translations';
import GameCorrectionModal from '../components/GameCorrectionModal';
import UninstallModal from '../components/UninstallModal';
import SwitchInstallerModal from '../components/SwitchInstallerModal';
import { Modal } from '../components/ui';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faSearch, faPen, faDownload, faGamepad, faMagnet, faSpinner, faSort, faSortUp, faSortDown, faArrowUp, faArrowDown, faTrash, faMicrochip, faPlay, faStar, faHeart, faTag, faStickyNote, faTrophy, faPlus, faTimes, faFile, faFolder, faFolderOpen, faCloudDownloadAlt, faCheck } from '@fortawesome/free-solid-svg-icons';
import EmulatorPlayer from '../components/EmulatorPlayer';
import ScoreCircle from '../components/ScoreCircle';
import RegionFlag from '../components/RegionFlag';
import ProtonDbBadge from '../components/ProtonDbBadge';
import './GameDetails.css';

interface Game {
  id: number;
  title: string;
  year: number;
  overview?: string;
  storyline?: string;
  images: {
    coverUrl?: string;
    backgroundUrl?: string;
    screenshots?: string[];
  };
  rating?: number;
  genres: string[];
  platform?: {
    id?: number;
    name: string;
    slug?: string;
  };
  platformId?: number;
  status: string | number;
  isInstallable?: boolean;
  availablePlatforms?: string[];
  steamId?: number;
  gogId?: string;
  path?: string;
  uninstallerPath?: string;
  downloadPath?: string;
  canPlay?: boolean;
  region?: string;
  languages?: string;
  revision?: string;
  protonDbTier?: string;
}

interface TorrentResult {
  title: string;
  guid: string;
  downloadUrl: string;
  magnetUrl: string;
  infoUrl: string;
  indexerId: number;
  indexerName?: string;
  indexer?: string; // Matches backend JSON
  indexerFlags: string[];
  size: number;
  seeders?: number;
  leechers?: number;
  totalPeers?: number;
  publishDate: string;
  age: number;
  ageHours: number;
  ageMinutes: number;
  category: string;
  categoryName?: string;
  grabs?: number;
  protocol: string;
  languages: string[];
  quality: string;
  releaseGroup: string;
  source: string;
  container: string;
  codec: string;
  resolution: string;
  formattedSize: string;
  formattedAge: string;
  publishedAt?: string;
  pubDate?: string;
  provider: string; // Added provider field
  // Platform detection fields
  detectedPlatform?: string;
  platformFolder?: string;
}

interface Platform {
  id: number;
  name: string;
  folder: string;
  slug: string;
  category: string;
}

interface GameTag {
  id: number;
  name: string;
  color?: string;
}

interface GameReview {
  userRating?: number;
  completionStatus?: string | number;
  notes?: string;
  isFavorite?: boolean;
  metacriticScore?: number;
  metacriticUrl?: string;
  openCriticScore?: number;
  openCriticUrl?: string;
}

interface GogDownloadResult extends Partial<TorrentResult> {
  gogDownload?: Record<string, unknown>;
  isOwned?: boolean;
}

function sanitizeHtml(html: string): string {
  const doc = new DOMParser().parseFromString(html, 'text/html');
  // Remove dangerous elements
  doc.querySelectorAll('script,iframe,object,embed,form,input,textarea,button,meta,link,style').forEach(el => el.remove());
  // Remove event handler attributes from all elements
  doc.querySelectorAll('*').forEach(el => {
    for (const attr of Array.from(el.attributes)) {
      if (attr.name.startsWith('on') || (attr.name === 'href' && attr.value.trimStart().startsWith('javascript'))) {
        el.removeAttribute(attr.name);
      }
    }
  });
  return doc.body.innerHTML;
}

const GameDetails: React.FC = () => {
  useTranslation(); // Subscribe to language changes
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const [game, setGame] = useState<Game | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searching, setSearching] = useState(false);
  const [hasSearched, setHasSearched] = useState(false);
  const [results, setResults] = useState<TorrentResult[]>([]);
  const [searchDiagnostics, setSearchDiagnostics] = useState<{ providers: Array<{ name: string; status: string; count?: number; error?: string }>; diagnostics: { configured: boolean; message?: string } } | null>(null);
  const [customSearchQuery, setCustomSearchQuery] = useState<string>('');
  const [sortField, setSortField] = useState<keyof TorrentResult | null>('seeders');
  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>('desc');
  const [downloadingUrl, setDownloadingUrl] = useState<string | null>(null);
  const [notification, setNotification] = useState<{ message: string, type: 'success' | 'error' | 'info' } | null>(null);
  const [showCorrectionModal, setShowCorrectionModal] = useState(false);
  const [showUninstallModal, setShowUninstallModal] = useState(false);
  const [showInstallWarning, setShowInstallWarning] = useState(false);
  const [showSwitchModal, setShowSwitchModal] = useState(false);
  const [activeTab, setActiveTab] = useState<'search' | 'files' | 'patches' | 'media' | 'none'>('search'); // 'search' by default to keep existing behavior? Or none? User said "Search Game" is one function. Let's make it toggleable.
  // Toggle search section visibility
  const [showEmulator, setShowEmulator] = useState(false);
  const [isWebPlayable, setIsWebPlayable] = useState(false);
  const [emulatorConfig, setEmulatorConfig] = useState<{ romUrl: string; core: string } | null>(null);
  
  // Reviews, Notes, Tags state
  const [review, setReview] = useState<GameReview | null>(null);
  const [tags, setTags] = useState<GameTag[]>([]);
  const [allTags, setAllTags] = useState<GameTag[]>([]);
  const [showNotesEditor, setShowNotesEditor] = useState(false);
  const [editedNotes, setEditedNotes] = useState('');
  const [showTagPicker, setShowTagPicker] = useState(false);
  const [newTagName, setNewTagName] = useState('');
  
  // Game files state
  const [gameFiles, setGameFiles] = useState<Array<{ name: string; relativePath: string; fullPath: string; size: number; formattedSize: string; extension: string; lastModified: string; fileType?: string }>>([]);
  const [supplementaryFiles, setSupplementaryFiles] = useState<Array<{ name: string; relativePath: string; size: number; formattedSize: string; extension: string; fileType: string; version?: string; contentName?: string; titleId?: string; serial?: string }>>([]);
  const [fileCounts, setFileCounts] = useState<{ main: number; patches: number; dlc: number }>({ main: 0, patches: 0, dlc: 0 });
  const [resolvedPath, setResolvedPath] = useState<string | null>(null);
  const [folderExists, setFolderExists] = useState(false);
  const [creatingFolder, setCreatingFolder] = useState(false);
  const [gameFilesLoading, setGameFilesLoading] = useState(false);
  const [gameFilesTotalSize, setGameFilesTotalSize] = useState<string>('');
  const [gogGameDownloads, setGogGameDownloads] = useState<Array<{ platform: string; name: string; size?: string; manualUrl?: string }>>([]);
  const [gogDownloading, setGogDownloading] = useState<string | null>(null);

  // Rename state
  const [renamePreview, setRenamePreview] = useState<Array<{ id: string; type: string; sourcePath: string; targetPath: string; status: string; conflict: string | null }> | null>(null);
  const [renameLoading, setRenameLoading] = useState(false);
  const [renameApplying, setRenameApplying] = useState(false);
  const [renameResult, setRenameResult] = useState<{ applied: number; failed: number; skipped: number } | null>(null);

  // Supplementary rename state
  const [supRenamePreview, setSupRenamePreview] = useState<Array<{ gameFileId: number; fileType: string; version?: string; contentName?: string; currentFileName: string; newFileName: string; conflict: boolean; status: string }> | null>(null);
  const [supRenameLoading, setSupRenameLoading] = useState(false);
  const [supRenameApplying, setSupRenameApplying] = useState(false);
  const [supRenameResult, setSupRenameResult] = useState<{ applied: number; failed: number; skipped: number } | null>(null);

  // Local media state
  const [localImages, setLocalImages] = useState<Array<{ type: string; fileName: string; url: string }>>([]);
  const [localVideos, setLocalVideos] = useState<Array<{ type: string; fileName: string; url: string; size?: number }>>([]);
  const [localMediaLoading, setLocalMediaLoading] = useState(false);
  const [selectedLocalImage, setSelectedLocalImage] = useState<string | null>(null);

  // Patches state
  const [patchFiles, setPatchFiles] = useState<Array<{ name: string; relativePath: string; fullPath: string; size: number; formattedSize: string; extension: string; lastModified: string }>>([]);
  const [patchesFolder, setPatchesFolder] = useState<string | null>(null);
  const [patchesFolderExists, setPatchesFolderExists] = useState(false);
  const [patchesLoading, setPatchesLoading] = useState(false);
  const [patchesTotalSize, setPatchesTotalSize] = useState<string>('');
  const [creatingPatchesFolder, setCreatingPatchesFolder] = useState(false);

  // Platform selection for download
  const [showLinuxExport, setShowLinuxExport] = useState(false);
  const [selectedRunner, setSelectedRunner] = useState('auto');
  const [showPlatformModal, setShowPlatformModal] = useState(false);
  const [availablePlatforms, setAvailablePlatforms] = useState<Platform[]>([]);
  const [pendingDownload, setPendingDownload] = useState<{ url: string; protocol?: string; detectedPlatform?: string; platformFolder?: string; releaseTitle?: string } | null>(null);
  const [selectedPlatform, setSelectedPlatform] = useState<string>('');

  useEffect(() => {
    if (notification) {
      const timer = setTimeout(() => {
        setNotification(null);
      }, 3000);
      return () => clearTimeout(timer);
    }
  }, [notification]);

  const language = getLanguage();

  useEffect(() => {
    const loadGame = async () => {
      if (!id) return;
      try {
        const response = await apiClient.get(`/game/${id}`, { params: { lang: language } });
        setGame(response.data);
      } catch (err: unknown) {
        setError(getErrorMessage(err, t('error')));
      } finally {
        setLoading(false);
      }
    };

    loadGame();
  }, [id, language]);

  // Check if game is web-playable
  useEffect(() => {
    const checkWebPlayable = async () => {
      if (!id) return;
      try {
        const response = await apiClient.get(`/emulator/${id}/playable`);
        setIsWebPlayable(response.data.playable);
        if (response.data.playable) {
          setEmulatorConfig({
            romUrl: `/api/v3/emulator/${id}/rom`,
            core: response.data.core
          });
        }
      } catch {
        setIsWebPlayable(false);
      }
    };
    checkWebPlayable();
  }, [id]);

  // Load available platforms for download selection
  useEffect(() => {
    const loadPlatforms = async () => {
      try {
        const response = await apiClient.get('/search/platforms');
        setAvailablePlatforms(response.data);
      } catch (err) {
        console.error('Failed to load platforms:', err);
      }
    };
    loadPlatforms();
  }, []);

  // Load review and tags
  useEffect(() => {
    const loadReviewAndTags = async () => {
      if (!id) return;
      try {
        const [reviewRes, tagsRes, allTagsRes] = await Promise.all([
          apiClient.get(`/review/game/${id}`),
          apiClient.get(`/tag/game/${id}`),
          apiClient.get('/tag')
        ]);
        setReview(reviewRes.data);
        setTags(tagsRes.data);
        setAllTags(allTagsRes.data);
      } catch (err) {
        console.error('Error loading review/tags:', err);
      }
    };
    loadReviewAndTags();
  }, [id]);

  // Load game files when files tab is activated
  useEffect(() => {
    const loadGameFiles = async () => {
      if (!id || activeTab !== 'files') return;
      setGameFilesLoading(true);
      try {
        const res = await apiClient.get(`/game/${id}/files`);
        setGameFiles(res.data.files || []);
        setSupplementaryFiles(res.data.supplementaryFiles || []);
        setFileCounts(res.data.counts || { main: 0, patches: 0, dlc: 0 });
        setResolvedPath(res.data.resolvedPath || null);
        setFolderExists(res.data.folderExists ?? false);
        setGameFilesTotalSize(res.data.totalSize || '');
      } catch (err) {
        console.error('Error loading game files:', err);
        setGameFiles([]);
        setSupplementaryFiles([]);
      } finally {
        setGameFilesLoading(false);
      }
    };
    loadGameFiles();
  }, [id, activeTab]);

  // Load GOG downloads when files tab is activated and game has gogId
  useEffect(() => {
    const loadGogDownloads = async () => {
      if (!id || !game?.gogId || activeTab !== 'files') return;
      try {
        const res = await apiClient.get(`/settings/gog/downloads/${game.gogId}`);
        if (res.data.success && res.data.downloads) {
          setGogGameDownloads(res.data.downloads);
        }
      } catch {
        setGogGameDownloads([]);
      }
    };
    loadGogDownloads();
  }, [id, game?.gogId, activeTab]);

  // Load patches when patches tab is activated
  useEffect(() => {
    const loadPatches = async () => {
      if (!id || activeTab !== 'patches') return;
      setPatchesLoading(true);
      try {
        const res = await apiClient.get(`/game/${id}/patches`);
        setPatchFiles(res.data.patches || []);
        setPatchesFolder(res.data.patchesFolder || null);
        setPatchesFolderExists(res.data.folderExists ?? false);
        setPatchesTotalSize(res.data.totalSize || '');
      } catch {
        setPatchFiles([]);
      } finally {
        setPatchesLoading(false);
      }
    };
    loadPatches();
  }, [id, activeTab]);

  // Load local media when media tab is activated
  useEffect(() => {
    const loadLocalMedia = async () => {
      if (!id || activeTab !== 'media') return;
      setLocalMediaLoading(true);
      try {
        const res = await apiClient.get(`/game/${id}/local-media`);
        setLocalImages(res.data.images || []);
        setLocalVideos(res.data.videos || []);
      } catch {
        setLocalImages([]);
        setLocalVideos([]);
      } finally {
        setLocalMediaLoading(false);
      }
    };
    loadLocalMedia();
  }, [id, activeTab]);

  const handleGogDownloadToFolder = async (manualUrl: string, fileName?: string, platform?: string) => {
    if (!id || gogDownloading) return;
    setGogDownloading(manualUrl);
    try {
      const res = await apiClient.post(`/game/${id}/gog-download`, { manualUrl, fileName, platform });
      if (res.data.success) {
        setNotification({ message: res.data.message || 'GOG download started', type: 'success' });
      } else {
        setNotification({ message: res.data.message || 'GOG download failed', type: 'error' });
      }
    } catch (err) {
      setNotification({ message: getErrorMessage(err, 'GOG download failed'), type: 'error' });
    } finally {
      setGogDownloading(null);
    }
  };

  const handleDownloadFile = (relativePath: string) => {
    const a = document.createElement('a');
    a.href = `/api/v3/game/${id}/files/download?path=${encodeURIComponent(relativePath)}`;
    a.download = relativePath.split('/').pop() || relativePath;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
  };

  const handleCreateFolder = async () => {
    if (!id || creatingFolder) return;
    setCreatingFolder(true);
    try {
      const res = await apiClient.post(`/game/${id}/folder`);
      setNotification({ message: res.data.message || 'Folder created', type: 'success' });
      setResolvedPath(res.data.path || resolvedPath);
      setFolderExists(true);
      // Reload files
      const filesRes = await apiClient.get(`/game/${id}/files`);
      setGameFiles(filesRes.data.files || []);
      setResolvedPath(filesRes.data.resolvedPath || null);
      setFolderExists(filesRes.data.folderExists ?? false);
      setGameFilesTotalSize(filesRes.data.totalSize || '');
    } catch (err) {
      console.error('Error creating folder:', err);
      setNotification({ message: 'Failed to create game folder', type: 'error' });
    } finally {
      setCreatingFolder(false);
    }
  };

  const handleCreatePatchesFolder = async () => {
    if (!id || creatingPatchesFolder) return;
    setCreatingPatchesFolder(true);
    try {
      const res = await apiClient.post(`/game/${id}/patches/folder`);
      setNotification({ message: res.data.message || 'Patches folder created', type: 'success' });
      setPatchesFolderExists(true);
      setPatchesFolder(res.data.path || patchesFolder);
    } catch (err) {
      console.error('Error creating patches folder:', err);
      setNotification({ message: 'Failed to create patches folder', type: 'error' });
    } finally {
      setCreatingPatchesFolder(false);
    }
  };

  const getFileIcon = (ext: string) => {
    const archiveExts = ['.zip', '.rar', '.7z', '.tar', '.gz'];
    const folderLike = ['.iso', '.bin', '.img'];
    if (archiveExts.includes(ext.toLowerCase())) return faFolder;
    if (folderLike.includes(ext.toLowerCase())) return faFolderOpen;
    return faFile;
  };

  const handleSaveNotes = async () => {
    if (!id) return;
    try {
      await apiClient.post(`/review/game/${id}`, { notes: editedNotes });
      setReview({ ...review, notes: editedNotes });
      setShowNotesEditor(false);
      setNotification({ message: t('notesSaved') || 'Notes saved', type: 'success' });
    } catch {
      setNotification({ message: t('error'), type: 'error' });
    }
  };

  const handleToggleFavorite = async () => {
    if (!id) return;
    try {
      const res = await apiClient.post(`/review/game/${id}/favorite`);
      setReview({ ...review, isFavorite: res.data.isFavorite });
    } catch (err) {
      console.error('Error toggling favorite:', err);
    }
  };

  const handleSetRating = async (rating: number) => {
    if (!id) return;
    try {
      await apiClient.post(`/review/game/${id}`, { userRating: rating });
      setReview({ ...review, userRating: rating });
    } catch (err) {
      console.error('Error setting rating:', err);
    }
  };

  const handleSetCompletionStatus = async (status: number) => {
    if (!id) return;
    try {
      await apiClient.post(`/review/game/${id}`, { completionStatus: status });
      setReview({ ...review, completionStatus: status });
    } catch (err) {
      console.error('Error setting status:', err);
    }
  };

  const handleAddTag = async (tagId?: number, tagName?: string) => {
    if (!id) return;
    try {
      await apiClient.post(`/tag/game/${id}`, { tagId, tagName: tagName });
      const tagsRes = await apiClient.get(`/tag/game/${id}`);
      setTags(tagsRes.data);
      const allTagsRes = await apiClient.get('/tag');
      setAllTags(allTagsRes.data);
      setNewTagName('');
      setShowTagPicker(false);
    } catch (err: unknown) {
      setNotification({ message: getErrorMessage(err, 'Error adding tag'), type: 'error' });
    }
  };

  const handleRemoveTag = async (tagId: number) => {
    if (!id) return;
    try {
      await apiClient.delete(`/tag/game/${id}/${tagId}`);
      setTags(tags.filter(t => t.id !== tagId));
    } catch (err) {
      console.error('Error removing tag:', err);
    }
  };

  const handlePlayInBrowser = () => {
    if (emulatorConfig) {
      setShowEmulator(true);
    }
  };

  const handleSort = (field: keyof TorrentResult) => {
    if (sortField === field) {
      setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc');
    } else {
      setSortField(field);
      setSortOrder('desc');
    }
  };

  // Detect import subfolder from release title: "Patches", "DLC", or null (main game)
  const detectImportSubfolder = (title?: string): string | null => {
    if (!title) return null;
    const t = title.toLowerCase();
    if (/\bdlc\b/.test(t) || /[-.]dlc[-.]/i.test(title)) return 'DLC';
    if (/\bupdate\b/.test(t) || /\bpatch\b/.test(t) || /\bhotfix\b/.test(t)) return 'Patches';
    return null;
  };

  // Open platform selection modal before download
  const handleDownloadWithPlatform = (url: string, protocol?: string, detectedPlatform?: string, platformFolder?: string, releaseTitle?: string) => {
    setPendingDownload({ url, protocol, detectedPlatform, platformFolder, releaseTitle });
    setSelectedPlatform(platformFolder || 'windows');
    setShowPlatformModal(true);
  };

  // Confirm download with selected platform
  const confirmDownload = async () => {
    if (!pendingDownload || downloadingUrl) return;

    setShowPlatformModal(false);
    setDownloadingUrl(pendingDownload.url);
    
    try {
      const response = await apiClient.post('/downloadclient/add', {
        url: pendingDownload.url,
        protocol: pendingDownload.protocol,
        platformFolder: selectedPlatform,
        gameId: game?.id,
        importSubfolder: detectImportSubfolder(pendingDownload.releaseTitle)
      });
      setNotification({ message: response.data.message || t('downloadStarted'), type: 'success' });
    } catch (error: unknown) {
      console.error('Download failed:', error);
      setNotification({ message: getErrorMessage(error, t('failedToDownload')), type: 'error' });
    } finally {
      setDownloadingUrl(null);
      setPendingDownload(null);
    }
  };

  const sortedResults = [...results].sort((a, b) => {
    if (!sortField) return 0;

    const aValue = a[sortField];
    const bValue = b[sortField];

    // Handle nulls
    if (aValue === undefined || aValue === null) return 1;
    if (bValue === undefined || bValue === null) return -1;

    if (typeof aValue === 'string' && typeof bValue === 'string') {
      return sortOrder === 'asc'
        ? aValue.localeCompare(bValue)
        : bValue.localeCompare(aValue);
    }

    if (typeof aValue === 'number' && typeof bValue === 'number') {
      return sortOrder === 'asc' ? aValue - bValue : bValue - aValue;
    }

    return 0;
  });

  const getSortIcon = (field: keyof TorrentResult) => {
    if (sortField !== field) return <FontAwesomeIcon icon={faSort} style={{ opacity: 0.3, marginLeft: '5px' }} />;
    return sortOrder === 'asc' ? <FontAwesomeIcon icon={faSortUp} style={{ marginLeft: '5px' }} /> : <FontAwesomeIcon icon={faSortDown} style={{ marginLeft: '5px' }} />;
  };

  const getSeedersClass = (seeders?: number) => {
    if (!seeders || seeders === 0) return 'danger';
    if (seeders > 50) return 'excellent';
    if (seeders > 10) return 'good';
    return 'warning';
  };

  // Platform configurations for release search
  // Steam, GOG Galaxy, and generic PC games all use PC categories
  const PC_CONFIG = {
    categories: [4000, 4010, 4050],
    keywords: ['PC', 'WINDOWS', 'WIN64', 'WIN32', '.EXE', 'WINE', 'GOG-GAMES', 'GOG', 'STEAM', 'CRACK', 'REPACK', 'FITGIRL', 'DODI', 'ELAMIGOS', 'RAZOR1911', 'CODEX', 'SKIDROW', 'PLAZA'],
    negativeKeywords: ['PS3', 'PS4', 'PS5', 'SWITCH', 'XBOX', 'NSW', 'NSP', 'XCI'],
    extensions: ['.exe', '.iso', '.bin', '.rar', '.zip', '.7z'],
    color: 'var(--ctp-green)'
  };

  const PLATFORM_CONFIG: Record<string, { categories: number[], keywords: string[], negativeKeywords: string[], extensions: string[], color: string }> = {
    // All PC-based platforms share the same config
    'PC': PC_CONFIG,
    'Steam': PC_CONFIG,
    'GOG': PC_CONFIG,
    'GOG Galaxy': PC_CONFIG,
    'Windows': PC_CONFIG,
    'Nintendo Switch': {
      categories: [1000, 1030],
      keywords: ['SWITCH', 'NSW', 'NSP', 'XCI', 'NSZ'],
      negativeKeywords: ['PS4', 'PC', 'XBOX', 'WII'],
      extensions: ['.nsp', '.xci', '.nsz'],
      color: 'var(--ctp-red)'
    },
    'PlayStation 4': {
      categories: [1000, 1080],
      keywords: ['PS4', 'PLAYSTATION 4', 'CUSA', 'PKG'],
      negativeKeywords: ['PS5', 'PC', 'SWITCH'],
      extensions: ['.pkg'],
      color: 'var(--ctp-blue)'
    },
    'PlayStation 5': {
      categories: [1000],
      keywords: ['PS5', 'PLAYSTATION 5', 'PPSA'],
      negativeKeywords: ['PS4', 'PC', 'SWITCH'],
      extensions: [],
      color: 'var(--ctp-blue)'
    },
    'Xbox One': {
      categories: [1000],
      keywords: ['XBOX ONE', 'XB1'],
      negativeKeywords: ['PS4', 'PC', 'SWITCH'],
      extensions: [],
      color: 'var(--ctp-green)'
    },
    'Xbox Series': {
      categories: [1000],
      keywords: ['XBOX SERIES', 'XBSX', 'XSX'],
      negativeKeywords: ['PS4', 'PC', 'SWITCH'],
      extensions: [],
      color: 'var(--ctp-green)'
    }
  };

  type PlatformType = 'PC' | 'PlayStation' | 'Xbox' | 'Nintendo' | 'Unknown';

  const GetPlatformInfo = (categoryId: number): { name: string, icon: string, type: PlatformType } => {
    switch (categoryId) {
      // ==========================================
      // 🖥️ PC & MAC
      // ==========================================
      case 4000: // PC General
      case 4010: // PC 0day
      case 4020: // PC ISO
      case 4040: // PC Mobile
      case 4050: // PC Games (Standard)
      case 14050: // PC Games (Extended)
      case 100400: // TPB PC General
      case 100401: // TPB PC
      case 104050: // User specific extended
        return { name: "PC", icon: "mdi-microsoft-windows", type: 'PC' };

      case 4030: // Mac
      case 100402: // TPB Mac
        return { name: "Mac", icon: "mdi-apple", type: 'PC' };

      // ==========================================
      // 🔵 SONY PLAYSTATION
      // ==========================================
      case 1080: // PS3
      case 101080: // PS3 Extended
      case 100403: // TPB PSx (A veces mezcla)
        return { name: "PS3", icon: "mdi-sony-playstation", type: 'PlayStation' };

      case 1180: // PS4 (Standard Newznab)
      case 101100: // PS4 (Extended)
        return { name: "PS4", icon: "mdi-sony-playstation", type: 'PlayStation' };

      case 1020: // PSP
      case 101020:
        return { name: "PSP", icon: "mdi-sony-playstation", type: 'PlayStation' };

      case 1120: // PS Vita
      case 101120:
        return { name: "PS Vita", icon: "mdi-sony-playstation", type: 'PlayStation' };

      // ==========================================
      // 🟢 MICROSOFT XBOX
      // ==========================================
      case 1040: // Xbox Original
      case 101040:
        return { name: "Xbox", icon: "mdi-microsoft-xbox", type: 'Xbox' };

      case 1050: // Xbox 360
      case 101050:
      case 1070: // 360 DLC
      case 100404: // TPB Xbox360
        return { name: "Xbox 360", icon: "mdi-microsoft-xbox", type: 'Xbox' };

      case 1140: // Xbox One
      case 101090: // Xbox One Extended
        return { name: "Xbox One", icon: "mdi-microsoft-xbox", type: 'Xbox' };

      // ==========================================
      // 🔴 NINTENDO
      // ==========================================
      case 101035: // Switch (El ID más común ahora)
      case 101110: // Switch Alternativo
      case 101111: // Switch Update/DLC
        return { name: "Switch", icon: "mdi-nintendo-switch", type: 'Nintendo' };

      case 1030: // Wii
      case 101030:
      case 100405: // TPB Wii
        return { name: "Wii", icon: "mdi-nintendo-wii", type: 'Nintendo' };

      case 1130: // Wii U
      case 101130:
        return { name: "Wii U", icon: "mdi-nintendo-wiiu", type: 'Nintendo' };

      case 1010: // NDS
      case 101010:
        return { name: "DS", icon: "mdi-nintendo-game-boy", type: 'Nintendo' };

      case 1110: // 3DS
        return { name: "3DS", icon: "mdi-nintendo-3ds", type: 'Nintendo' };

      // ==========================================
      // 📦 OTROS / GENÉRICOS
      // ==========================================
      case 1000: // Console General
        return { name: "Console", icon: "mdi-gamepad-variant", type: 'Unknown' };

      default:
        // Si es un 1xxx desconocido, es consola
        if (categoryId >= 1000 && categoryId < 2000) return { name: "Console", icon: "mdi-gamepad-variant", type: 'Unknown' };
        // Si es un 4xxx desconocido, es PC
        if (categoryId >= 4000 && categoryId < 5000) return { name: "PC", icon: "mdi-laptop", type: 'PC' };

        return { name: "Unknown", icon: "mdi-help-circle", type: 'Unknown' };
    }
  };

  const SCENE_GROUPS = ['FLT', 'CODEX', 'RUNE', 'TENOKE', 'SKIDROW', 'RELOADED', 'PROPHET', 'CPY', 'EMPRESS', 'RAZOR1911', 'GOLDBERG'];
  const REPACK_GROUPS = ['FITGIRL', 'DODI', 'ELAMIGOS', 'KAOS', 'XATAB'];

  const analyzeTorrent = (title: string) => {
    const t = title.toUpperCase();
    let detectedPlatform = 'Game';
    let confidence: 'match' | 'mismatch' | 'unknown' = 'unknown';
    const tags: string[] = [];

    // Detect Platform
    for (const [platformName, config] of Object.entries(PLATFORM_CONFIG)) {
      const hasKeyword = config.keywords.some(k => t.includes(k));
      const hasNegative = config.negativeKeywords.some(k => t.includes(k));

      if (hasKeyword && !hasNegative) {
        detectedPlatform = platformName;
        break;
      }
    }

    // Special case for generic PC keywords if not found
    if (detectedPlatform === 'Game') {
      if (t.includes('LINUX') || t.includes('WINE')) detectedPlatform = 'Linux';
    }

    // Determine Confidence relative to current game
    if (game?.platform) {
      if (detectedPlatform === game.platform.name ||
        (game.platform.name.includes('PC') && detectedPlatform === 'PC') ||
        (game.platform.name.includes('Switch') && detectedPlatform === 'Nintendo Switch')) {
        confidence = 'match';
      } else if (detectedPlatform !== 'Game' && detectedPlatform !== 'Linux') {
        confidence = 'mismatch';
      }
    }

    // Extract Extra Tags
    if (SCENE_GROUPS.some(g => t.includes(g))) tags.push('Scene');
    if (REPACK_GROUPS.some(g => t.includes(g))) tags.push('Repack');
    if (t.includes('FIX')) tags.push('Fix');
    if (t.includes('UPDATE')) tags.push('Update');
    if (t.includes('GOG')) tags.push('GOG');
    if (t.includes('STEAM')) tags.push('Steam');

    return { detectedPlatform, confidence, tags };
  };

  const handleSearchTorrents = async (overrideQuery?: string) => {
    if (!game) return;
    setSearching(true);
    setHasSearched(true);
    setResults([]);
    setError(null);

    const searchQuery = overrideQuery || customSearchQuery || game.title;
    if (!customSearchQuery) setCustomSearchQuery(game.title);

    const platformSlug = game.platform?.slug?.toLowerCase();

    // Determine if this is a PC-based platform (Steam, GOG, Windows, etc.)
    const isPCPlatform = platformSlug === 'pc' || platformSlug === 'steam' || platformSlug === 'steamos' || 
                         platformSlug === 'gog' || platformSlug === 'windows' ||
                         game.platform?.name?.toLowerCase().includes('pc') ||
                         game.platform?.name?.toLowerCase().includes('steam') ||
                         game.platform?.name?.toLowerCase().includes('gog') ||
                         game.platform?.name?.toLowerCase().includes('windows');

    console.log('[Release Search] Platform:', game.platform?.name, 'Slug:', platformSlug, 'Is PC:', isPCPlatform);
    console.log('[Release Search] Game IDs - Steam:', game.steamId || 'none', 'GOG:', game.gogId || 'none');

    // For GOG games with a GOG ID, fetch GOG downloads AND search for releases
    let gogDownloads: GogDownloadResult[] = [];
    if (game.gogId) {
      console.log('[Release Search] GOG ID found, fetching owned downloads for:', game.gogId);
      try {
        const response = await apiClient.get(`/settings/gog/downloads/${game.gogId}`);
        console.log('[Release Search] GOG API response:', response.data);
        if (response.data.success && response.data.downloads && response.data.downloads.length > 0) {
          gogDownloads = response.data.downloads.map((dl: Record<string, unknown>, index: number) => ({
            title: `[GOG] ${game.title} - ${dl.name || dl.platform || 'Download'} ${dl.version || ''}`.trim(),
            guid: `gog-${game.gogId}-${index}`,
            size: (dl.size as number) || 0,
            indexerName: 'GOG Galaxy (Owned)',
            provider: 'GOG',
            protocol: 'gog',
            downloadUrl: dl.manualUrl || dl.downloadUrl,
            formattedSize: dl.size ? `${((dl.size as number) / 1024 / 1024 / 1024).toFixed(2)} GB` : 'Unknown',
            gogDownload: dl,
            isOwned: true // Mark as owned for UI highlighting
          }));
          console.log('[Release Search] GOG downloads found:', gogDownloads.length);
        }
      } catch (err: unknown) {
        const errorMsg = getErrorMessage(err);
        console.log('[Release Search] GOG downloads error:', errorMsg);
        console.log('[Release Search] Hint: Re-authenticate GOG in Settings -> GOG if token expired');
      }
    }

    // Get categories based on platform - PC platforms all use PC categories
    let cats = '';
    let config = null;
    
    if (isPCPlatform) {
      // All PC-based platforms (Steam, GOG, Windows, etc.) use PC categories
      config = PC_CONFIG;
    } else if (game.platform) {
      const platformName = game.platform.name;
      
      // Try exact match first, then partial match for console platforms
      config = PLATFORM_CONFIG[platformName];
      if (!config) {
        if (platformSlug === 'switch' || platformSlug === 'switch2' || platformName.includes('Switch')) {
          config = PLATFORM_CONFIG['Nintendo Switch'];
        } else if (platformSlug === 'ps4' || platformName.includes('PlayStation 4')) {
          config = PLATFORM_CONFIG['PlayStation 4'];
        } else if (platformSlug === 'ps5' || platformName.includes('PlayStation 5')) {
          config = PLATFORM_CONFIG['PlayStation 5'];
        } else if (platformSlug === 'xboxone' || platformName.includes('Xbox One')) {
          config = PLATFORM_CONFIG['Xbox One'];
        } else if (platformSlug === 'xboxseriesx' || platformName.includes('Xbox Series')) {
          config = PLATFORM_CONFIG['Xbox Series'];
        }
      }
    }
    
    if (config) {
      cats = config.categories.join(',');
    }

    console.log('[Release Search] Categories:', cats);

    try {
      console.log('[Release Search] Making API request to /api/v3/search with query:', searchQuery, 'categories:', cats);
      const response = await apiClient.get('/search', {
        params: { query: searchQuery, categories: cats },
        timeout: 120000 // 120 second timeout to match backend
      });
      console.log('[Release Search] API response:', response.data);

      // Handle both old flat-array format and new structured format
      let indexerResults: TorrentResult[] = [];
      if (Array.isArray(response.data)) {
        // Legacy flat array response
        indexerResults = response.data;
        setSearchDiagnostics(null);
      } else if (response.data && typeof response.data === 'object') {
        // New structured response: { results, providers, diagnostics }
        indexerResults = response.data.results || [];
        setSearchDiagnostics({
          providers: response.data.providers || [],
          diagnostics: response.data.diagnostics || { configured: true }
        });
        // Log provider diagnostics
        if (response.data.providers) {
          for (const p of response.data.providers) {
            console.log(`[Release Search] Provider ${p.name}: ${p.status}${p.error ? ' — ' + p.error : ''} (${p.count ?? 0} results)`);
          }
        }
        if (response.data.diagnostics?.message) {
          console.log('[Release Search] Diagnostics:', response.data.diagnostics.message);
        }
      }

      console.log('[Release Search] Results count:', indexerResults.length);
      
      // Combine GOG downloads (if any) with indexer results
      // GOG owned downloads appear first
      const combinedResults = [...gogDownloads, ...indexerResults] as TorrentResult[];
      console.log('[Release Search] Combined results:', combinedResults.length);
      setResults(combinedResults);
    } catch (err: unknown) {
      console.error('[Search] Error:', err);
      if (isTimeoutError(err)) {
        setError(t('searchTimeout') || 'Search timed out. Please try again.');
      } else {
        setError(getErrorMessage(err, t('error')));
      }
    } finally {
      setSearching(false);
    }
  };

  const handleCorrectionSave = async (updates: Record<string, unknown>) => {
    if (!game) return;
    try {
      await apiClient.put(`/game/${game.id}`, updates);
      setNotification({ message: t('gameUpdated'), type: 'success' });
      setShowCorrectionModal(false);
      // Reload game to reflect changes
      const response = await apiClient.get(`/game/${game.id}`, { params: { lang: language } });
      setGame(response.data);
    } catch (err: unknown) {
      console.error(err);
      setNotification({ message: getErrorMessage(err, t('errorUpdating')), type: 'error' });
    }
  };
  const handleInstallClick = () => {
    setShowInstallWarning(true);
  };

  const confirmInstall = async () => {
    setShowInstallWarning(false);
    try {
      setNotification({ message: t('searchingInstaller'), type: 'info' });
      const res = await apiClient.post(`/game/${id}/install`);
      setNotification({ message: `${t('installerLaunched')}: ${res.data.path}`, type: 'success' });
    } catch (err: unknown) {
      console.error(err);
      setNotification({ message: getErrorMessage(err, t('errorLaunchingInstaller')), type: 'error' });
    }
  };

  const handlePlay = async () => {
    console.log('[handlePlay] Clicked. Game:', game);
    console.log('[handlePlay] SteamId:', game?.steamId, 'GogId:', game?.gogId, 'Platform:', game?.platform?.slug);
    
    const platformSlug = game?.platform?.slug?.toLowerCase();
    
    // For Steam/SteamOS platforms with a Steam ID, use steam:// protocol
    if ((platformSlug === 'steam' || platformSlug === 'steamos') && game?.steamId) {
      const steamUrl = `steam://rungameid/${game.steamId}`;
      console.log('[handlePlay] Launching via Steam protocol:', steamUrl);
      setNotification({ message: t('launchingViaSteam') || 'Launching via Steam...', type: 'info' });
      window.open(steamUrl, '_self');
      setTimeout(() => setNotification({ message: t('gameLaunched'), type: 'success' }), 1000);
      return;
    }
    
    // For GOG Galaxy platform with a GOG ID, use goggalaxy:// protocol
    if (platformSlug === 'gog' && game?.gogId) {
      const gogUrl = `goggalaxy://openGameView/${game.gogId}`;
      console.log('[handlePlay] Launching via GOG Galaxy protocol:', gogUrl);
      setNotification({ message: t('launchingViaGog') || 'Launching via GOG Galaxy...', type: 'info' });
      window.open(gogUrl, '_self');
      setTimeout(() => setNotification({ message: t('gameLaunched'), type: 'success' }), 1000);
      return;
    }
    
    // Fallback to backend launch for other games
    try {
      setNotification({ message: t('launchingGame'), type: 'info' });
      await apiClient.post(`/game/${id}/play`);
      setNotification({ message: t('gameLaunched'), type: 'success' });
    } catch (err: unknown) {
      console.error(err);
      setNotification({ message: getErrorMessage(err, t('errorLaunchingGame')), type: 'error' });
    }
  };

  const handleRunUninstaller = async () => {
    try {
      setNotification({ message: t('launchingUninstaller') || 'Launching Uninstaller...', type: 'info' });
      await apiClient.post(`/game/${id}/uninstall`);
      setNotification({ message: t('uninstallerLaunched') || 'Uninstaller Launched', type: 'success' });
    } catch (err: unknown) {
      console.error(err);
      setNotification({ message: getErrorMessage(err, t('errorLaunchingUninstaller') || 'Error launching uninstaller'), type: 'error' });
    }
  };

  const handleDeleteGame = async (deleteLibraryFiles: boolean, deleteDownloadFiles: boolean, targetLibraryPath?: string, targetDownloadPath?: string) => {
    if (!id) return;
    try {
      setNotification({ message: t('deletingGame') || 'Deleting...', type: 'info' });
      let url = `/game/${id}?deleteFiles=${deleteLibraryFiles}&deleteDownloadFiles=${deleteDownloadFiles}`;
      if (targetLibraryPath) url += `&targetPath=${encodeURIComponent(targetLibraryPath)}`;
      if (targetDownloadPath) url += `&downloadPath=${encodeURIComponent(targetDownloadPath)}`;

      await apiClient.delete(url);
      setNotification({ message: t('gameDeleted') || 'Game Deleted', type: 'success' });
      // Redirect to library after short delay
      setTimeout(() => {
        navigate(backUrl);
      }, 1000);
    } catch (err: unknown) {
      console.error(err);
      setNotification({ message: getErrorMessage(err, t('errorDeletingGame') || 'Error deleting game'), type: 'error' });
    }
  };

  if (loading) {
    return <div className="game-details"><p>{t('loadingGame')}</p></div>;
  }

  // Smart back link: navigate to the platform-filtered library the user came from
  const locationState = location.state as { fromPlatformId?: string; fromPlatformName?: string } | null;
  const backPlatformId = locationState?.fromPlatformId || game?.platformId?.toString() || '';
  const backPlatformName = locationState?.fromPlatformName || game?.platform?.name || '';
  const backUrl = backPlatformId ? `/library?platform=${backPlatformId}` : '/library';
  const backLabel = backPlatformName 
    ? `${t('backToLibrary')} — ${backPlatformName}`
    : t('backToLibrary');

  if (error || !game) {
    return (
      <div className="game-details">
        <p>{error || t('gameNotFound')}</p>
        <Link to="/library">{t('backToLibrary')}</Link>
      </div>
    );
  }

  const isSwitchGame = (() => {
    if (!game) return false;
    const pathLower = game.path?.toLowerCase() || '';
    const isSwitchFile = pathLower.endsWith('.nsp') || pathLower.endsWith('.xci') || pathLower.endsWith('.nsz') || pathLower.endsWith('.xcz');
    const isSwitchPlatform = game.platform?.name?.toLowerCase().includes('switch') || false;
    return isSwitchFile || isSwitchPlatform;
  })();

  const platformClass = (() => {
    const raw = (game.platform?.slug || game.platform?.name || '').toLowerCase();
    return raw ? 'plat-' + raw.replace(/[^a-z0-9]+/g, '') : 'plat-default';
  })();

  return (
    <div className={`game-details ${platformClass}`}>
      {/* Hero Banner with Dynamic Background */}
      {(game.images.backgroundUrl || game.images.screenshots?.[0]) && (
        <div className="game-hero-banner">
          <div 
            className="game-hero-background"
            style={{ 
              backgroundImage: `url(${game.images.backgroundUrl || game.images.screenshots?.[0]})` 
            }}
          />
        </div>
      )}
      
      <div className="game-details-header">
        <div className="game-details-poster">
          {game.images.coverUrl ? (
            <img src={game.images.coverUrl} alt={game.title} />
          ) : (
            <div className="placeholder">?</div>
          )}
        </div>
        <div className="game-details-info">
          <div className="title-row" style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
            <h1>{game.title}</h1>
          </div>

          <div className="game-actions-menu">
            <button
              className={`action-btn ${activeTab === 'search' ? 'active' : ''}`}
              onClick={() => {
                setActiveTab('search');
                handleSearchTorrents();
              }}
              title={t('searchLinks')}
            >
              <FontAwesomeIcon icon={faSearch} />
              <span>{t('search')}</span>
            </button>
            <button
              className={`action-btn ${activeTab === 'files' ? 'active' : ''}`}
              onClick={() => setActiveTab(activeTab === 'files' ? 'none' : 'files')}
              title={t('gameFiles') || 'Game Files'}
            >
              <FontAwesomeIcon icon={faFile} />
              <span>{t('files') || 'Files'}</span>
            </button>
            {game && typeof game.platformId === 'number' && [1, 2, 46, 47].includes(game.platformId) && (
              <button
                className={`action-btn ${activeTab === 'patches' ? 'active' : ''}`}
                onClick={() => setActiveTab(activeTab === 'patches' ? 'none' : 'patches')}
                title={t('patches')}
              >
                <FontAwesomeIcon icon={faDownload} />
                <span>{t('patches')}</span>
              </button>
            )}
            <button
              className={`action-btn ${activeTab === 'media' ? 'active' : ''}`}
              onClick={() => setActiveTab(activeTab === 'media' ? 'none' : 'media')}
              title={t('localMedia') || 'Local Media'}
            >
              <FontAwesomeIcon icon={faFolder} />
              <span>{t('localMedia') || 'Media'}</span>
            </button>
            <button
              className="action-btn"
              onClick={() => setShowCorrectionModal(true)}
              title={t('correctMetadata')}
            >
              <FontAwesomeIcon icon={faPen} />
              <span>{t('correct')}</span>
            </button>

            <button
              className="action-btn"
              onClick={() => setShowUninstallModal(true)}
              title={t('uninstallTitle')}
            >
              <FontAwesomeIcon icon={faTrash} />
              <span>{t('remove')}</span>
            </button>

            {(!isSwitchGame) && (
              <button
                className={`action-btn ${game.isInstallable && !game.canPlay ? 'install-ready' : ''}`}
                onClick={handleInstallClick}
                title={t('install')}
              >
                <FontAwesomeIcon icon={faDownload} />
                <span>{t('install')}</span>
              </button>
            )}

            {isSwitchGame && (
              <button
                className="action-btn switch-usb"
                onClick={() => setShowSwitchModal(true)}
                title="Install to Switch via USB"
                style={{ background: 'var(--ctp-red)', color: 'white' }}
              >
                <FontAwesomeIcon icon={faMicrochip} />
                <span>USB Install</span>
              </button>
            )}

            {(!isSwitchGame) && (
              <button
                className={`action-btn ${game.canPlay || isWebPlayable ? 'play-ready' : ''}`}
                onClick={isWebPlayable ? handlePlayInBrowser : handlePlay}
                title={isWebPlayable ? 'Play in Browser' : t('play')}
                style={isWebPlayable ? { background: 'linear-gradient(135deg, var(--ctp-green), var(--ctp-teal))', color: 'var(--ctp-base)' } : {}}
              >
                <FontAwesomeIcon icon={isWebPlayable ? faPlay : faGamepad} />
                <span>{isWebPlayable ? 'Play' : t('play')}</span>
              </button>
            )}

            {/* Linux Gaming Exports */}
            {game.canPlay && (
              <div style={{ position: 'relative', display: 'inline-block' }}>
                <button
                  className="action-btn"
                  onClick={() => setShowLinuxExport(!showLinuxExport)}
                  title="Linux Gaming Exports"
                  style={{ background: 'var(--ctp-surface1)', color: 'var(--ctp-text)' }}
                >
                  <FontAwesomeIcon icon={faDownload} />
                  <span>Export</span>
                </button>
                {showLinuxExport && (
                  <div style={{
                    position: 'absolute', top: '100%', left: 0, zIndex: 100,
                    background: 'var(--ctp-surface0)', border: '1px solid var(--ctp-surface2)',
                    borderRadius: '8px', padding: '6px 0', minWidth: '200px', marginTop: '4px',
                    boxShadow: '0 4px 12px rgba(0,0,0,0.3)'
                  }}>
                    <button
                      style={{ display: 'block', width: '100%', padding: '8px 16px', background: 'none', border: 'none', color: 'var(--ctp-text)', cursor: 'pointer', textAlign: 'left', fontSize: '13px' }}
                      onClick={() => { window.open(`/api/v3/linux/export/lutris/${game.id}`, '_blank'); setShowLinuxExport(false); }}
                    >
                      Lutris Installer YAML
                    </button>
                    <button
                      style={{ display: 'block', width: '100%', padding: '8px 16px', background: 'none', border: 'none', color: 'var(--ctp-text)', cursor: 'pointer', textAlign: 'left', fontSize: '13px' }}
                      onClick={() => { window.open(`/api/v3/linux/export/steam-shortcut/${game.id}`, '_blank'); setShowLinuxExport(false); }}
                    >
                      Steam Shortcut (Deck)
                    </button>
                    <button
                      style={{ display: 'block', width: '100%', padding: '8px 16px', background: 'none', border: 'none', color: 'var(--ctp-text)', cursor: 'pointer', textAlign: 'left', fontSize: '13px' }}
                      onClick={() => { window.open(`/api/v3/linux/export/desktop-entry/${game.id}`, '_blank'); setShowLinuxExport(false); }}
                    >
                      .desktop Launcher
                    </button>
                    <hr style={{ border: 'none', borderTop: '1px solid var(--ctp-surface2)', margin: '4px 0' }} />
                    <div style={{ padding: '8px 16px', fontSize: '12px', color: 'var(--ctp-subtext0)' }}>
                      Runner:
                      <select
                        value={selectedRunner}
                        onChange={async (e) => {
                          setSelectedRunner(e.target.value);
                          try {
                            await apiClient.put(`/game/${game.id}`, { preferredRunner: e.target.value });
                          } catch { /* best effort */ }
                        }}
                        style={{ marginLeft: '8px', background: 'var(--ctp-surface1)', color: 'var(--ctp-text)', border: '1px solid var(--ctp-surface2)', borderRadius: '4px', padding: '2px 6px', fontSize: '12px' }}
                      >
                        <option value="auto">Auto-detect</option>
                        <option value="proton">Proton</option>
                        <option value="wine">Wine</option>
                        <option value="native">Native</option>
                      </select>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
          <div className="meta" style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
            <span>{game.year}</span>
            {game.platform && <span>{game.platform.name}</span>}
            
            {/* Score Badges - inline with meta */}
            {game.rating && (
              <ScoreCircle
                score={Math.round(game.rating)}
                size="small"
                label="IGDB"
              />
            )}
            {review?.metacriticScore && (
              <ScoreCircle
                score={review.metacriticScore}
                size="small"
                label="META"
                href={review.metacriticUrl || '#'}
              />
            )}
            {review?.openCriticScore && (
              <ScoreCircle
                score={review.openCriticScore}
                size="small"
                label="OC"
                href={review.openCriticUrl || '#'}
              />
            )}
            {review?.userRating && review.userRating > 0 && (
              <ScoreCircle
                score={review.userRating * 20}
                maxScore={100}
                size="small"
                label="YOU"
              />
            )}
            {game.protonDbTier && (
              <a
                href={game.steamId ? `https://www.protondb.com/app/${game.steamId}` : '#'}
                target="_blank"
                rel="noopener noreferrer"
                style={{ textDecoration: 'none' }}
                title="View on ProtonDB"
              >
                <ProtonDbBadge tier={game.protonDbTier} size="large" showLabel />
              </a>
            )}
          </div>

          {game.availablePlatforms && game.availablePlatforms.length > 0 && (
            <div className="platforms-list" style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', marginTop: '8px', marginBottom: '8px' }}>
              {game.availablePlatforms.map(p => (
                <span key={p} style={{
                  backgroundColor: 'rgba(255, 255, 255, 0.1)',
                  padding: '2px 8px',
                  borderRadius: '4px',
                  fontSize: '0.8rem',
                  color: 'var(--ctp-text)',
                  border: '1px solid rgba(255, 255, 255, 0.05)'
                }}>
                  {p}
                </span>
              ))}
            </div>
          )}

          {/* External IDs */}
          {(game.steamId || game.gogId) && (
            <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', marginTop: '8px', marginBottom: '8px' }}>
              {game.steamId && (
                <a 
                  href={`https://store.steampowered.com/app/${game.steamId}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px',
                    backgroundColor: 'var(--ctp-crust)',
                    padding: '4px 10px',
                    borderRadius: '4px',
                    fontSize: '0.75rem',
                    color: 'var(--ctp-blue)',
                    textDecoration: 'none',
                    border: '1px solid #66c0f4'
                  }}
                >
                  <span>🎮</span> Steam ID: {game.steamId}
                </a>
              )}
              {game.steamId && game.protonDbTier && (
                <a
                  href={`https://www.protondb.com/app/${game.steamId}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px',
                    backgroundColor: 'var(--ctp-crust)',
                    padding: '4px 10px',
                    borderRadius: '4px',
                    fontSize: '0.75rem',
                    color: 'var(--ctp-subtext0)',
                    textDecoration: 'none',
                    border: '1px solid var(--ctp-surface2)'
                  }}
                  title="View on ProtonDB"
                >
                  <ProtonDbBadge tier={game.protonDbTier} size="medium" showLabel />
                  <span style={{ color: 'var(--ctp-subtext1)', fontSize: '0.7rem' }}>ProtonDB</span>
                </a>
              )}
              {game.gogId && (
                <a 
                  href={`https://www.gog.com/game/${game.gogId}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px',
                    backgroundColor: 'var(--ctp-mantle)',
                    padding: '4px 10px',
                    borderRadius: '4px',
                    fontSize: '0.75rem',
                    color: 'var(--ctp-green)',
                    textDecoration: 'none',
                    border: '1px solid var(--ctp-green)'
                  }}
                >
                  <span>🟣</span> GOG ID: {game.gogId}
                </a>
              )}
            </div>
          )}

          {game.genres && game.genres.length > 0 && (
            <div className="genres">
              {game.genres.join(' / ')}
            </div>
          )}
          {(game.region || game.languages || game.revision) && (
            <div className="region" style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '6px', flexWrap: 'wrap' }}>
              {(game.region || game.languages) && (
                <RegionFlag region={game.region} languages={game.languages} size="medium" showLabel />
              )}
              {game.revision && (
                <span style={{
                  background: 'var(--ctp-surface1)',
                  color: '#fab387',
                  fontSize: '12px',
                  fontWeight: 600,
                  padding: '2px 8px',
                  borderRadius: '4px',
                  whiteSpace: 'nowrap'
                }}>{game.revision}</span>
              )}
            </div>
          )}
          {game.overview && (
            <div className="overview" dangerouslySetInnerHTML={{ __html: sanitizeHtml(game.overview) }} />
          )}

          {/* User Review Section */}
          <div className="user-review-section" style={{ marginTop: '20px', padding: '15px', backgroundColor: 'rgba(49, 50, 68, 0.5)', borderRadius: '8px' }}>
            <div style={{ display: 'flex', gap: '20px', flexWrap: 'wrap', alignItems: 'center' }}>
              {/* Favorite */}
              <button 
                onClick={handleToggleFavorite}
                style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: '24px', color: review?.isFavorite ? 'var(--ctp-red)' : 'var(--ctp-overlay0)' }}
                title={t('toggleFavorite') || 'Toggle Favorite'}
              >
                <FontAwesomeIcon icon={faHeart} />
              </button>

              {/* User Rating */}
              <div style={{ display: 'flex', alignItems: 'center', gap: '5px' }}>
                <span style={{ color: 'var(--ctp-subtext0)', marginRight: '5px' }}>{t('yourRating') || 'Your Rating'}:</span>
                {[1, 2, 3, 4, 5].map(star => (
                  <button
                    key={star}
                    onClick={() => handleSetRating(star * 20)}
                    style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: '18px', color: (review?.userRating || 0) >= star * 20 ? 'var(--ctp-yellow)' : 'var(--ctp-overlay0)' }}
                  >
                    <FontAwesomeIcon icon={faStar} />
                  </button>
                ))}
                {review?.userRating && <span style={{ marginLeft: '8px', color: 'var(--ctp-text)' }}>{review.userRating}%</span>}
              </div>

              {/* Completion Status */}
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <FontAwesomeIcon icon={faTrophy} style={{ color: 'var(--ctp-subtext0)' }} />
                <select
                  value={review?.completionStatus || 0}
                  onChange={(e) => handleSetCompletionStatus(parseInt(e.target.value))}
                  style={{ backgroundColor: 'var(--ctp-surface0)', border: '1px solid var(--ctp-surface1)', borderRadius: '4px', padding: '4px 8px', color: 'var(--ctp-text)' }}
                >
                  <option value={0}>{t('notPlayed') || 'Not Played'}</option>
                  <option value={1}>{t('playing') || 'Playing'}</option>
                  <option value={2}>{t('onHold') || 'On Hold'}</option>
                  <option value={3}>{t('dropped') || 'Dropped'}</option>
                  <option value={4}>{t('completed') || 'Completed'}</option>
                  <option value={5}>{t('mastered') || 'Mastered'}</option>
                </select>
              </div>
            </div>

            {/* Notes */}
            <div style={{ marginTop: '15px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '8px' }}>
                <FontAwesomeIcon icon={faStickyNote} style={{ color: 'var(--ctp-subtext0)' }} />
                <span style={{ color: 'var(--ctp-subtext0)' }}>{t('notes') || 'Notes'}</span>
                <button
                  onClick={() => { setEditedNotes(review?.notes || ''); setShowNotesEditor(true); }}
                  style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--ctp-blue)', fontSize: '12px' }}
                >
                  <FontAwesomeIcon icon={faPen} /> {t('edit') || 'Edit'}
                </button>
              </div>
              {review?.notes ? (
                <p style={{ color: 'var(--ctp-text)', fontSize: '14px', lineHeight: '1.5' }}>{review.notes}</p>
              ) : (
                <p style={{ color: 'var(--ctp-overlay0)', fontSize: '14px', fontStyle: 'italic' }}>{t('noNotes') || 'No notes yet. Click Edit to add.'}</p>
              )}
            </div>

            {/* Tags */}
            <div style={{ marginTop: '15px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '8px' }}>
                <FontAwesomeIcon icon={faTag} style={{ color: 'var(--ctp-subtext0)' }} />
                <span style={{ color: 'var(--ctp-subtext0)' }}>{t('tags') || 'Tags'}</span>
                <button
                  onClick={() => setShowTagPicker(true)}
                  style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--ctp-blue)', fontSize: '12px' }}
                >
                  <FontAwesomeIcon icon={faPlus} /> {t('add') || 'Add'}
                </button>
              </div>
              <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                {tags.length === 0 ? (
                  <span style={{ color: 'var(--ctp-overlay0)', fontSize: '14px', fontStyle: 'italic' }}>{t('noTags') || 'No tags'}</span>
                ) : (
                  tags.map((tag: GameTag) => (
                    <span 
                      key={tag.id} 
                      style={{ 
                        backgroundColor: tag.color || 'var(--ctp-surface1)', 
                        padding: '4px 10px', 
                        borderRadius: '12px', 
                        fontSize: '12px',
                        color: 'var(--ctp-base)',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px'
                      }}
                    >
                      {tag.name}
                      <button 
                        onClick={() => handleRemoveTag(tag.id)}
                        style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--ctp-base)', padding: 0 }}
                      >
                        <FontAwesomeIcon icon={faTimes} style={{ fontSize: '10px' }} />
                      </button>
                    </span>
                  ))
                )}
              </div>
            </div>
          </div>
        </div>
      </div>

      {
        game && (
          <UninstallModal
            isOpen={showUninstallModal}
            onClose={() => setShowUninstallModal(false)}
            onRunUninstaller={handleRunUninstaller}
            onDelete={handleDeleteGame}
            gameTitle={game.title}
            gamePath={game.path}
            uninstallerPath={game.uninstallerPath}
            downloadPath={game.downloadPath}
          />
        )
      }

      {
        game && game.path && (
          <SwitchInstallerModal
            isOpen={showSwitchModal}
            onClose={() => setShowSwitchModal(false)}
            filePath={game.path}
            fileName={game.title} // Or get filename from path
          />
        )
      }

      {
        activeTab === 'search' && (
          <div className="torrent-search">

            {/* Editable search query field - shown after first search or always if hasSearched */}
            {hasSearched && !searching && (
              <div className="search-query-bar" style={{
                display: 'flex',
                gap: '8px',
                alignItems: 'center',
                marginBottom: '12px',
                padding: '8px 12px',
                background: 'var(--ctp-surface0)',
                borderRadius: '8px',
                border: '1px solid var(--ctp-surface1)'
              }}>
                <FontAwesomeIcon icon={faSearch} style={{ color: 'var(--ctp-overlay0)', flexShrink: 0 }} />
                <input
                  type="text"
                  value={customSearchQuery}
                  onChange={(e) => setCustomSearchQuery(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter') handleSearchTorrents(customSearchQuery); }}
                  placeholder={t('searchQuery') || 'Search query...'}
                  style={{
                    flex: 1,
                    background: 'var(--ctp-base)',
                    border: '1px solid var(--ctp-surface2)',
                    borderRadius: '6px',
                    padding: '8px 12px',
                    color: 'var(--ctp-text)',
                    fontSize: '14px',
                    outline: 'none'
                  }}
                />
                <button
                  onClick={() => handleSearchTorrents(customSearchQuery)}
                  disabled={!customSearchQuery.trim()}
                  style={{
                    padding: '8px 16px',
                    background: 'var(--ctp-blue)',
                    color: 'var(--ctp-base)',
                    border: 'none',
                    borderRadius: '6px',
                    cursor: 'pointer',
                    fontWeight: 600,
                    fontSize: '13px',
                    opacity: !customSearchQuery.trim() ? 0.5 : 1
                  }}
                >
                  {t('search') || 'Search'}
                </button>
              </div>
            )}

            {searching && (
              <div className="search-loading">
                <FontAwesomeIcon icon={faSearch} spin />
                <p>{t('searching') || 'Buscando...'}</p>
              </div>
            )}

            {error && <p className="error">{error}</p>}

            {/* Show "no results" message with diagnostics when search completed but found nothing */}
            {!searching && !error && results.length === 0 && hasSearched && (
              <div className="no-results-message" style={{
                padding: '20px',
                textAlign: 'center',
                color: 'var(--ctp-overlay0)',
                background: 'rgba(0,0,0,0.2)',
                borderRadius: '8px',
                marginTop: '10px'
              }}>
                <FontAwesomeIcon icon={faSearch} style={{ fontSize: '24px', marginBottom: '10px', opacity: 0.5 }} />
                <p style={{ margin: 0 }}>{t('noDownloadsFound') || 'No downloads found for this game.'}</p>

                {/* Show diagnostics if available */}
                {searchDiagnostics && !searchDiagnostics.diagnostics.configured && (
                  <p style={{ margin: '8px 0 0 0', fontSize: '13px', color: 'var(--ctp-peach)' }}>
                    {searchDiagnostics.diagnostics.message || t('noIndexersConfigured') || 'No indexers configured. Add Prowlarr, Jackett, or a Hydra source in Settings.'}
                  </p>
                )}

                {searchDiagnostics && searchDiagnostics.diagnostics.configured && searchDiagnostics.providers.length > 0 && (
                  <div style={{ margin: '10px auto 0', maxWidth: '400px', textAlign: 'left', fontSize: '12px' }}>
                    {searchDiagnostics.providers.map((p, idx) => (
                      <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '3px 0' }}>
                        <span style={{
                          width: '8px', height: '8px', borderRadius: '50%', flexShrink: 0,
                          background: p.status === 'ok' ? 'var(--ctp-green)' : p.status === 'timeout' ? 'var(--ctp-yellow)' : p.status === 'error' ? 'var(--ctp-red)' : 'var(--ctp-overlay0)'
                        }} />
                        <span style={{ fontWeight: 600 }}>{p.name}</span>
                        <span style={{ opacity: 0.7 }}>
                          {p.status === 'ok' && `${p.count ?? 0} results`}
                          {p.status === 'error' && (p.error || 'Connection failed')}
                          {p.status === 'timeout' && (p.error || 'Timed out')}
                          {p.status === 'not_configured' && (t('notConfigured') || 'Not configured')}
                          {p.status === 'disabled' && (t('disabled') || 'Disabled')}
                        </span>
                      </div>
                    ))}
                  </div>
                )}

                {(!searchDiagnostics || (searchDiagnostics.diagnostics.configured && searchDiagnostics.providers.every(p => p.status === 'ok'))) && (
                  <p style={{ margin: '5px 0 0 0', fontSize: '12px', opacity: 0.7 }}>
                    {t('tryDifferentQuery') || 'Try searching with a different query or check your indexer configuration.'}
                  </p>
                )}
              </div>
            )}

            {results.length > 0 && (
              <div className="results-container">
                {notification && (
                  <div className={`download-notification ${notification.type}`}>
                    {notification.message}
                  </div>
                )}
                <div className="results-header">
                  <h4>{t('searchResults')} ({results.length} {t('resultsFound')})</h4>
                </div>

                <div className="results-table">
                  <div className="results-header-row">
                    <div className="col-protocol sortable" onClick={() => handleSort('protocol')}>{t('protocol')} {getSortIcon('protocol')}</div>
                    <div className="col-title sortable" onClick={() => handleSort('title')}>{t('title')} {getSortIcon('title')}</div>
                    <div className="col-indexer sortable" onClick={() => handleSort('indexer')}>{t('indexer')} {getSortIcon('indexer')}</div>
                    <div className="col-platform">{t('platform')}</div>
                    <div className="col-size sortable" onClick={() => handleSort('size')}>{t('size')} {getSortIcon('size')}</div>
                    <div className="col-peers sortable" onClick={() => handleSort('seeders')}>{t('peers')} {getSortIcon('seeders')}</div>
                    <div className="col-actions">{t('download')}</div>
                  </div>

                  {sortedResults.map((result, index) => {
                    const analysis = analyzeTorrent(result.title);

                    // Try to resolve explicit category name
                    let explicitPlatform = '';
                    let explicitPlatformType: PlatformType | null = null;

                    if (result.category) {
                      const catIds = result.category.split(',').map(s => parseInt(s.trim())).filter(n => !isNaN(n));
                      // Prioritize finding a detailed match (skipping general ones if detailed exists)
                      // But our GetPlatformInfo returns generic names for 1000/4000 too.
                      // Pick the most specific category ID (e.g. 1010 over 1000)

                      for (const cid of catIds) {
                        const info = GetPlatformInfo(cid);
                        if (info.name !== "Console" && info.name !== "Unknown") {
                          explicitPlatform = info.name;
                          explicitPlatformType = info.type;
                          break; // Found a specific one
                        }

                        // Only set generic if we haven't found anything yet AND it's not Unknown
                        if (!explicitPlatform && info.name !== "Unknown") {
                          explicitPlatform = info.name;
                          explicitPlatformType = info.type;
                        }
                      }
                    }

                    // Final display platform: Explicit Category > Detected Title Analysis
                    // If explicit is empty (because all were Unknown), it falls back to detected.
                    const displayPlatform = explicitPlatform || analysis.detectedPlatform;

                    // Map platform type to CSS class for proper contrast badges
                    let platformClass = 'platform-unknown';
                    const pType = explicitPlatformType || (analysis.detectedPlatform === 'PC' ? 'PC' : null);
                    if (pType) {
                      switch (pType) {
                        case 'Nintendo': platformClass = 'platform-nintendo'; break;
                        case 'PlayStation': platformClass = 'platform-playstation'; break;
                        case 'Xbox': platformClass = 'platform-xbox'; break;
                        case 'PC': platformClass = displayPlatform === 'Mac' ? 'platform-mac' : 'platform-pc'; break;
                        default: platformClass = 'platform-unknown'; break;
                      }
                    } else if (displayPlatform === 'Console') {
                      platformClass = 'platform-console';
                    } else if (displayPlatform !== 'Game' && displayPlatform !== 'Unknown') {
                      platformClass = 'platform-console';
                    }

                    return (
                      <div key={index} className={`results-row ${analysis.confidence}`}>
                        <div className="col-protocol">
                          <span className={`protocol-badge ${(result.protocol || 'torrent').toLowerCase()}`}>
                            {(result.protocol || 'TORRENT').toUpperCase()}
                          </span>
                        </div>



                        <div className="col-title">
                          <div className="title-content">
                            {result.infoUrl ? (
                              <a href={result.infoUrl} target="_blank" rel="noopener noreferrer" className="title-link">
                                {result.title}
                              </a>
                            ) : (
                              <span className="title-text">{result.title}</span>
                            )}
                            <div className="title-meta">
                              {result.releaseGroup && (
                                <span className="release-group">{result.releaseGroup}</span>
                              )}
                              {analysis.tags.map((tag, i) => (
                                <span key={i} className={`title-tag ${tag.toLowerCase()}`}>[{tag}]</span>
                              ))}
                            </div>
                          </div>
                        </div>

                        <div className="col-indexer">
                          <span className="indexer-name">{result.indexer || result.indexerName}</span>
                        </div>

                        <div className="col-platform">
                          <span className={`platform-tag ${platformClass}`} title={`Category IDs: ${result.category || 'None'}`}>
                            {displayPlatform}
                          </span>
                        </div>

                        <div className="col-size">
                          <span className="size">{result.formattedSize || `${(result.size / (1024 * 1024 * 1024)).toFixed(2)} GB`}</span>
                        </div>

                        <div className="col-peers">
                          {result.protocol?.toLowerCase() === 'usenet' || result.protocol?.toLowerCase() === 'nzb' ? (
                            <span className="peers-info" title="Grabs (downloads)">
                              {result.grabs != null ? <><FontAwesomeIcon icon={faDownload} /> {result.grabs}</> : '-'}
                            </span>
                          ) : (
                            <div className="peers-info">
                              <span className={`seeders ${getSeedersClass(result.seeders)}`}>
                                <FontAwesomeIcon icon={faArrowUp} /> {result.seeders ?? 0}
                              </span>
                              <span className="separator">/</span>
                              <span className="leechers">
                                <FontAwesomeIcon icon={faArrowDown} /> {result.leechers ?? 0}
                              </span>
                            </div>
                          )}
                        </div>



                        <div className="col-actions">
                          <div className="download-buttons">
                            {result.magnetUrl && (
                              <button
                                className={`download-btn magnet ${downloadingUrl === result.magnetUrl ? 'loading' : ''}`}
                                title={`Download to ${result.detectedPlatform || 'PC'}`}
                                onClick={() => handleDownloadWithPlatform(result.magnetUrl, result.protocol, result.detectedPlatform, result.platformFolder, result.title)}
                                disabled={!!downloadingUrl}
                              >
                                {downloadingUrl === result.magnetUrl ? <FontAwesomeIcon icon={faSpinner} spin /> : <FontAwesomeIcon icon={faMagnet} />}
                              </button>
                            )}
                            {result.downloadUrl && (
                              <button
                                className={`download-btn direct ${downloadingUrl === result.downloadUrl ? 'loading' : ''}`}
                                title={`Download to ${result.detectedPlatform || 'PC'}`}
                                onClick={() => handleDownloadWithPlatform(result.downloadUrl, result.protocol, result.detectedPlatform, result.platformFolder, result.title)}
                                disabled={!!downloadingUrl}
                              >
                                {downloadingUrl === result.downloadUrl ? <FontAwesomeIcon icon={faSpinner} spin /> : <FontAwesomeIcon icon={faDownload} />}
                              </button>
                            )}
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        )
      }

      {
        activeTab === 'files' && game && (
          <div className="game-files-section" style={{ marginTop: '20px' }}>
            {/* Game Folder Path */}
            <div style={{
              background: 'var(--ctp-surface0)',
              borderRadius: '10px',
              padding: '16px 20px',
              marginBottom: '16px',
              border: '1px solid var(--ctp-surface1)'
            }}>
              <h4 style={{ margin: '0 0 10px 0', color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                <FontAwesomeIcon icon={faFolderOpen} style={{ color: 'var(--ctp-peach)' }} />
                {t('gameFiles')}
              </h4>
              {resolvedPath ? (
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                  <code style={{ background: 'var(--ctp-surface1)', padding: '4px 10px', borderRadius: '6px', fontSize: '12px', color: 'var(--ctp-text)', flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {resolvedPath}
                  </code>
                  {folderExists ? (
                    <span style={{ fontSize: '11px', color: 'var(--ctp-green)', display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
                      <FontAwesomeIcon icon={faCheck} /> Folder exists
                    </span>
                  ) : (
                    <button
                      onClick={handleCreateFolder}
                      disabled={creatingFolder}
                      style={{
                        padding: '5px 14px',
                        background: 'var(--ctp-blue)',
                        color: 'var(--ctp-base)',
                        border: 'none',
                        borderRadius: '6px',
                        cursor: creatingFolder ? 'not-allowed' : 'pointer',
                        fontSize: '12px',
                        fontWeight: 600,
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px',
                        flexShrink: 0
                      }}
                    >
                      {creatingFolder ? (
                        <><FontAwesomeIcon icon={faSpinner} spin /> Creating...</>
                      ) : (
                        <><FontAwesomeIcon icon={faFolder} /> Create Folder</>
                      )}
                    </button>
                  )}
                </div>
              ) : (
                <p style={{ margin: 0, fontSize: '12px', color: 'var(--ctp-overlay0)' }}>
                  Configure Library Folder in Media Management settings to resolve game paths.
                </p>
              )}
            </div>

            {/* GOG Downloads Section */}
            {game.gogId && gogGameDownloads.length > 0 && (
              <div style={{
                background: 'var(--ctp-surface0)',
                borderRadius: '10px',
                padding: '16px 20px',
                marginBottom: '16px',
                border: '1px solid var(--ctp-surface1)'
              }}>
                <h4 style={{ margin: '0 0 12px 0', color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <FontAwesomeIcon icon={faCloudDownloadAlt} style={{ color: 'var(--ctp-green)' }} />
                  GOG Downloads
                  <span style={{ fontSize: '12px', color: 'var(--ctp-subtext0)', fontWeight: 400 }}>({gogGameDownloads.length} files)</span>
                </h4>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                  {gogGameDownloads.map((dl, idx) => (
                    <div key={idx} style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '10px 14px',
                      background: 'var(--ctp-base)',
                      borderRadius: '8px',
                      border: '1px solid var(--ctp-surface1)'
                    }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flex: 1, minWidth: 0 }}>
                        <FontAwesomeIcon icon={faCloudDownloadAlt} style={{ color: 'var(--ctp-green)', flexShrink: 0 }} />
                        <div style={{ minWidth: 0 }}>
                          <div style={{ color: 'var(--ctp-text)', fontSize: '13px', fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                            {dl.name}
                          </div>
                          <div style={{ color: 'var(--ctp-subtext0)', fontSize: '11px', display: 'flex', gap: '8px' }}>
                            <span style={{ textTransform: 'capitalize' }}>{dl.platform}</span>
                            {dl.size && <span>{dl.size}</span>}
                          </div>
                        </div>
                      </div>
                      {dl.manualUrl && (
                        <button
                          onClick={() => handleGogDownloadToFolder(dl.manualUrl!, dl.name, dl.platform)}
                          disabled={!!gogDownloading}
                          style={{
                            padding: '6px 14px',
                            background: gogDownloading === dl.manualUrl ? 'var(--ctp-surface1)' : 'var(--ctp-green)',
                            color: 'var(--ctp-base)',
                            border: 'none',
                            borderRadius: '6px',
                            cursor: gogDownloading ? 'not-allowed' : 'pointer',
                            fontSize: '12px',
                            fontWeight: 600,
                            display: 'flex',
                            alignItems: 'center',
                            gap: '6px',
                            flexShrink: 0,
                            opacity: gogDownloading && gogDownloading !== dl.manualUrl ? 0.5 : 1
                          }}
                        >
                          {gogDownloading === dl.manualUrl ? (
                            <><FontAwesomeIcon icon={faSpinner} spin /> Downloading...</>
                          ) : (
                            <><FontAwesomeIcon icon={faDownload} /> Download</>
                          )}
                        </button>
                      )}
                    </div>
                  ))}
                </div>
                <p style={{ margin: '10px 0 0 0', fontSize: '11px', color: 'var(--ctp-subtext0)' }}>
                  Downloads will be saved to: <code style={{ background: 'var(--ctp-surface1)', padding: '2px 6px', borderRadius: '4px' }}>{resolvedPath || game.path || 'N/A'}</code>
                </p>
              </div>
            )}

            {/* Local Files Section */}
            <div style={{
              background: 'var(--ctp-surface0)',
              borderRadius: '10px',
              padding: '16px 20px',
              border: '1px solid var(--ctp-surface1)'
            }}>
              <h4 style={{ margin: '0 0 12px 0', color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                <FontAwesomeIcon icon={faFolder} style={{ color: 'var(--ctp-yellow)' }} />
                {t('localFiles')}
                {gameFilesTotalSize && <span style={{ fontSize: '12px', color: 'var(--ctp-subtext0)', fontWeight: 400 }}>({gameFiles.length} files, {gameFilesTotalSize})</span>}
                {(fileCounts.patches > 0 || fileCounts.dlc > 0) && (
                  <span style={{ display: 'flex', gap: '6px', marginLeft: '4px' }}>
                    {fileCounts.patches > 0 && (
                      <span style={{ fontSize: '10px', padding: '2px 8px', borderRadius: '10px', background: 'var(--ctp-blue)', color: 'var(--ctp-base)', fontWeight: 700 }}>
                        {fileCounts.patches} Update{fileCounts.patches !== 1 ? 's' : ''}
                      </span>
                    )}
                    {fileCounts.dlc > 0 && (
                      <span style={{ fontSize: '10px', padding: '2px 8px', borderRadius: '10px', background: 'var(--ctp-mauve)', color: 'var(--ctp-base)', fontWeight: 700 }}>
                        {fileCounts.dlc} DLC{fileCounts.dlc !== 1 ? 's' : ''}
                      </span>
                    )}
                  </span>
                )}
              </h4>

              {gameFilesLoading && (
                <div style={{ textAlign: 'center', padding: '30px 0', color: 'var(--ctp-overlay0)' }}>
                  <FontAwesomeIcon icon={faSpinner} spin style={{ fontSize: '24px', marginBottom: '10px' }} />
                  <p style={{ margin: 0 }}>Loading files...</p>
                </div>
              )}

              {!gameFilesLoading && gameFiles.length === 0 && supplementaryFiles.length === 0 && (
                <div style={{ textAlign: 'center', padding: '30px 0', color: 'var(--ctp-overlay0)' }}>
                  <FontAwesomeIcon icon={faFolder} style={{ fontSize: '24px', marginBottom: '10px', opacity: 0.5 }} />
                  <p style={{ margin: 0 }}>{t('noFilesFound')}</p>
                  {!folderExists && resolvedPath && (
                    <p style={{ margin: '8px 0 0 0', fontSize: '12px' }}>
                      Create the game folder first, then download or import files.
                    </p>
                  )}
                </div>
              )}

              {!gameFilesLoading && gameFiles.length > 0 && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                  {gameFiles.map((file, idx) => {
                    const ft = file.fileType || 'Main';
                    const badgeColor = ft === 'Patch' ? 'var(--ctp-blue)' : ft === 'DLC' ? 'var(--ctp-mauve)' : 'var(--ctp-green)';
                    return (
                    <div key={idx} style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '8px 12px',
                      background: idx % 2 === 0 ? 'var(--ctp-base)' : 'transparent',
                      borderRadius: '6px'
                    }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flex: 1, minWidth: 0 }}>
                        <FontAwesomeIcon icon={getFileIcon(file.extension)} style={{ color: 'var(--ctp-blue)', flexShrink: 0, width: '14px' }} />
                        <div style={{ minWidth: 0, flex: 1 }}>
                          <div style={{ color: 'var(--ctp-text)', fontSize: '13px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', display: 'flex', alignItems: 'center', gap: '6px' }} title={file.relativePath}>
                            {file.relativePath}
                            {ft !== 'Main' && (
                              <span style={{ fontSize: '9px', padding: '1px 6px', borderRadius: '8px', background: badgeColor, color: 'var(--ctp-base)', fontWeight: 700, flexShrink: 0 }}>
                                {ft === 'Patch' ? 'UPDATE' : 'DLC'}
                              </span>
                            )}
                          </div>
                        </div>
                      </div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexShrink: 0 }}>
                        <span style={{ color: 'var(--ctp-subtext0)', fontSize: '12px', minWidth: '70px', textAlign: 'right' }}>{file.formattedSize}</span>
                        <button
                          onClick={() => handleDownloadFile(file.relativePath)}
                          title={`Download ${file.name}`}
                          style={{
                            padding: '4px 10px',
                            background: 'var(--ctp-blue)',
                            color: 'var(--ctp-base)',
                            border: 'none',
                            borderRadius: '5px',
                            cursor: 'pointer',
                            fontSize: '11px',
                            fontWeight: 600,
                            display: 'flex',
                            alignItems: 'center',
                            gap: '4px'
                          }}
                        >
                          <FontAwesomeIcon icon={faDownload} />
                        </button>
                      </div>
                    </div>
                    );
                  })}
                </div>
              )}

              {/* Supplementary files from shared folders (Updates+DLCs, Patches+DLCs) */}
              {!gameFilesLoading && supplementaryFiles.length > 0 && (
                <div style={{ marginTop: '16px' }}>
                  <h5 style={{ margin: '0 0 8px 0', color: 'var(--ctp-subtext0)', fontSize: '12px', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.5px' }}>
                    Linked Updates & DLCs ({supplementaryFiles.length})
                  </h5>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                    {supplementaryFiles.map((file, idx) => {
                      const badgeColor = file.fileType === 'Patch' ? 'var(--ctp-blue)' : 'var(--ctp-mauve)';
                      return (
                        <div key={`supp-${idx}`} style={{
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          padding: '8px 12px',
                          background: idx % 2 === 0 ? 'var(--ctp-base)' : 'transparent',
                          borderRadius: '6px',
                          borderLeft: `3px solid ${badgeColor}`
                        }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flex: 1, minWidth: 0 }}>
                            <FontAwesomeIcon icon={getFileIcon(file.extension)} style={{ color: badgeColor, flexShrink: 0, width: '14px' }} />
                            <div style={{ minWidth: 0, flex: 1 }}>
                              <div style={{ color: 'var(--ctp-text)', fontSize: '13px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', display: 'flex', alignItems: 'center', gap: '6px' }} title={file.relativePath}>
                                {file.name}
                                <span style={{ fontSize: '9px', padding: '1px 6px', borderRadius: '8px', background: badgeColor, color: 'var(--ctp-base)', fontWeight: 700, flexShrink: 0 }}>
                                  {file.fileType === 'Patch' ? 'UPDATE' : 'DLC'}
                                </span>
                                {file.version && (
                                  <span style={{ fontSize: '10px', color: 'var(--ctp-subtext0)', fontWeight: 500 }}>{file.version}</span>
                                )}
                              </div>
                              {file.contentName && file.contentName !== file.name && (
                                <div style={{ color: 'var(--ctp-subtext0)', fontSize: '11px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                  {file.contentName}
                                </div>
                              )}
                            </div>
                          </div>
                          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
                            {file.serial && <span style={{ fontSize: '10px', color: 'var(--ctp-overlay0)', fontFamily: 'monospace' }}>{file.serial}</span>}
                            <span style={{ color: 'var(--ctp-subtext0)', fontSize: '12px', minWidth: '70px', textAlign: 'right' }}>{file.formattedSize}</span>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </div>

            {/* Rename Updates & DLCs Section */}
            {supplementaryFiles.length > 0 && (
              <div style={{
                background: 'var(--ctp-surface0)',
                borderRadius: '10px',
                padding: '16px 20px',
                marginTop: '16px',
                border: '1px solid var(--ctp-surface1)'
              }}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '12px' }}>
                  <h4 style={{ margin: 0, color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <FontAwesomeIcon icon={faPen} style={{ color: 'var(--ctp-blue)' }} />
                    Rename Updates & DLCs
                  </h4>
                  <button
                    onClick={async () => {
                      if (!id) return;
                      setSupRenameLoading(true);
                      setSupRenamePreview(null);
                      setSupRenameResult(null);
                      try {
                        const res = await apiClient.get(`/resort/game/${id}/supplementary/rename/preview`);
                        if (res.data.operations && res.data.count > 0) {
                          setSupRenamePreview(res.data.operations);
                        } else {
                          setSupRenamePreview([]);
                        }
                      } catch (err) {
                        console.error('Supplementary rename preview error:', err);
                        setNotification({ message: 'Failed to load rename preview', type: 'error' });
                      } finally {
                        setSupRenameLoading(false);
                      }
                    }}
                    disabled={supRenameLoading || supRenameApplying}
                    style={{
                      padding: '6px 14px',
                      background: 'var(--ctp-surface2)',
                      color: 'var(--ctp-text)',
                      border: '1px solid var(--ctp-overlay0)',
                      borderRadius: '6px',
                      cursor: 'pointer',
                      fontSize: '12px',
                      display: 'flex',
                      alignItems: 'center',
                      gap: '6px'
                    }}
                  >
                    {supRenameLoading ? <FontAwesomeIcon icon={faSpinner} spin /> : <FontAwesomeIcon icon={faSearch} />}
                    Preview
                  </button>
                </div>

                {supRenamePreview !== null && supRenamePreview.length === 0 && !supRenameResult && (
                  <p style={{ color: 'var(--ctp-green)', fontSize: '13px', margin: 0 }}>
                    <FontAwesomeIcon icon={faCheck} style={{ marginRight: '6px' }} />
                    All update & DLC files are already correctly named.
                  </p>
                )}

                {supRenamePreview !== null && supRenamePreview.length > 0 && (
                  <div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', marginBottom: '12px', maxHeight: '200px', overflowY: 'auto' }}>
                      {supRenamePreview.map((op, idx) => {
                        const badgeColor = op.fileType === 'Patch' ? 'var(--ctp-blue)' : 'var(--ctp-mauve)';
                        return (
                          <div key={op.gameFileId || idx} style={{
                            padding: '6px 10px',
                            background: idx % 2 === 0 ? 'var(--ctp-base)' : 'transparent',
                            borderRadius: '4px',
                            fontSize: '12px',
                            borderLeft: `3px solid ${badgeColor}`
                          }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', marginBottom: '2px' }}>
                              <span style={{ fontSize: '9px', padding: '1px 5px', borderRadius: '6px', background: badgeColor, color: 'var(--ctp-base)', fontWeight: 700 }}>
                                {op.fileType === 'Patch' ? 'UPDATE' : 'DLC'}
                              </span>
                              {op.version && <span style={{ color: 'var(--ctp-subtext0)', fontSize: '10px' }}>{op.version}</span>}
                              {op.conflict && <span style={{ color: 'var(--ctp-red)', fontSize: '10px', fontWeight: 600 }}>CONFLICT</span>}
                              {op.status !== 'Pending' && (
                                <span style={{ color: op.status === 'Applied' ? 'var(--ctp-green)' : op.status === 'Failed' ? 'var(--ctp-red)' : 'var(--ctp-yellow)', fontSize: '10px', fontWeight: 600 }}>
                                  {op.status.toUpperCase()}
                                </span>
                              )}
                            </div>
                            <div style={{ color: 'var(--ctp-overlay0)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={op.currentFileName}>
                              {op.currentFileName}
                            </div>
                            <div style={{ color: 'var(--ctp-green)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={op.newFileName}>
                              → {op.newFileName}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                    {!supRenameResult && (
                      <button
                        onClick={async () => {
                          if (!id) return;
                          setSupRenameApplying(true);
                          try {
                            const res = await apiClient.post(`/resort/game/${id}/supplementary/rename/apply`);
                            const data = res.data;
                            setSupRenameResult({
                              applied: data.applied || 0,
                              failed: data.failed || 0,
                              skipped: data.skipped || 0
                            });
                            if (data.operations) setSupRenamePreview(data.operations);
                            const filesRes = await apiClient.get(`/game/${id}/files`);
                            setGameFiles(filesRes.data.files || []);
                            setSupplementaryFiles(filesRes.data.supplementaryFiles || []);
                            setFileCounts(filesRes.data.counts || { main: 0, patches: 0, dlc: 0 });
                          } catch (err) {
                            console.error('Supplementary rename apply error:', err);
                            setNotification({ message: 'Failed to apply rename', type: 'error' });
                          } finally {
                            setSupRenameApplying(false);
                          }
                        }}
                        disabled={supRenameApplying}
                        style={{
                          padding: '8px 18px',
                          background: 'var(--ctp-green)',
                          color: 'var(--ctp-base)',
                          border: 'none',
                          borderRadius: '6px',
                          cursor: 'pointer',
                          fontWeight: 600,
                          fontSize: '13px',
                          display: 'flex',
                          alignItems: 'center',
                          gap: '6px'
                        }}
                      >
                        {supRenameApplying ? <FontAwesomeIcon icon={faSpinner} spin /> : <FontAwesomeIcon icon={faCheck} />}
                        Apply Rename ({supRenamePreview.length} file{supRenamePreview.length !== 1 ? 's' : ''})
                      </button>
                    )}

                    {supRenameResult && (
                      <div style={{ display: 'flex', gap: '12px', fontSize: '12px', marginTop: '8px' }}>
                        {supRenameResult.applied > 0 && <span style={{ color: 'var(--ctp-green)' }}>✓ {supRenameResult.applied} renamed</span>}
                        {supRenameResult.skipped > 0 && <span style={{ color: 'var(--ctp-yellow)' }}>⊘ {supRenameResult.skipped} skipped</span>}
                        {supRenameResult.failed > 0 && <span style={{ color: 'var(--ctp-red)' }}>✗ {supRenameResult.failed} failed</span>}
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}

            {/* Rename Files Section */}
            <div style={{
              background: 'var(--ctp-surface0)',
              borderRadius: '10px',
              padding: '16px 20px',
              marginTop: '16px',
              border: '1px solid var(--ctp-surface1)'
            }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '12px' }}>
                <h4 style={{ margin: 0, color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <FontAwesomeIcon icon={faPen} style={{ color: 'var(--ctp-mauve)' }} />
                  Rename Files
                </h4>
                <button
                  onClick={async () => {
                    if (!id) return;
                    setRenameLoading(true);
                    setRenamePreview(null);
                    setRenameResult(null);
                    try {
                      const res = await apiClient.get(`/resort/game/${id}/rename/preview`);
                      if (res.data.operations) {
                        setRenamePreview(res.data.operations);
                      } else {
                        setRenamePreview([]);
                        setRenameResult({ applied: 0, failed: 0, skipped: 0 });
                      }
                    } catch (err) {
                      console.error('Rename preview error:', err);
                      setNotification({ message: 'Failed to load rename preview', type: 'error' });
                    } finally {
                      setRenameLoading(false);
                    }
                  }}
                  disabled={renameLoading || renameApplying}
                  style={{
                    padding: '6px 14px',
                    background: 'var(--ctp-surface2)',
                    color: 'var(--ctp-text)',
                    border: '1px solid var(--ctp-overlay0)',
                    borderRadius: '6px',
                    cursor: 'pointer',
                    fontSize: '12px',
                    fontWeight: 600,
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px'
                  }}
                >
                  {renameLoading ? <FontAwesomeIcon icon={faSpinner} spin /> : <FontAwesomeIcon icon={faSearch} />}
                  Preview
                </button>
              </div>

              {renamePreview !== null && renamePreview.length === 0 && !renameResult && (
                <p style={{ color: 'var(--ctp-green)', fontSize: '13px', margin: 0 }}>
                  <FontAwesomeIcon icon={faCheck} style={{ marginRight: '6px' }} />
                  All files are already correctly named.
                </p>
              )}

              {renamePreview !== null && renamePreview.length > 0 && (
                <div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', marginBottom: '12px', maxHeight: '200px', overflowY: 'auto' }}>
                    {renamePreview.map((op, idx) => (
                      <div key={op.id || idx} style={{
                        padding: '6px 10px',
                        background: idx % 2 === 0 ? 'var(--ctp-base)' : 'transparent',
                        borderRadius: '4px',
                        fontSize: '12px'
                      }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                          <span style={{ color: 'var(--ctp-subtext0)', fontWeight: 600, minWidth: '80px' }}>{op.type}</span>
                          {op.status === 'Applied' ? (
                            <FontAwesomeIcon icon={faCheck} style={{ color: 'var(--ctp-green)', fontSize: '10px' }} />
                          ) : op.conflict ? (
                            <span style={{ color: 'var(--ctp-yellow)', fontSize: '10px' }}>⚠ {op.conflict}</span>
                          ) : null}
                        </div>
                        <div style={{ color: 'var(--ctp-red)', marginTop: '2px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={op.sourcePath}>
                          {op.sourcePath.split('/').pop() || op.sourcePath}
                        </div>
                        <div style={{ color: 'var(--ctp-green)', marginTop: '1px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={op.targetPath}>
                          → {op.targetPath.split('/').pop() || op.targetPath}
                        </div>
                      </div>
                    ))}
                  </div>

                  {!renameResult && (
                    <button
                      onClick={async () => {
                        if (!id) return;
                        setRenameApplying(true);
                        try {
                          const res = await apiClient.post(`/resort/game/${id}/rename/apply`);
                          const plan = res.data;
                          setRenameResult({
                            applied: plan.appliedCount || 0,
                            failed: plan.failedCount || 0,
                            skipped: plan.skippedCount || 0
                          });
                          if (plan.operations) setRenamePreview(plan.operations);
                          // Reload game files after rename
                          const filesRes = await apiClient.get(`/game/${id}/files`);
                          setGameFiles(filesRes.data.files || []);
                          setResolvedPath(filesRes.data.resolvedPath || null);
                          setFolderExists(filesRes.data.folderExists ?? false);
                          setGameFilesTotalSize(filesRes.data.totalSize || '');
                        } catch (err) {
                          console.error('Rename apply error:', err);
                          setNotification({ message: 'Failed to apply rename', type: 'error' });
                        } finally {
                          setRenameApplying(false);
                        }
                      }}
                      disabled={renameApplying}
                      style={{
                        padding: '8px 18px',
                        background: 'var(--ctp-green)',
                        color: 'var(--ctp-base)',
                        border: 'none',
                        borderRadius: '6px',
                        cursor: 'pointer',
                        fontSize: '13px',
                        fontWeight: 600,
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px'
                      }}
                    >
                      {renameApplying ? <FontAwesomeIcon icon={faSpinner} spin /> : <FontAwesomeIcon icon={faCheck} />}
                      Apply Rename ({renamePreview.length} operation{renamePreview.length !== 1 ? 's' : ''})
                    </button>
                  )}

                  {renameResult && (
                    <div style={{ fontSize: '13px', display: 'flex', gap: '16px', marginTop: '8px' }}>
                      <span style={{ color: 'var(--ctp-green)' }}>Applied: {renameResult.applied}</span>
                      {renameResult.failed > 0 && <span style={{ color: 'var(--ctp-red)' }}>Failed: {renameResult.failed}</span>}
                      {renameResult.skipped > 0 && <span style={{ color: 'var(--ctp-yellow)' }}>Skipped: {renameResult.skipped}</span>}
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        )
      }

      {
        activeTab === 'patches' && game && (
          <div className="game-files-section" style={{ marginTop: '20px' }}>
            {/* Patches Folder Path */}
            <div style={{
              background: 'var(--ctp-surface0)',
              borderRadius: '10px',
              padding: '16px 20px',
              marginBottom: '16px',
              border: '1px solid var(--ctp-surface1)'
            }}>
              <h4 style={{ margin: '0 0 10px 0', color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                <FontAwesomeIcon icon={faFolderOpen} style={{ color: 'var(--ctp-peach)' }} />
                {t('patchesFolder')}
              </h4>
              {patchesFolder ? (
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
                  <code style={{ background: 'var(--ctp-surface1)', padding: '4px 10px', borderRadius: '6px', fontSize: '12px', color: 'var(--ctp-text)', flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {patchesFolder}
                  </code>
                  {patchesFolderExists ? (
                    <span style={{ fontSize: '11px', color: 'var(--ctp-green)', display: 'flex', alignItems: 'center', gap: '4px', flexShrink: 0 }}>
                      <FontAwesomeIcon icon={faCheck} /> Folder exists
                    </span>
                  ) : (
                    <button
                      onClick={handleCreatePatchesFolder}
                      disabled={creatingPatchesFolder}
                      style={{
                        padding: '5px 14px',
                        background: 'var(--ctp-blue)',
                        color: 'var(--ctp-base)',
                        border: 'none',
                        borderRadius: '6px',
                        cursor: creatingPatchesFolder ? 'not-allowed' : 'pointer',
                        fontSize: '12px',
                        fontWeight: 600,
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px',
                        flexShrink: 0
                      }}
                    >
                      {creatingPatchesFolder ? (
                        <><FontAwesomeIcon icon={faSpinner} spin /> Creating...</>
                      ) : (
                        <><FontAwesomeIcon icon={faFolder} /> {t('createPatchesFolder')}</>
                      )}
                    </button>
                  )}
                </div>
              ) : (
                <p style={{ margin: 0, fontSize: '12px', color: 'var(--ctp-overlay0)' }}>
                  Configure Library Folder in Media Management settings to resolve game paths.
                </p>
              )}
            </div>

            {/* Main Game Info */}
            {resolvedPath && (
              <div style={{
                background: 'var(--ctp-surface0)',
                borderRadius: '10px',
                padding: '12px 20px',
                marginBottom: '16px',
                border: '1px solid var(--ctp-surface1)',
                display: 'flex',
                alignItems: 'center',
                gap: '10px'
              }}>
                <FontAwesomeIcon icon={faGamepad} style={{ color: 'var(--ctp-green)', flexShrink: 0 }} />
                <div>
                  <div style={{ fontSize: '12px', color: 'var(--ctp-subtext0)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>{t('mainGame')}</div>
                  <code style={{ fontSize: '12px', color: 'var(--ctp-text)' }}>{resolvedPath}</code>
                </div>
              </div>
            )}

            {/* Patch Files List */}
            <div style={{
              background: 'var(--ctp-surface0)',
              borderRadius: '10px',
              padding: '16px 20px',
              border: '1px solid var(--ctp-surface1)'
            }}>
              <h4 style={{ margin: '0 0 12px 0', color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                <FontAwesomeIcon icon={faDownload} style={{ color: 'var(--ctp-blue)' }} />
                {t('patchFiles')}
                {patchesTotalSize && <span style={{ fontSize: '12px', color: 'var(--ctp-subtext0)', fontWeight: 400 }}>({patchFiles.length} files, {patchesTotalSize})</span>}
              </h4>

              {patchesLoading && (
                <div style={{ textAlign: 'center', padding: '30px 0', color: 'var(--ctp-overlay0)' }}>
                  <FontAwesomeIcon icon={faSpinner} spin style={{ fontSize: '24px', marginBottom: '10px' }} />
                  <p style={{ margin: 0 }}>Loading patches...</p>
                </div>
              )}

              {!patchesLoading && patchFiles.length === 0 && (
                <div style={{ textAlign: 'center', padding: '30px 0', color: 'var(--ctp-overlay0)' }}>
                  <FontAwesomeIcon icon={faFolder} style={{ fontSize: '24px', marginBottom: '10px', opacity: 0.5 }} />
                  <p style={{ margin: 0 }}>{t('noPatchesFound')}</p>
                  {!patchesFolderExists && patchesFolder && (
                    <p style={{ margin: '8px 0 0 0', fontSize: '12px' }}>
                      Create the Patches folder first, then add patch files.
                    </p>
                  )}
                </div>
              )}

              {!patchesLoading && patchFiles.length > 0 && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                  {patchFiles.map((file, idx) => (
                    <div key={idx} style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '8px 12px',
                      background: idx % 2 === 0 ? 'var(--ctp-base)' : 'transparent',
                      borderRadius: '6px'
                    }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flex: 1, minWidth: 0 }}>
                        <FontAwesomeIcon icon={getFileIcon(file.extension)} style={{ color: 'var(--ctp-blue)', flexShrink: 0, width: '14px' }} />
                        <div style={{ minWidth: 0, flex: 1 }}>
                          <div style={{ color: 'var(--ctp-text)', fontSize: '13px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={file.relativePath}>
                            {file.relativePath}
                          </div>
                        </div>
                      </div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexShrink: 0 }}>
                        <span style={{ color: 'var(--ctp-subtext0)', fontSize: '12px', minWidth: '70px', textAlign: 'right' }}>{file.formattedSize}</span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )
      }

      {
        activeTab === 'media' && game && (
          <div className="game-files-section" style={{ marginTop: '20px' }}>
            {localMediaLoading ? (
              <div style={{ textAlign: 'center', padding: '40px', color: 'var(--ctp-subtext0)' }}>
                <FontAwesomeIcon icon={faSpinner} spin size="2x" />
                <div style={{ marginTop: '12px' }}>{t('loading')}</div>
              </div>
            ) : localImages.length === 0 && localVideos.length === 0 ? (
              <div style={{
                textAlign: 'center', padding: '40px', color: 'var(--ctp-subtext0)',
                background: 'var(--ctp-surface0)', borderRadius: '10px', border: '1px solid var(--ctp-surface1)'
              }}>
                <FontAwesomeIcon icon={faFolder} size="2x" style={{ marginBottom: '12px', opacity: 0.5 }} />
                <div>{t('noLocalMedia') || 'No local images or videos found.'}</div>
                <div style={{ fontSize: '12px', marginTop: '8px', opacity: 0.7 }}>
                  {t('localMediaHint') || 'Place files in images/ and videos/ folders next to your ROM files (RetroBat/Batocera convention).'}
                </div>
              </div>
            ) : (
              <>
                {/* Videos */}
                {localVideos.length > 0 && (
                  <div style={{ marginBottom: '20px' }}>
                    <h4 style={{ margin: '0 0 12px 0', color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <FontAwesomeIcon icon={faGamepad} style={{ color: 'var(--ctp-mauve)' }} />
                      {t('videos') || 'Videos'} ({localVideos.length})
                    </h4>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                      {localVideos.map((video, idx) => (
                        <div key={idx} style={{
                          background: 'var(--ctp-surface0)', borderRadius: '10px', overflow: 'hidden',
                          border: '1px solid var(--ctp-surface1)'
                        }}>
                          <div style={{ padding: '8px 12px', fontSize: '12px', color: 'var(--ctp-subtext0)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <span>{video.fileName}</span>
                            <span style={{
                              background: 'var(--ctp-mauve)', color: 'var(--ctp-base)',
                              padding: '2px 8px', borderRadius: '4px', fontSize: '11px', fontWeight: 600
                            }}>{video.type}</span>
                          </div>
                          <video
                            controls
                            preload="metadata"
                            style={{ width: '100%', maxHeight: '400px', background: '#000' }}
                          >
                            <source src={video.url} type="video/mp4" />
                          </video>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Images */}
                {localImages.length > 0 && (
                  <div>
                    <h4 style={{ margin: '0 0 12px 0', color: 'var(--ctp-text)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <FontAwesomeIcon icon={faFile} style={{ color: 'var(--ctp-blue)' }} />
                      {t('images') || 'Images'} ({localImages.length})
                    </h4>
                    <div style={{
                      display: 'grid',
                      gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
                      gap: '12px'
                    }}>
                      {localImages.map((img, idx) => (
                        <div key={idx} style={{
                          background: 'var(--ctp-surface0)', borderRadius: '10px', overflow: 'hidden',
                          border: '1px solid var(--ctp-surface1)', cursor: 'pointer',
                          transition: 'transform 0.15s ease, box-shadow 0.15s ease'
                        }}
                          onClick={() => setSelectedLocalImage(img.url)}
                          onMouseEnter={e => { e.currentTarget.style.transform = 'scale(1.02)'; e.currentTarget.style.boxShadow = '0 4px 16px rgba(0,0,0,0.3)'; }}
                          onMouseLeave={e => { e.currentTarget.style.transform = 'scale(1)'; e.currentTarget.style.boxShadow = 'none'; }}
                        >
                          <img
                            src={img.url}
                            alt={img.fileName}
                            style={{ width: '100%', height: '180px', objectFit: 'cover', display: 'block' }}
                            loading="lazy"
                          />
                          <div style={{ padding: '8px 10px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <span style={{ fontSize: '11px', color: 'var(--ctp-subtext0)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', flex: 1 }}>
                              {img.fileName}
                            </span>
                            <span style={{
                              background: 'var(--ctp-blue)', color: 'var(--ctp-base)',
                              padding: '2px 6px', borderRadius: '4px', fontSize: '10px', fontWeight: 600, flexShrink: 0, marginLeft: '6px'
                            }}>{img.type}</span>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </>
            )}

            {/* Lightbox */}
            {selectedLocalImage && (
              <div
                style={{
                  position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, zIndex: 9999,
                  background: 'rgba(0,0,0,0.85)', display: 'flex', alignItems: 'center', justifyContent: 'center',
                  cursor: 'pointer'
                }}
                onClick={() => setSelectedLocalImage(null)}
              >
                <img
                  src={selectedLocalImage}
                  alt=""
                  style={{ maxWidth: '90vw', maxHeight: '90vh', objectFit: 'contain', borderRadius: '8px' }}
                  onClick={e => e.stopPropagation()}
                />
                <button
                  onClick={() => setSelectedLocalImage(null)}
                  style={{
                    position: 'absolute', top: '20px', right: '20px',
                    background: 'rgba(0,0,0,0.5)', color: '#fff', border: 'none',
                    borderRadius: '50%', width: '40px', height: '40px', fontSize: '20px',
                    cursor: 'pointer', display: 'flex', alignItems: 'center', justifyContent: 'center'
                  }}
                >&times;</button>
              </div>
            )}
          </div>
        )
      }

      <div className="back-link">
        <Link to={backUrl}>{backLabel}</Link>
      </div>

      {
        showCorrectionModal && game && (
          <GameCorrectionModal
            game={game}
            language={language}
            onClose={() => setShowCorrectionModal(false)}
            onSave={handleCorrectionSave}
          />
        )
      }

      <Modal
        isOpen={showInstallWarning}
        onClose={() => setShowInstallWarning(false)}
        title={t('installWarningTitle')}
        footer={
          <>
            <button className="btn-secondary" onClick={() => setShowInstallWarning(false)}>{t('cancel')}</button>
            <button className="btn-danger" onClick={confirmInstall}>{t('confirmInstall')}</button>
          </>
        }
      >
        <p style={{ color: 'var(--ctp-text)', lineHeight: '1.6', margin: 0 }}>{t('installWarningBody')}</p>
      </Modal>

      {showEmulator && game && emulatorConfig && (
        <EmulatorPlayer
          romUrl={emulatorConfig.romUrl}
          gameTitle={game.title}
          platform={game.platform?.name || 'Unknown'}
          gameId={game.id}
          onClose={() => setShowEmulator(false)}
        />
      )}

      {/* Notes Editor Modal */}
      <Modal
        isOpen={showNotesEditor}
        onClose={() => setShowNotesEditor(false)}
        title={t('editNotes') || 'Edit Notes'}
        maxWidth="600px"
        footer={
          <>
            <button className="btn-secondary" onClick={() => setShowNotesEditor(false)}>{t('cancel')}</button>
            <button className="btn-primary" onClick={handleSaveNotes} style={{ backgroundColor: 'var(--ctp-blue)', color: 'var(--ctp-base)' }}>{t('save')}</button>
          </>
        }
      >
        <textarea
          value={editedNotes}
          onChange={(e) => setEditedNotes(e.target.value)}
          placeholder={t('notesPlaceholder') || 'Add your personal notes about this game...'}
          style={{ 
            width: '100%', 
            minHeight: '200px', 
            backgroundColor: 'var(--ctp-surface0)', 
            border: '1px solid var(--ctp-surface1)', 
            borderRadius: '8px', 
            padding: '12px',
            color: 'var(--ctp-text)',
            fontSize: '14px',
            resize: 'vertical'
          }}
        />
      </Modal>

      {/* Tag Picker Modal */}
      <Modal
        isOpen={showTagPicker}
        onClose={() => setShowTagPicker(false)}
        title={t('addTag') || 'Add Tag'}
        maxWidth="400px"
      >
        {/* Existing Tags */}
        {allTags.length > 0 && (
          <div style={{ marginBottom: '15px' }}>
            <p style={{ color: 'var(--ctp-subtext0)', marginBottom: '8px', fontSize: '14px' }}>{t('existingTags') || 'Existing Tags'}:</p>
            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
              {allTags.filter((at: GameTag) => !tags.some((tg: GameTag) => tg.id === at.id)).map((tag: GameTag) => (
                <button
                  key={tag.id}
                  onClick={() => handleAddTag(tag.id)}
                  style={{
                    backgroundColor: tag.color || 'var(--ctp-surface1)',
                    padding: '6px 12px',
                    borderRadius: '12px',
                    border: 'none',
                    cursor: 'pointer',
                    fontSize: '12px',
                    color: 'var(--ctp-base)'
                  }}
                >
                  {tag.name}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Create New Tag */}
        <div>
          <p style={{ color: 'var(--ctp-subtext0)', marginBottom: '8px', fontSize: '14px' }}>{t('createNewTag') || 'Create New Tag'}:</p>
          <div style={{ display: 'flex', gap: '8px' }}>
            <input
              type="text"
              value={newTagName}
              onChange={(e) => setNewTagName(e.target.value)}
              placeholder={t('tagName') || 'Tag name...'}
              style={{
                flex: 1,
                backgroundColor: 'var(--ctp-surface0)',
                border: '1px solid var(--ctp-surface1)',
                borderRadius: '4px',
                padding: '8px 12px',
                color: 'var(--ctp-text)'
              }}
              onKeyPress={(e) => e.key === 'Enter' && newTagName.trim() && handleAddTag(undefined, newTagName.trim())}
            />
            <button
              onClick={() => newTagName.trim() && handleAddTag(undefined, newTagName.trim())}
              disabled={!newTagName.trim()}
              style={{
                backgroundColor: newTagName.trim() ? 'var(--ctp-green)' : 'var(--ctp-surface1)',
                color: 'var(--ctp-base)',
                border: 'none',
                borderRadius: '4px',
                padding: '8px 16px',
                cursor: newTagName.trim() ? 'pointer' : 'not-allowed'
              }}
            >
              <FontAwesomeIcon icon={faPlus} />
            </button>
          </div>
        </div>
      </Modal>

      {/* Platform Selection Modal */}
      <Modal
        isOpen={showPlatformModal && !!pendingDownload}
        onClose={() => { setShowPlatformModal(false); setPendingDownload(null); }}
        title={t('selectPlatform') || 'Select Platform'}
        maxWidth="500px"
        footer={
          <>
            <button className="btn-secondary" onClick={() => { setShowPlatformModal(false); setPendingDownload(null); }}>
              {t('cancel')}
            </button>
            <button className="btn-primary" onClick={confirmDownload} style={{ backgroundColor: 'var(--ctp-green)', color: 'var(--ctp-base)' }}>
              <FontAwesomeIcon icon={faDownload} style={{ marginRight: '8px' }} />
              {t('startDownload') || 'Start Download'}
            </button>
          </>
        }
      >
        <p style={{ color: 'var(--ctp-subtext0)', marginBottom: '15px', fontSize: '0.9em' }}>
          {t('platformSelectionDesc') || 'Choose the target platform folder for this download. The file will be organized accordingly.'}
        </p>
        
        {pendingDownload?.detectedPlatform && (
          <div style={{ 
            backgroundColor: 'var(--ctp-surface0)', 
            padding: '10px 15px', 
            borderRadius: '8px', 
            marginBottom: '15px',
            display: 'flex',
            alignItems: 'center',
            gap: '10px'
          }}>
            <span style={{ color: 'var(--ctp-subtext0)' }}>{t('detectedPlatform') || 'Detected'}:</span>
            <span style={{ 
              backgroundColor: 'var(--ctp-blue)', 
              color: 'var(--ctp-base)', 
              padding: '4px 12px', 
              borderRadius: '4px',
              fontWeight: 600
            }}>
              {pendingDownload.detectedPlatform}
            </span>
          </div>
        )}

        <label style={{ display: 'block', marginBottom: '8px', color: 'var(--ctp-text)' }}>
          {t('targetPlatform') || 'Target Platform'}
        </label>
        <select
          value={selectedPlatform}
          onChange={(e) => setSelectedPlatform(e.target.value)}
          style={{
            width: '100%',
            padding: '10px 12px',
            backgroundColor: 'var(--ctp-surface0)',
            border: '1px solid var(--ctp-surface1)',
            borderRadius: '8px',
            color: 'var(--ctp-text)',
            fontSize: '14px',
            marginBottom: '20px'
          }}
        >
          {availablePlatforms.map(platform => (
            <option key={platform.id} value={platform.folder}>
              {platform.name} ({platform.folder})
            </option>
          ))}
        </select>
      </Modal>
    </div >
  );
};

export default GameDetails;
