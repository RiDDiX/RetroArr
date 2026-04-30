import React from 'react';
import { useCardTilt } from '../../hooks/useCardTilt';
import { PlatformBadge } from './PlatformBadge';
import { SegmentedBar } from './SegmentedBar';
import { Button } from './Button';
import './GameCard.css';

export type GameCardProps = {
  title: string;
  platformSlug: string;
  coverUrl?: string | null;
  year?: number | null;
  progress?: number;            // 0..1, presence toggles the download overlay
  unidentified?: boolean;
  onOpen?: () => void;
  onPlay?: () => void;
  className?: string;
};

export function GameCard({
  title,
  platformSlug,
  coverUrl,
  year,
  progress,
  unidentified,
  onOpen,
  onPlay,
  className = '',
}: GameCardProps) {
  const ref = useCardTilt<HTMLElement>(4);

  const handleKey = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      onOpen?.();
    }
  };

  return (
    <article
      ref={ref}
      className={`game-card plat-${platformSlug} ${unidentified ? 'is-unidentified' : ''} ${className}`.trim()}
      tabIndex={0}
      role="button"
      aria-label={`${title} - ${platformSlug}`}
      onClick={onOpen}
      onKeyDown={handleKey}
    >
      <div className="game-card__art">
        {coverUrl ? (
          <img src={coverUrl} alt="" loading="lazy" decoding="async" />
        ) : (
          <div className="game-card__art-fallback">
            <span className="pixel">NO ART</span>
          </div>
        )}

        {progress !== undefined && (
          <div className="game-card__progress-overlay">
            <SegmentedBar value={progress} blocks={14} label={`${title} download`} />
            <span className="pixel game-card__progress-pct">{Math.round(progress * 100)}%</span>
          </div>
        )}
      </div>

      <div className="game-card__label">
        <span className="game-card__title" title={title}>{title}</span>
        <span className="game-card__meta">
          <PlatformBadge slug={platformSlug} />
          {year ? <span className="pixel game-card__year">{year}</span> : null}
        </span>
      </div>

      {onPlay && (
        <div className="game-card__actions">
          <Button
            tier="primary"
            onClick={(e) => { e.stopPropagation(); onPlay(); }}
          >
            Play
          </Button>
        </div>
      )}
    </article>
  );
}
