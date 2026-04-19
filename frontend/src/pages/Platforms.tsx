import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { gamesApi, platformsApi, type GameListDto, type Platform } from '../api/client';
import GameCard from '../components/GameCard';
import { Skeleton } from '../components/retro';
import { useRetroNavigate } from '../hooks/useRetroNavigate';
import { t } from '../i18n/translations';
import './Platforms.css';

type Shelf = {
  platform: Platform;
  games: GameListDto[];
  total: number;
};

const SHELF_PAGE_SIZE = 12;

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
  };
}

export default function Platforms() {
  const navigate = useNavigate();
  const go = useRetroNavigate();
  const [shelves, setShelves] = useState<Shelf[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadShelves = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const platformsResp = await platformsApi.getEnabled();
      const enabled = platformsResp.data || [];

      // Fire per-platform sample requests in parallel — cheap because
      // each is capped at SHELF_PAGE_SIZE.
      const results = await Promise.all(
        enabled.map(async (p) => {
          try {
            const r = await gamesApi.getPaged({
              platformId: p.id,
              pageSize: SHELF_PAGE_SIZE,
              sortOrder: 'asc',
            });
            return {
              platform: p,
              games: r.data.items || [],
              total: r.data.totalItems || 0,
            } as Shelf;
          } catch {
            return { platform: p, games: [], total: 0 } as Shelf;
          }
        })
      );

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

  useEffect(() => {
    const controller = new AbortController();
    loadShelves(controller.signal);

    const handleLibraryUpdate = () => { loadShelves(); };
    window.addEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
    return () => {
      controller.abort();
      window.removeEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
    };
  }, [loadShelves]);

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
