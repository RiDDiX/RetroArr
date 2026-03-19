import React, { useState, useEffect } from 'react';
import { dashboardApi, DashboardStats } from '../api/client';
import { useTranslation } from '../i18n/translations';
import './Statistics.css';

const Statistics: React.FC = () => {
  const { t } = useTranslation();
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadStats();
  }, []);

  const loadStats = async () => {
    try {
      const response = await dashboardApi.getStats();
      setStats(response.data);
    } catch (error) {
      console.error('Error loading statistics:', error);
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return (
      <div className="statistics loading">
        <div className="loading-spinner"></div>
        <p>Loading Statistics...</p>
      </div>
    );
  }

  const maxGenreCount = stats?.genreStats?.[0]?.count || 1;
  const maxPlatformCount = stats?.platformStats?.[0]?.count || 1;
  const maxYearCount = Math.max(...(stats?.yearStats?.map(y => y.count) || [1]));

  const ratingTotal = stats ? 
    stats.ratingStats.excellent + stats.ratingStats.good + stats.ratingStats.average + stats.ratingStats.poor + stats.ratingStats.unrated : 0;

  return (
    <div className="statistics">
      <div className="statistics-header">
        <h1>📊 {t('statistics') || 'Gaming Statistics'}</h1>
        <p className="statistics-subtitle">{t('statisticsSubtitle') || 'Detailed insights into your game collection'}</p>
      </div>

      {/* Overview Cards */}
      <div className="stats-overview">
        <div className="overview-card">
          <span className="overview-icon">🎮</span>
          <div className="overview-data">
            <span className="overview-value">{stats?.totalGames || 0}</span>
            <span className="overview-label">{t('totalGames') || 'Total Games'}</span>
          </div>
        </div>
        <div className="overview-card">
          <span className="overview-icon">💾</span>
          <div className="overview-data">
            <span className="overview-value">{stats?.installedGames || 0}</span>
            <span className="overview-label">{t('installed') || 'Installed'}</span>
          </div>
        </div>
        <div className="overview-card">
          <span className="overview-icon">🌐</span>
          <div className="overview-data">
            <span className="overview-value">{stats?.externalGames || 0}</span>
            <span className="overview-label">{t('external') || 'External'}</span>
          </div>
        </div>
        <div className="overview-card">
          <span className="overview-icon">❤️</span>
          <div className="overview-data">
            <span className="overview-value">{stats?.favoriteGames || 0}</span>
            <span className="overview-label">{t('favorites') || 'Favorites'}</span>
          </div>
        </div>
      </div>

      <div className="statistics-grid">
        {/* Genre Distribution */}
        <div className="stats-section">
          <h2>🎭 {t('genreDistribution') || 'Genre Distribution'}</h2>
          <div className="chart-container genre-chart">
            {stats?.genreStats && stats.genreStats.length > 0 ? (
              stats.genreStats.map((genre, idx) => (
                <div key={genre.genre} className="chart-bar-item" style={{ animationDelay: `${idx * 0.05}s` }}>
                  <div className="chart-bar-label">{genre.genre}</div>
                  <div className="chart-bar-track">
                    <div 
                      className="chart-bar-fill genre-fill" 
                      style={{ width: `${(genre.count / maxGenreCount) * 100}%` }}
                    />
                  </div>
                  <div className="chart-bar-value">{genre.count}</div>
                </div>
              ))
            ) : (
              <p className="no-data">No genre data available</p>
            )}
          </div>
        </div>

        {/* Platform Distribution */}
        <div className="stats-section">
          <h2>🖥️ {t('platformDistribution') || 'Platform Distribution'}</h2>
          <div className="chart-container platform-chart">
            {stats?.platformStats && stats.platformStats.length > 0 ? (
              stats.platformStats.map((platform, idx) => (
                <div key={platform.platformId} className="chart-bar-item" style={{ animationDelay: `${idx * 0.05}s` }}>
                  <div className="chart-bar-label">{platform.platform}</div>
                  <div className="chart-bar-track">
                    <div 
                      className="chart-bar-fill platform-fill" 
                      style={{ width: `${(platform.count / maxPlatformCount) * 100}%` }}
                    />
                  </div>
                  <div className="chart-bar-value">{platform.count}</div>
                </div>
              ))
            ) : (
              <p className="no-data">No platform data available</p>
            )}
          </div>
        </div>

        {/* Rating Distribution */}
        <div className="stats-section rating-section">
          <h2>⭐ {t('ratingDistribution') || 'Rating Distribution'}</h2>
          <div className="rating-chart">
            {stats && ratingTotal > 0 && (
              <>
                <div className="rating-pie">
                  <div className="pie-segment excellent" style={{ '--percentage': `${(stats.ratingStats.excellent / ratingTotal) * 100}` } as React.CSSProperties}>
                    <span>{stats.ratingStats.excellent}</span>
                  </div>
                </div>
                <div className="rating-legend">
                  <div className="legend-item">
                    <span className="legend-color excellent"></span>
                    <span className="legend-label">Excellent (80+)</span>
                    <span className="legend-value">{stats.ratingStats.excellent}</span>
                  </div>
                  <div className="legend-item">
                    <span className="legend-color good"></span>
                    <span className="legend-label">Good (60-79)</span>
                    <span className="legend-value">{stats.ratingStats.good}</span>
                  </div>
                  <div className="legend-item">
                    <span className="legend-color average"></span>
                    <span className="legend-label">Average (40-59)</span>
                    <span className="legend-value">{stats.ratingStats.average}</span>
                  </div>
                  <div className="legend-item">
                    <span className="legend-color poor"></span>
                    <span className="legend-label">Poor (&lt;40)</span>
                    <span className="legend-value">{stats.ratingStats.poor}</span>
                  </div>
                  <div className="legend-item">
                    <span className="legend-color unrated"></span>
                    <span className="legend-label">Unrated</span>
                    <span className="legend-value">{stats.ratingStats.unrated}</span>
                  </div>
                </div>
              </>
            )}
          </div>
        </div>

        {/* Year Distribution */}
        <div className="stats-section year-section">
          <h2>📅 {t('releaseYears') || 'Release Years'}</h2>
          <div className="year-chart">
            {stats?.yearStats && stats.yearStats.length > 0 ? (
              <div className="year-bars">
                {stats.yearStats.slice(0, 15).reverse().map((year) => (
                  <div key={year.year} className="year-bar-item">
                    <div 
                      className="year-bar" 
                      style={{ height: `${(year.count / maxYearCount) * 100}%` }}
                    >
                      <span className="year-count">{year.count}</span>
                    </div>
                    <span className="year-label">{year.year}</span>
                  </div>
                ))}
              </div>
            ) : (
              <p className="no-data">No year data available</p>
            )}
          </div>
        </div>

        {/* Status Distribution */}
        <div className="stats-section status-section">
          <h2>📋 {t('gameStatus') || 'Game Status'}</h2>
          <div className="status-grid">
            {stats?.statusStats && stats.statusStats.length > 0 ? (
              stats.statusStats.map((status) => (
                <div key={status.status} className={`status-card ${status.status.toLowerCase()}`}>
                  <span className="status-count">{status.count}</span>
                  <span className="status-label">{status.status}</span>
                </div>
              ))
            ) : (
              <p className="no-data">No status data available</p>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default Statistics;
