import React, { useState, useEffect, useCallback } from 'react';
import { metadataReviewApi, MetadataReviewItem, MatchCandidate, getErrorMessage } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faSearch, faCheck, faForward, faTimes, faSpinner, faFilter, faExclamationTriangle, faGamepad } from '@fortawesome/free-solid-svg-icons';
import PlatformIcon from '../components/PlatformIcon';
import './MetadataReview.css';

const MetadataReview: React.FC = () => {
  const [queue, setQueue] = useState<MetadataReviewItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [platformFilter, setPlatformFilter] = useState<string>('');
  const [selectedGame, setSelectedGame] = useState<MetadataReviewItem | null>(null);
  const [candidates, setCandidates] = useState<MatchCandidate[]>([]);
  const [candidatesLoading, setCandidatesLoading] = useState(false);
  const [searchOverride, setSearchOverride] = useState('');
  const [confirming, setConfirming] = useState<number | null>(null);

  const loadQueue = useCallback(async () => {
    setLoading(true);
    try {
      const response = await metadataReviewApi.getQueue(platformFilter || undefined);
      setQueue(response.data);
      setError(null);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to load review queue'));
    } finally {
      setLoading(false);
    }
  }, [platformFilter]);

  useEffect(() => {
    loadQueue();
  }, [loadQueue]);

  const openReview = async (item: MetadataReviewItem) => {
    setSelectedGame(item);
    setCandidates([]);
    setCandidatesLoading(true);
    setSearchOverride('');
    try {
      const response = await metadataReviewApi.getCandidates(item.gameId);
      setCandidates(response.data);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to load candidates'));
    } finally {
      setCandidatesLoading(false);
    }
  };

  const searchAgain = async () => {
    if (!selectedGame || !searchOverride.trim()) return;
    setCandidatesLoading(true);
    try {
      const response = await metadataReviewApi.getCandidates(selectedGame.gameId, searchOverride.trim());
      setCandidates(response.data);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Search failed'));
    } finally {
      setCandidatesLoading(false);
    }
  };

  const confirmMatch = async (candidate: MatchCandidate) => {
    if (!selectedGame) return;
    setConfirming(candidate.igdbId || -1);
    try {
      if (candidate.source === 'ScreenScraper') {
        await metadataReviewApi.confirm(selectedGame.gameId, 0, candidate.score, 'ScreenScraper', {
          title: candidate.title,
          overview: candidate.overview,
          year: candidate.year,
          developer: candidate.developer,
          publisher: candidate.publisher,
          genres: candidate.genres,
          rating: candidate.rating,
          coverUrl: candidate.coverUrl,
          coverLargeUrl: candidate.coverLargeUrl,
          backgroundUrl: candidate.backgroundUrl,
          bannerUrl: candidate.bannerUrl,
        });
      } else {
        await metadataReviewApi.confirm(selectedGame.gameId, candidate.igdbId, candidate.score, 'IGDB');
      }
      setSelectedGame(null);
      setCandidates([]);
      await loadQueue();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to confirm match'));
    } finally {
      setConfirming(null);
    }
  };

  const skipGame = async (gameId: number) => {
    try {
      await metadataReviewApi.skip(gameId);
      setSelectedGame(null);
      await loadQueue();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to skip'));
    }
  };

  const dismissGame = async (gameId: number) => {
    try {
      await metadataReviewApi.dismiss(gameId);
      setSelectedGame(null);
      await loadQueue();
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to dismiss'));
    }
  };

  const platforms = [...new Set(queue.map(q => q.platformName).filter(Boolean))];

  const getConfidenceColor = (score?: number): string => {
    if (!score) return '#888';
    if (score >= 0.85) return '#4caf50';
    if (score >= 0.60) return '#ff9800';
    if (score >= 0.40) return '#f44336';
    return '#888';
  };

  const formatScore = (score?: number): string => {
    if (score == null) return '—';
    return `${(score * 100).toFixed(0)}%`;
  };

  return (
    <div className="metadata-review-page">
      <div className="review-header">
        <h1>
          <FontAwesomeIcon icon={faExclamationTriangle} />
          {' '}Metadata Review
          {queue.length > 0 && <span className="review-count">{queue.length}</span>}
        </h1>
        <div className="review-filters">
          <div className="filter-group">
            <FontAwesomeIcon icon={faFilter} />
            <select value={platformFilter} onChange={e => setPlatformFilter(e.target.value)}>
              <option value="">All Platforms</option>
              {platforms.map(p => (
                <option key={p} value={p}>{p}</option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {error && <div className="review-error">{error}</div>}

      {loading ? (
        <div className="review-loading">
          <FontAwesomeIcon icon={faSpinner} spin /> Loading review queue...
        </div>
      ) : queue.length === 0 ? (
        <div className="review-empty">
          <FontAwesomeIcon icon={faCheck} size="2x" />
          <p>No games need metadata review.</p>
        </div>
      ) : (
        <div className="review-table">
          <div className="review-table-header">
            <span className="col-title">Title</span>
            <span className="col-platform">Platform</span>
            <span className="col-confidence">Confidence</span>
            <span className="col-reason">Reason</span>
            <span className="col-actions">Actions</span>
          </div>
          {queue.map(item => (
            <div key={item.gameId} className="review-row">
              <div className="col-title">
                <div className="review-game-info">
                  {item.coverUrl ? (
                    <img src={item.coverUrl} alt="" className="review-cover-thumb" />
                  ) : (
                    <div className="review-cover-placeholder">
                      <FontAwesomeIcon icon={faGamepad} />
                    </div>
                  )}
                  <div>
                    <strong>{item.title}</strong>
                    {item.alternativeTitle && (
                      <div className="review-alt-title">{item.alternativeTitle}</div>
                    )}
                  </div>
                </div>
              </div>
              <div className="col-platform">
                <PlatformIcon platformSlug={item.platformSlug || ''} />
                <span>{item.platformName}</span>
              </div>
              <div className="col-confidence">
                <span className="confidence-badge" style={{ color: getConfidenceColor(item.matchConfidence) }}>
                  {formatScore(item.matchConfidence)}
                </span>
              </div>
              <div className="col-reason">
                <span className="reason-tag">{item.reviewReason}</span>
              </div>
              <div className="col-actions">
                <button className="btn-review" onClick={() => openReview(item)} title="Review match candidates">
                  <FontAwesomeIcon icon={faSearch} /> Review
                </button>
                <button className="btn-skip" onClick={() => skipGame(item.gameId)} title="Skip this game">
                  <FontAwesomeIcon icon={faForward} />
                </button>
                <button className="btn-dismiss" onClick={() => dismissGame(item.gameId)} title="Keep without metadata">
                  <FontAwesomeIcon icon={faTimes} />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Review Modal */}
      {selectedGame && (
        <div className="review-modal-overlay" onClick={() => setSelectedGame(null)}>
          <div className="review-modal" onClick={e => e.stopPropagation()}>
            <div className="review-modal-header">
              <h2>Review: &ldquo;{selectedGame.title}&rdquo;</h2>
              <div className="review-modal-meta">
                <PlatformIcon platformSlug={selectedGame.platformSlug || ''} />
                <span>{selectedGame.platformName}</span>
                {selectedGame.matchConfidence != null && (
                  <span className="confidence-badge" style={{ color: getConfidenceColor(selectedGame.matchConfidence) }}>
                    Current: {formatScore(selectedGame.matchConfidence)}
                  </span>
                )}
              </div>
              <button className="modal-close" onClick={() => setSelectedGame(null)}>&times;</button>
            </div>

            <div className="review-search-bar">
              <input
                type="text"
                placeholder="Override search title..."
                value={searchOverride}
                onChange={e => setSearchOverride(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && searchAgain()}
              />
              <button onClick={searchAgain} disabled={candidatesLoading || !searchOverride.trim()}>
                <FontAwesomeIcon icon={faSearch} /> Search
              </button>
            </div>

            <div className="review-candidates">
              {candidatesLoading ? (
                <div className="candidates-loading">
                  <FontAwesomeIcon icon={faSpinner} spin /> Searching...
                </div>
              ) : candidates.length === 0 ? (
                <div className="candidates-empty">No candidates found. Try a different search term.</div>
              ) : (
                candidates.map((candidate, idx) => (
                  <div key={`${candidate.source}-${candidate.igdbId || idx}`} className={`candidate-card ${idx === 0 ? 'best-match' : ''}`}>
                    <div className="candidate-cover">
                      {candidate.coverUrl ? (
                        <img src={candidate.coverUrl} alt="" />
                      ) : (
                        <div className="candidate-cover-placeholder">
                          <FontAwesomeIcon icon={faGamepad} />
                        </div>
                      )}
                    </div>
                    <div className="candidate-info">
                      <div className="candidate-title">
                        {candidate.title}
                        <span className="source-badge" style={{
                          marginLeft: 8,
                          padding: '2px 8px',
                          borderRadius: 4,
                          fontSize: '0.75em',
                          fontWeight: 600,
                          background: candidate.source === 'ScreenScraper' ? '#e65100' : '#1565c0',
                          color: '#fff'
                        }}>{candidate.source}</span>
                      </div>
                      {candidate.alternativeNames && candidate.alternativeNames.length > 0 && (
                        <div className="candidate-alt-names">
                          {candidate.alternativeNames.slice(0, 3).join(', ')}
                          {candidate.alternativeNames.length > 3 && ` +${candidate.alternativeNames.length - 3} more`}
                        </div>
                      )}
                      <div className="candidate-meta">
                        {candidate.platforms.length > 0 && (
                          <span className="candidate-platforms">{candidate.platforms.join(', ')}</span>
                        )}
                        {candidate.year && <span className="candidate-year">{candidate.year}</span>}
                        {candidate.source === 'IGDB' && <span className="candidate-id">IGDB: {candidate.igdbId}</span>}
                        {candidate.developer && <span className="candidate-developer">{candidate.developer}</span>}
                      </div>
                    </div>
                    <div className="candidate-score">
                      <span className="score-value" style={{ color: getConfidenceColor(candidate.score) }}>
                        {formatScore(candidate.score)}
                      </span>
                    </div>
                    <div className="candidate-actions">
                      <button
                        className="btn-confirm"
                        onClick={() => confirmMatch(candidate)}
                        disabled={confirming != null}
                      >
                        {confirming === (candidate.igdbId || -1) ? (
                          <FontAwesomeIcon icon={faSpinner} spin />
                        ) : (
                          <><FontAwesomeIcon icon={faCheck} /> Confirm</>
                        )}
                      </button>
                    </div>
                  </div>
                ))
              )}
            </div>

            <div className="review-modal-footer">
              <button className="btn-skip-modal" onClick={() => skipGame(selectedGame.gameId)}>
                <FontAwesomeIcon icon={faForward} /> Skip
              </button>
              <button className="btn-dismiss-modal" onClick={() => dismissGame(selectedGame.gameId)}>
                <FontAwesomeIcon icon={faTimes} /> No metadata needed
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default MetadataReview;
