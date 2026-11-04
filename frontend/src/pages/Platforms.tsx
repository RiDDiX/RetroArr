import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { gamesApi, platformsApi, monitorApi, reviewsApi, type BulkReviewData, type GameListDto, type Platform, type PlatformMonitorCount } from '../api/client';
import GameCard from '../components/GameCard';
import { Skeleton } from '../components/retro';
import { useRetroNavigate } from '../hooks/useRetroNavigate';
import { t } from '../i18n/translations';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faBookmark } from '@fortawesome/free-solid-svg-icons';
import './Platforms.css';

type Shelf = {
  platform: Platform;
  games: GameListDto[];
  total: number;
  reviews: Record<number, BulkReviewData>;
};

const SHELF_PAGE_SIZE = 12;
const SHELF_CONCURRENCY = 6;

// Runs fn over items with at most `limit` calls in flight, keeping input order.
async function mapLimit<T, R>(items: T[], limit: number, fn: (item: T) => Promise<R>): Promise<R[]> {
  const results: R[] = new Array(items.length);
  let next = 0;
  const worker = async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await fn(items[i]);
    }
  };
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return results;
}

function toSlugClass(p: Platform): string {
  const raw = (p.slug || p.name || '').toLowerCase();
  return raw ? 'plat-' + raw.replace(/[^a-z0-9]+/g, '') : 'plat-default';
}

function dtoToGame(dto: GameListDto) {
  return {
    id: dto.id,
    title: dto.title,
    year: dto.year,
    images: { coverUrl: dto.coverUrl },
    rating: dto.rating,
    genres: dto.genres,
    platformId: dto.platformId,
    platform: dto.platformName
      ? { id: dto.platformId, name: dto.platformName }
      : undefined,
    status: dto.status,
    steamId: dto.steamId,
    path: dto.path,
    region: dto.region,
    languages: dto.languages,
    revision: dto.revision,
    protonDbTier: dto.protonDbTier,
    missingSince: dto.missingSince,
    monitored: dto.monitored,
  };
}

export default function Platforms() {
  const navigate = useNavigate();
  const go = useRetroNavigate();
  const [shelves, setShelves] = useState<Shelf[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [monitorCounts, setMonitorCounts] = useState<Record<string, PlatformMonitorCount>>({});
  const [monitorBusyId, setMonitorBusyId] = useState<number | null>(null);

  const loadShelves = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const platformsResp = await platformsApi.getEnabled();
      const enabled = platformsResp.data || [];

      // Per-platform sample requests, a few at a time so large platform
      // lists don't flood the backend.
      const results = await mapLimit(enabled, SHELF_CONCURRENCY, async (p) => {
        try {
          const r = await gamesApi.getPaged({
            platformId: p.id,
            pageSize: SHELF_PAGE_SIZE,
            sortOrder: 'asc',
          });
          const games = r.data.items || [];
          // One review request per shelf instead of one per card
          const reviews = games.length > 0
            ? await reviewsApi.getBulk(games.map((g) => g.id)).then((res) => res.data, () => ({}))
            : {};
          return {
            platform: p,
            games,
            total: r.data.totalItems || 0,
            reviews,
          } as Shelf;
        } catch {
          return { platform: p, games: [], total: 0, reviews: {} } as Shelf;
        }
      });

      if (signal?.aborted) return;
      const populated = results
        .filter((s) => s.games.length > 0)
        .sort((a, b) => b.total - a.total);
      setShelves(populated);
    } catch (e) {
      if (!signal?.aborted) setError(e instanceof Error ? e.message : 'Failed to load platforms');
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  const loadMonitorCounts = useCallback(async () => {
    try {
      const r = await monitorApi.getPlatformMonitoredCounts();
      setMonitorCounts(r.data);
    } catch {
      // Non-critical: toggle falls back to "not all monitored".
    }
  }, []);

  // Toggle monitoring for a whole platform shelf (bulk + per-platform default).
  const handlePlatformMonitorToggle = useCallback(async (platformId: number) => {
    if (monitorBusyId !== null) return;
    const c = monitorCounts[String(platformId)];
    const allMonitored = !!c && c.total > 0 && c.monitored >= c.total;
    setMonitorBusyId(platformId);
    try {
      await monitorApi.setPlatformMonitored(platformId, !allMonitored, true);
      await Promise.all([loadShelves(), loadMonitorCounts()]);
    } catch (e) {
      console.error('Platform monitor toggle failed:', e);
    } finally {
      setMonitorBusyId(null);
    }
  }, [monitorBusyId, monitorCounts, loadShelves, loadMonitorCounts]);

  // Reflect a single-game marker change in the shelf and refresh platform counts.
  const handleCardMonitoredChange = useCallback((gameId: number, monitored: boolean) => {
    setShelves(prev => prev.map(s => ({
      ...s,
      games: s.games.map(g => g.id === gameId ? { ...g, monitored } : g),
    })));
    loadMonitorCounts();
  }, [loadMonitorCounts]);

  useEffect(() => {
    const controller = new AbortController();
    loadShelves(controller.signal);
    loadMonitorCounts();

    // Debounced like Library: a scan fires this per batch
    let debounceTimer: ReturnType<typeof setTimeout> | null = null;
    const handleLibraryUpdate = () => {
      if (debounceTimer) clearTimeout(debounceTimer);
      debounceTimer = setTimeout(() => { loadShelves(); loadMonitorCounts(); }, 2000);
    };
    window.addEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
    return () => {
      controller.abort();
      window.removeEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
      if (debounceTimer) clearTimeout(debounceTimer);
    };
  }, [loadShelves, loadMonitorCounts]);

  const totalGames = useMemo(
    () => shelves.reduce((acc, s) => acc + s.total, 0),
    [shelves]
  );

  return (
    <div className="page platforms-page">
      <header className="page-header">
        <div>
          <h1>{t('platforms') || 'Platforms'}</h1>
          {!loading && (
            <p className="platforms-page__summary">
              {shelves.length} {t('platforms')?.toLowerCase() || 'platforms'} · {totalGames} {t('library')?.toLowerCase() || 'games'}
            </p>
          )}
        </div>
      </header>

      {error && <div className="platforms-page__error">{error}</div>}

      {loading && (
        <div className="platforms-page__shelves">
          {[0, 1, 2].map((i) => (
            <section key={i} className="shelf">
              <header className="shelf__header">
                <Skeleton variant="line" width={160} />
              </header>
              <div className="shelf__row">
                {Array.from({ length: 6 }, (_, j) => (
                  <div key={j} className="shelf__slot">
                    <Skeleton variant="card" />
                  </div>
                ))}
              </div>
            </section>
          ))}
        </div>
      )}

      {!loading && shelves.length === 0 && !error && (
        <p className="platforms-page__empty">
          {t('noGamesInLibrary') || 'No cartridges yet.'}{' '}
          <Link to="/settings#media">{t('openSettings') || 'Open Settings'}</Link>
        </p>
      )}

      {!loading && shelves.length > 0 && (
        <div className="platforms-page__shelves">
          {shelves.map((shelf) => (
            <section key={shelf.platform.id} className={`shelf ${toSlugClass(shelf.platform)}`}>
              <header className="shelf__header">
                <span className="retro-led" aria-hidden="true" />
                <h2 className="shelf__title">{shelf.platform.name}</h2>
                <span className="pixel shelf__count">{shelf.total}</span>
                {(() => {
                  const c = monitorCounts[String(shelf.platform.id)];
                  const total = c?.total ?? shelf.total;
                  const mon = c?.monitored ?? 0;
                  const state = total > 0 && mon >= total ? 'all' : mon > 0 ? 'partial' : 'none';
                  return (
                    <button
                      type="button"
                      className={`shelf__monitor is-${state}`}
                      onClick={() => handlePlatformMonitorToggle(shelf.platform.id)}
                      disabled={monitorBusyId === shelf.platform.id}
                      aria-pressed={state === 'all'}
                      title={t('platformMonitorToggleHint') || 'Toggle monitoring for every game on this platform (and the default for new games)'}
                    >
                      <FontAwesomeIcon icon={faBookmark} spin={monitorBusyId === shelf.platform.id} />
                      <span className="shelf__monitor-label">
                        {state === 'all'
                          ? (t('platformMonitoredAll') || 'Monitored')
                          : state === 'partial'
                            ? `${mon}/${total}`
                            : (t('platformMonitorEnable') || 'Monitor All')}
                      </span>
                    </button>
                  );
                })()}
                <Link
                  to={`/library?platform=${shelf.platform.id}`}
                  className="shelf__all"
                  onClick={(e) => {
                    e.preventDefault();
                    go(`/library?platform=${shelf.platform.id}`);
                  }}
                >
                  {t('viewAll') || 'View all →'}
                </Link>
              </header>

              <div className="shelf__row" role="list">
                {shelf.games.map((dto) => {
                  const game = dtoToGame(dto);
                  return (
                    <div key={dto.id} className="shelf__slot" role="listitem">
                      <GameCard
                        game={game}
                        reviewData={shelf.reviews[dto.id] ?? null}
                        onMonitoredChange={handleCardMonitoredChange}
                        onClick={() => navigate(`/game/${dto.id}`, {
                          state: {
                            fromPlatformId: String(shelf.platform.id),
                            fromPlatformName: shelf.platform.name,
                          },
                        })}
                      />
                    </div>
                  );
                })}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
