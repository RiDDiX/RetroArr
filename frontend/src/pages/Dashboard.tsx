import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { dashboardApi, DashboardStats } from '../api/client';
import { useTranslation } from '../i18n/translations';
import './Dashboard.css';

interface Game {
  id: number;
  title: string;
  overview?: string;
  images?: { coverUrl?: string; backgroundUrl?: string };
  genres?: string[];
  platform?: { name: string };
  communityRating?: number;
}

const Dashboard: React.FC = () => {
  const { t } = useTranslation();
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [randomGame, setRandomGame] = useState<Game | null>(null);
  const [loading, setLoading] = useState(true);
  const [shuffling, setShuffling] = useState(false);

  useEffect(() => {
    loadDashboardData();

    // Auto-refresh when library changes (game added/removed, download finished, scan complete)
    const handleLibraryUpdate = () => {
      console.log('[Dashboard] Library update detected. Refreshing stats...');
      loadDashboardData();
    };
    window.addEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
    return () => window.removeEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
  }, []);

  const loadDashboardData = async () => {
    try {
      const [statsResponse] = await Promise.all([
        dashboardApi.getStats()
      ]);
      setStats(statsResponse.data);
    } catch (error) {
      console.error('Error loading dashboard:', error);
    } finally {
      setLoading(false);
    }
  };

  const getRandomGame = async () => {
    setShuffling(true);
    try {
      const response = await dashboardApi.getRandom();
      setRandomGame(response.data);
    } catch (error) {
      console.error('Error getting random game:', error);
    } finally {
      setShuffling(false);
    }
  };

  if (loading) {
    return (
      <div className="dashboard loading">
        <div className="loading-spinner"></div>
        <p>Loading Dashboard...</p>
      </div>
    );
  }

  return (
    <div className="dashboard">
      <div className="dashboard-header">
        <h1>🎮 {t('dashboard') || 'Dashboard'}</h1>
        <p className="dashboard-subtitle">{t('dashboardSubtitle') || 'Your gaming overview at a glance'}</p>
      </div>

      {/* Stats Cards */}
      <div className="stats-grid">
        <div className="stat-card primary">
          <div className="stat-icon">🎯</div>
          <div className="stat-content">
            <h3>{t('totalGames') || 'Total Games'}</h3>
            <p className="stat-value">{stats?.totalGames || 0}</p>
          </div>
        </div>
        <div className="stat-card success">
          <div className="stat-icon">💾</div>
          <div className="stat-content">
            <h3>{t('installed') || 'Installed'}</h3>
            <p className="stat-value">{stats?.installedGames || 0}</p>
          </div>
        </div>
        <div className="stat-card warning">
          <div className="stat-icon">🌐</div>
          <div className="stat-content">
            <h3>{t('external') || 'External'}</h3>
            <p className="stat-value">{stats?.externalGames || 0}</p>
          </div>
        </div>
        <div className="stat-card danger">
          <div className="stat-icon">❤️</div>
          <div className="stat-content">
            <h3>{t('favorites') || 'Favorites'}</h3>
            <p className="stat-value">{stats?.favoriteGames || 0}</p>
          </div>
        </div>
      </div>

      {/* Main Content Grid */}
      <div className="dashboard-content">
        {/* Random Game Picker */}
        <div className="dashboard-section random-picker">
          <div className="section-header">
            <h2>🎲 {t('randomPick') || 'Random Pick'}</h2>
            <button 
              className="btn-shuffle" 
              onClick={getRandomGame}
              disabled={shuffling}
            >
              {shuffling ? '...' : '🔀'} {t('shuffle') || 'Shuffle'}
            </button>
          </div>
          
          {randomGame ? (
            <Link to={`/game/${randomGame.id}`} className="random-game-card">
              <div 
                className="random-game-bg"
                style={{ backgroundImage: randomGame.images?.backgroundUrl ? `url(${randomGame.images.backgroundUrl})` : 'none' }}
              />
              <div className="random-game-content">
                {randomGame.images?.coverUrl && (
                  <img src={randomGame.images.coverUrl} alt={randomGame.title} className="random-game-cover" />
                )}
                <div className="random-game-info">
                  <h3>{randomGame.title}</h3>
                  {randomGame.platform && <span className="platform-tag">{randomGame.platform.name}</span>}
                  {randomGame.genres && randomGame.genres.length > 0 && (
                    <div className="genre-tags">
                      {randomGame.genres.slice(0, 3).map((g, i) => (
                        <span key={i} className="genre-tag">{g}</span>
                      ))}
                    </div>
                  )}
                  {randomGame.communityRating && (
                    <div className="rating-badge">⭐ {randomGame.communityRating}%</div>
                  )}
                </div>
              </div>
            </Link>
          ) : (
            <div className="random-game-placeholder" onClick={getRandomGame}>
              <span className="placeholder-icon">🎲</span>
              <p>{t('clickToShuffle') || 'Click to get a random game suggestion!'}</p>
            </div>
          )}
        </div>

        {/* Recently Added */}
        <div className="dashboard-section recent-games">
          <div className="section-header">
            <h2>🆕 {t('recentlyAdded') || 'Recently Added'}</h2>
            <Link to="/library" className="view-all-link">{t('viewAll') || 'View All'} →</Link>
          </div>
          <div className="recent-games-list">
            {stats?.recentlyAdded && stats.recentlyAdded.length > 0 ? (
              stats.recentlyAdded.slice(0, 6).map(game => (
                <Link key={game.id} to={`/game/${game.id}`} className="recent-game-item">
                  {game.coverUrl ? (
                    <img src={game.coverUrl} alt={game.title} />
                  ) : (
                    <div className="no-cover">🎮</div>
                  )}
                  <div className="recent-game-info">
                    <span className="game-title">{game.title}</span>
                    <span className="game-date">{new Date(game.added).toLocaleDateString()}</span>
                  </div>
                </Link>
              ))
            ) : (
              <p className="no-games">{t('noRecentGames') || 'No recent games'}</p>
            )}
          </div>
        </div>

        {/* Genre Distribution */}
        <div className="dashboard-section genre-stats">
          <div className="section-header">
            <h2>📊 {t('topGenres') || 'Top Genres'}</h2>
          </div>
          <div className="genre-bars">
            {stats?.genreStats && stats.genreStats.length > 0 ? (
              stats.genreStats.slice(0, 8).map((genre, idx) => (
                <div key={genre.genre} className="genre-bar-item">
                  <div className="genre-label">{genre.genre}</div>
                  <div className="genre-bar-container">
                    <div 
                      className="genre-bar" 
                      style={{ 
                        width: `${(genre.count / stats.genreStats[0].count) * 100}%`,
                        animationDelay: `${idx * 0.1}s`
                      }}
                    />
                  </div>
                  <div className="genre-count">{genre.count}</div>
                </div>
              ))
            ) : (
              <p className="no-data">{t('noGenreData') || 'No genre data available'}</p>
            )}
          </div>
        </div>

        {/* Platform Distribution */}
        <div className="dashboard-section platform-stats">
          <div className="section-header">
            <h2>🖥️ {t('platforms') || 'Platforms'}</h2>
          </div>
          <div className="platform-grid">
            {stats?.platformStats && stats.platformStats.length > 0 ? (
              stats.platformStats.slice(0, 6).map(platform => (
                <div key={platform.platformId} className="platform-card">
                  <span className="platform-name">{platform.platform}</span>
                  <span className="platform-count">{platform.count}</span>
                </div>
              ))
            ) : (
              <p className="no-data">{t('noPlatformData') || 'No platform data available'}</p>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
