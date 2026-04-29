import React, { useEffect, useState } from 'react';
import { reviewsApi } from '../api/client';
import { useTranslation } from '../i18n/translations';
import { useCardTilt } from '../hooks/useCardTilt';
import steamLogo from '../assets/steam_logo.png';
import PlatformIcon from './PlatformIcon';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faStar } from '@fortawesome/free-solid-svg-icons';
import RegionFlag from './RegionFlag';
import ProtonDbBadge from './ProtonDbBadge';
import './GameCard.css';

// Derive a platform slug for --platform-accent tinting. Accepts the explicit
// slug, then falls back to a sanitised name, then 'default'.
function platClass(p?: { slug?: string; name?: string }): string {
  const raw = (p?.slug || p?.name || '').toLowerCase();
  if (!raw) return 'plat-default';
  return 'plat-' + raw.replace(/[^a-z0-9]+/g, '');
}

interface Game {
  id: number;
  title: string;
  year: number;
  overview?: string;
  images: {
    coverUrl?: string;
    backgroundUrl?: string;
  };
  rating?: number;
  genres: string[];
  platformId?: number;
  platform?: {
    id?: number;
    name: string;
    slug?: string;
  };
  status: string;
  steamId?: number;
  path?: string;
  region?: string;
  languages?: string;
  revision?: string;
  protonDbTier?: string;
  missingSince?: string | null;
}

interface ReviewData {
  userRating?: number;
  metacriticScore?: number;
  openCriticScore?: number;
}

interface GameCardProps {
  game: Game;
  reviewData?: ReviewData | null;
  onClick?: () => void;
  onContextMenu?: (e: React.MouseEvent) => void;
  onDelete?: () => void;
}

// Simple cache to prevent duplicate API calls across re-renders
const reviewCache = new Map<number, ReviewData | null>();
const pendingRequests = new Map<number, Promise<ReviewData | null>>();

const GameCard: React.FC<GameCardProps> = ({ game, reviewData, onClick, onContextMenu, onDelete }) => {
  const { t } = useTranslation();
  const tiltRef = useCardTilt<HTMLDivElement>(4);
  const [review, setReview] = useState<ReviewData | null>(reviewData ?? reviewCache.get(game.id) ?? null);

  // Sync from parent-provided reviewData prop
  useEffect(() => {
    if (reviewData !== undefined) {
      setReview(reviewData);
      if (reviewData) reviewCache.set(game.id, reviewData);
    }
  }, [reviewData, game.id]);

  useEffect(() => {
    // Skip if parent already provided data
    if (reviewData !== undefined) return;

    // Skip if already cached
    if (reviewCache.has(game.id)) {
      setReview(reviewCache.get(game.id) || null);
      return;
    }

    // Skip if request already pending
    if (pendingRequests.has(game.id)) {
      pendingRequests.get(game.id)?.then(data => setReview(data));
      return;
    }

    // Debounce requests - only load after a small delay to prevent spam
    const timeoutId = setTimeout(async () => {
      const loadReview = async (): Promise<ReviewData | null> => {
        try {
          const res = await reviewsApi.getByGameId(game.id);
          reviewCache.set(game.id, res.data);
          return res.data;
        } catch {
          reviewCache.set(game.id, null);
          return null;
        }
      };

      const promise = loadReview();
      pendingRequests.set(game.id, promise);
      const data = await promise;
      pendingRequests.delete(game.id);
      setReview(data);
    }, Math.random() * 500); // Stagger requests over 500ms

    return () => clearTimeout(timeoutId);
  }, [game.id, reviewData]);

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Downloaded': return 'var(--ctp-green)';
      case 'Downloading': return 'var(--ctp-blue)';
      case 'Missing': return 'var(--ctp-red)';
      default: return 'var(--ctp-text)';
    }
  };

  const isSteamGame = !!game.steamId;

  const handleKey = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      onClick?.();
    }
  };

  return (
    <div
      ref={tiltRef}
      className={`game-card ${platClass(game.platform)}`}
      role="button"
      tabIndex={0}
      aria-label={`${game.title}${game.platform?.name ? ' — ' + game.platform.name : ''}`}
      onClick={onClick}
      onContextMenu={onContextMenu}
      onKeyDown={handleKey}
    >
      <div className={`game-card-poster${game.missingSince ? ' game-card-poster--missing' : ''}`}>
        {game.images.coverUrl ? (
          <img src={game.images.coverUrl} alt={game.title} />
        ) : (
          <div className="game-card-placeholder">
            <span>?</span>
          </div>
        )}

        {game.missingSince && (
          <div
            className="game-card-missing-badge"
            title={`${t('missingBadgeTitle') || 'Files missing on disk'} (${new Date(game.missingSince).toLocaleDateString()})`}
          >
            {t('missingBadge') || 'Missing'}
          </div>
        )}

        {/* Platform Badge Overlay */}
        <div className="game-card-platform-badge" title={game.platform?.name || 'Platform'}>
          {isSteamGame ? (
            <img src={steamLogo} alt="Steam" />
          ) : (
            <PlatformIcon
              platformId={game.platformId || game.platform?.id}
              platformSlug={game.platform?.slug}
              platformName={game.platform?.name}
              size={24}
            />
          )}
        </div>

        {/* MetaScore Badge */}
        {review?.metacriticScore && (
          <div 
            className="game-card-metascore" 
            style={{ 
              position: 'absolute', 
              top: '8px', 
              left: '8px', 
              backgroundColor: review.metacriticScore >= 75 ? 'var(--ctp-green)' : review.metacriticScore >= 50 ? 'var(--ctp-yellow)' : 'var(--ctp-red)',
              color: review.metacriticScore >= 75 ? '#000' : review.metacriticScore >= 50 ? '#000' : '#fff',
              padding: '4px 8px',
              borderRadius: '4px',
              fontWeight: 'bold',
              fontSize: '14px',
              zIndex: 10
            }}
            title="Metacritic Score"
          >
            {review.metacriticScore}
          </div>
        )}
        {/* region row sits outside the hover overlay so the flag is always visible */}
        {(game.region || game.languages || game.revision || game.protonDbTier) && (
          <div className="game-card-region" style={{ position: 'absolute', bottom: '8px', left: '8px', zIndex: 10, display: 'flex', alignItems: 'center', gap: '4px' }}>
            {(game.region || game.languages) && (
              <RegionFlag region={game.region} languages={game.languages} size="small" />
            )}
            {game.revision && (
              <span style={{
                background: 'rgba(0,0,0,0.7)',
                color: '#fab387',
                fontSize: '9px',
                fontWeight: 600,
                padding: '1px 5px',
                borderRadius: '3px',
                whiteSpace: 'nowrap'
              }}>{game.revision}</span>
            )}
            {(() => {
              const slug = game.platform?.slug?.toLowerCase() ?? '';
              const showAlways = slug === 'pc' || slug === 'steam' || slug === 'windows';
              return (game.protonDbTier || showAlways) ? (
                <ProtonDbBadge tier={game.protonDbTier} size="medium" showLabel showWhenMissing={showAlways} />
              ) : null;
            })()}
          </div>
        )}
        <div className="game-card-overlay">
          <div className="game-card-rating">
            {game.rating ? `${Math.round(game.rating)}%` : 'N/A'}
          </div>
          {onDelete && (
            <button
              className="game-card-delete-btn"
              onClick={(e) => {
                e.stopPropagation();
                onDelete();
              }}
              title={t('deleteFromLibrary')}
            >
              ×
            </button>
          )}
        </div>
      </div>
      <div className="game-card-info">
        <h3 className="game-card-title">{game.title}</h3>
        <div className="game-card-meta">
          <span className="game-card-year">{game.year}</span>
          {game.platform && (
            <span className="game-card-platform">{game.platform.name}</span>
          )}
        </div>
        <div
          className="game-card-status"
          style={{ backgroundColor: getStatusColor(game.status) }}
        >
          {game.status}
        </div>

        {/* User Star Rating */}
        <div className="game-card-stars" style={{ display: 'flex', gap: '2px', marginTop: '6px' }}>
          {[1, 2, 3, 4, 5].map(star => (
            <FontAwesomeIcon 
              key={star} 
              icon={faStar} 
              style={{ 
                fontSize: '12px', 
                color: (review?.userRating || 0) >= star * 20 ? 'var(--ctp-yellow)' : 'var(--ctp-surface1)' 
              }} 
            />
          ))}
        </div>
      </div>
    </div>
  );
};

export default GameCard;
