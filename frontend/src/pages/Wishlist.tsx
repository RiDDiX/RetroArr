import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { wishlistApi, getErrorMessage, type WishlistEntry, type WishlistRefreshResult } from '../api/client';
import { useTranslation } from '../i18n/translations';
import './Wishlist.css';

const COUNTRY_CODES = ['US', 'DE', 'GB', 'FR', 'ES', 'IT', 'NL', 'PL', 'BR', 'JP', 'AU', 'CA'];

const formatPrice = (price: number | null | undefined, currency: string | null | undefined) => {
  if (price == null) return '-';
  if (price === 0) return 'Free';
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: currency || 'USD',
      maximumFractionDigits: 2,
    }).format(price);
  } catch {
    return `${price.toFixed(2)} ${currency || ''}`.trim();
  }
};

const formatDate = (iso: string | null | undefined) => {
  if (!iso) return '-';
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
};

export default function Wishlist() {
  const { t } = useTranslation();
  const [entries, setEntries] = useState<WishlistEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [country, setCountry] = useState<string>(() => {
    try { return localStorage.getItem('RetroArr_wishlist_country') || 'US'; }
    catch { return 'US'; }
  });
  const [targetInputs, setTargetInputs] = useState<Record<number, string>>({});
  const [busyGame, setBusyGame] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await wishlistApi.getAll();
      setEntries(resp.data.entries || []);
    } catch (e) {
      setError(getErrorMessage(e, t('wishlistLoadFailed')));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    try { localStorage.setItem('RetroArr_wishlist_country', country); }
    catch { /* ignore storage errors */ }
  }, [country]);

  const refresh = async () => {
    setRefreshing(true);
    setError(null);
    setNotice(null);
    try {
      const resp = await wishlistApi.refresh(country);
      const r: WishlistRefreshResult = resp.data;
      setNotice(
        t('wishlistRefreshSummary')
          .replace('{checked}', String(r.checkedCount))
          .replace('{updated}', String(r.updated))
          .replace('{dropped}', String(r.dropped))
          .replace('{target}', String(r.targetReached))
          .replace('{failed}', String(r.failed))
      );
      await load();
    } catch (e) {
      setError(getErrorMessage(e, t('wishlistRefreshFailed')));
    } finally {
      setRefreshing(false);
    }
  };

  const trackable = useMemo(() => entries.filter(e => e.game.steamId != null), [entries]);
  const untrackable = useMemo(() => entries.filter(e => e.game.steamId == null), [entries]);

  const startTracking = async (entry: WishlistEntry) => {
    if (entry.game.steamId == null) return;
    const inputVal = targetInputs[entry.game.id];
    const targetPrice = inputVal ? parseFloat(inputVal) : null;
    setBusyGame(entry.game.id);
    try {
      await wishlistApi.setWatch(entry.game.id, {
        provider: 'steam',
        externalId: String(entry.game.steamId),
        targetPrice: Number.isFinite(targetPrice as number) ? targetPrice : null,
        notifyOnAnyDrop: true,
      });
      await load();
    } catch (e) {
      setError(getErrorMessage(e, t('wishlistTrackFailed')));
    } finally {
      setBusyGame(null);
    }
  };

  const stopTracking = async (entry: WishlistEntry) => {
    if (!entry.watch) return;
    setBusyGame(entry.game.id);
    try {
      await wishlistApi.removeWatch(entry.game.id, entry.watch.provider);
      await load();
    } catch (e) {
      setError(getErrorMessage(e, t('wishlistRemoveFailed')));
    } finally {
      setBusyGame(null);
    }
  };

  const updateTarget = async (entry: WishlistEntry) => {
    if (!entry.watch) return;
    const inputVal = targetInputs[entry.game.id];
    if (inputVal === undefined) return;
    const trimmed = inputVal.trim();
    const targetPrice = trimmed === '' ? null : parseFloat(trimmed);
    if (trimmed !== '' && !Number.isFinite(targetPrice as number)) return;
    setBusyGame(entry.game.id);
    try {
      await wishlistApi.setWatch(entry.game.id, {
        provider: entry.watch.provider,
        externalId: entry.watch.externalId,
        targetPrice: targetPrice,
        notifyOnAnyDrop: entry.watch.notifyOnAnyDrop,
      });
      await load();
    } catch (e) {
      setError(getErrorMessage(e, t('wishlistTrackFailed')));
    } finally {
      setBusyGame(null);
    }
  };

  if (loading) {
    return <div className="page-loading"><div className="loading-spinner" /></div>;
  }

  return (
    <div className="wishlist-page">
      <header className="wishlist-header">
        <div>
          <h1>{t('wishlistTitle')}</h1>
          <p className="wishlist-subtitle">{t('wishlistSubtitle')}</p>
        </div>
        <div className="wishlist-controls">
          <label className="wishlist-country">
            <span>{t('wishlistCountry')}</span>
            <select value={country} onChange={e => setCountry(e.target.value)} disabled={refreshing}>
              {COUNTRY_CODES.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          </label>
          <button type="button" className="btn-primary" onClick={refresh} disabled={refreshing}>
            {refreshing ? t('wishlistRefreshing') : t('wishlistRefresh')}
          </button>
        </div>
      </header>

      {error && <div className="alert alert-error">{error}</div>}
      {notice && <div className="alert alert-info">{notice}</div>}

      {entries.length === 0 && (
        <div className="wishlist-empty">
          <p>{t('wishlistEmpty')}</p>
          <p className="wishlist-empty-hint">{t('wishlistEmptyHint')}</p>
        </div>
      )}

      {trackable.length > 0 && (
        <section className="wishlist-section">
          <h2>{t('wishlistTrackable')}</h2>
          <div className="wishlist-grid">
            {trackable.map(entry => (
              <article key={entry.game.id} className="wishlist-card">
                {entry.game.cover ? (
                  <img src={entry.game.cover} alt="" className="wishlist-cover" />
                ) : (
                  <div className="wishlist-cover wishlist-cover--placeholder" />
                )}
                <div className="wishlist-info">
                  <h3>
                    <Link to={`/game/${entry.game.id}`}>{entry.game.title}</Link>
                  </h3>
                  <div className="wishlist-meta">
                    {entry.game.platform && <span>{entry.game.platform}</span>}
                    {entry.game.year > 0 && <span>{entry.game.year}</span>}
                  </div>
                  {entry.watch ? (
                    <>
                      <div className="wishlist-prices">
                        <div className="wishlist-current">
                          <span className="wishlist-price-label">{t('wishlistCurrentPrice')}</span>
                          <span className="wishlist-price-value">
                            {formatPrice(entry.watch.currentPrice, entry.watch.currency)}
                            {entry.watch.isOnSale && entry.watch.discountPercent != null && entry.watch.discountPercent > 0 && (
                              <span className="wishlist-sale-badge">-{entry.watch.discountPercent}%</span>
                            )}
                          </span>
                        </div>
                        {entry.watch.previousPrice != null && entry.watch.previousPrice !== entry.watch.currentPrice && (
                          <div className="wishlist-previous">
                            <span className="wishlist-price-label">{t('wishlistPreviousPrice')}</span>
                            <span className="wishlist-price-value wishlist-price-previous">
                              {formatPrice(entry.watch.previousPrice, entry.watch.currency)}
                            </span>
                          </div>
                        )}
                      </div>
                      <div className="wishlist-target">
                        <label>
                          <span>{t('wishlistTargetPrice')}</span>
                          <input
                            type="number"
                            min={0}
                            step="0.01"
                            value={targetInputs[entry.game.id] ?? (entry.watch.targetPrice?.toString() ?? '')}
                            onChange={e => setTargetInputs(prev => ({ ...prev, [entry.game.id]: e.target.value }))}
                            placeholder={t('wishlistTargetPlaceholder')}
                            disabled={busyGame === entry.game.id}
                          />
                        </label>
                        <button
                          type="button"
                          className="btn-secondary"
                          onClick={() => updateTarget(entry)}
                          disabled={busyGame === entry.game.id}
                        >
                          {t('save')}
                        </button>
                      </div>
                      <div className="wishlist-footer">
                        <span className="wishlist-last">
                          {t('wishlistLastChecked')}: {formatDate(entry.watch.lastCheckedAt)}
                        </span>
                        <button
                          type="button"
                          className="btn-danger-link"
                          onClick={() => stopTracking(entry)}
                          disabled={busyGame === entry.game.id}
                        >
                          {t('wishlistStopTracking')}
                        </button>
                      </div>
                    </>
                  ) : (
                    <div className="wishlist-track-setup">
                      <label>
                        <span>{t('wishlistTargetPrice')}</span>
                        <input
                          type="number"
                          min={0}
                          step="0.01"
                          value={targetInputs[entry.game.id] ?? ''}
                          onChange={e => setTargetInputs(prev => ({ ...prev, [entry.game.id]: e.target.value }))}
                          placeholder={t('wishlistTargetPlaceholder')}
                          disabled={busyGame === entry.game.id}
                        />
                      </label>
                      <button
                        type="button"
                        className="btn-primary"
                        onClick={() => startTracking(entry)}
                        disabled={busyGame === entry.game.id}
                      >
                        {t('wishlistStartTracking')}
                      </button>
                    </div>
                  )}
                </div>
              </article>
            ))}
          </div>
        </section>
      )}

      {untrackable.length > 0 && (
        <section className="wishlist-section">
          <h2>{t('wishlistUntrackable')}</h2>
          <p className="wishlist-section-hint">{t('wishlistUntrackableHint')}</p>
          <ul className="wishlist-list">
            {untrackable.map(entry => (
              <li key={entry.game.id} className="wishlist-list-item">
                <Link to={`/game/${entry.game.id}`}>{entry.game.title}</Link>
                {entry.game.platform && <span className="wishlist-list-meta">{entry.game.platform}</span>}
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
