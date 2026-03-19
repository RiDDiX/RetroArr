import React, { useState, useEffect } from 'react';
import { steamApi } from '../api/client';
import { t as translate } from '../i18n/translations';
import './User.css';

interface SteamProfile {
    steamId: string;
    personaName: string;
    avatarUrl: string;
    personaState: number;
    gameExtraInfo?: string;
    realName?: string;
    countryCode?: string;
    accountCreated?: string;
    level?: number;
}

interface SteamLibraryStats {
    totalGames: number;
    totalMinutesPlayed: number;
    totalHoursPlayed: number;
}

interface SteamRecentGame {
    appId: number;
    name: string;
    playtime2Weeks: number;
    playtimeForever: number;
    iconUrl: string | null;
    achieved: number;
    totalAchievements: number;
    completionPercent: number;
    latestNews?: {
        title: string;
        url: string;
        feedLabel: string;
        date: string;
    };
}

interface SteamFriend {
    steamId: string;
    personaName: string;
    avatarUrl: string;
    personaState: number;
    gameExtraInfo: string;
}

const User: React.FC = () => {
    const [profile, setProfile] = useState<SteamProfile | null>(null);
    const [stats, setStats] = useState<SteamLibraryStats | null>(null);
    const [recentGames, setRecentGames] = useState<SteamRecentGame[]>([]);
    const [friends, setFriends] = useState<SteamFriend[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        const fetchData = async () => {
            try {
                try {
                    const profileRes = await steamApi.getProfile();
                    if (profileRes.data) setProfile(profileRes.data);
                } catch (e) {
                    console.warn('Failed to load profile', e);
                }

                try {
                    const statsRes = await steamApi.getStats();
                    if (statsRes.data) setStats(statsRes.data);
                } catch (e) {
                    console.warn('Failed to load stats', e);
                }

                try {
                    const recentRes = await steamApi.getRecent();
                    if (Array.isArray(recentRes.data)) {
                        setRecentGames(recentRes.data);
                    } else {
                        setRecentGames([]);
                    }
                } catch (e) {
                    console.warn('Failed to load recent games', e);
                    setRecentGames([]);
                }

                try {
                    const friendRes = await steamApi.getFriends();
                    if (Array.isArray(friendRes.data)) {
                        setFriends(friendRes.data);
                    }
                } catch (e) {
                    console.warn('Failed to load friends', e);
                }

            } catch (error) {
                console.error('Error fetching Steam data:', error);
            } finally {
                setLoading(false);
            }
        };

        fetchData();
    }, []);

    const getStatusClass = (state: number, game?: string) => {
        if (game) return 'in-game';
        if (state === 1) return 'online';
        return 'offline';
    };

    const t = (key: string) => translate(key as Parameters<typeof translate>[0]);

    const getStatusText = (state: number, game?: string) => {
        if (game) return `${t('playing') || 'Playing'} ${game}`;
        switch (state) {
            case 0: return 'Offline';
            case 1: return 'Online';
            case 2: return 'Busy';
            case 3: return 'Away';
            default: return 'Online';
        }
    };

    return (
        <div className="user-page">
            <div className="user-profile-header">
                {loading ? (
                    <div className="user-loading">{t('loading') || 'Loading...'}</div>
                ) : profile ? (
                    <>
                        <div className="profile-avatar">
                            <img
                                src={profile.avatarUrl}
                                alt={profile.personaName}
                                className={`avatar-img ${getStatusClass(profile.personaState, profile.gameExtraInfo)}`}
                            />
                        </div>
                        <div className="profile-info">
                            <div className="profile-name-row">
                                <h1 className="profile-name">{profile.personaName}</h1>
                                {profile.countryCode && (
                                    <img
                                        src={`https://flagcdn.com/24x18/${profile.countryCode.toLowerCase()}.png`}
                                        alt={profile.countryCode}
                                        title={profile.countryCode}
                                        className="profile-flag"
                                    />
                                )}
                                {profile.level !== undefined && (
                                    <div className="profile-level" title={`Steam Level ${profile.level}`}>
                                        {profile.level}
                                    </div>
                                )}
                            </div>

                            <div className={`profile-status ${getStatusClass(profile.personaState, profile.gameExtraInfo)}`}>
                                {getStatusText(profile.personaState, profile.gameExtraInfo)}
                            </div>

                            {(profile.realName || profile.accountCreated) && (
                                <div className="profile-meta">
                                    {profile.realName && <span>{profile.realName}</span>}
                                    {profile.accountCreated && (
                                        <span>
                                            {t('memberSince') || 'Member since'} {new Date(profile.accountCreated).getFullYear()}
                                        </span>
                                    )}
                                </div>
                            )}

                            <div className="profile-id">
                                Steam ID: {profile.steamId}
                            </div>
                        </div>

                        {stats && (
                            <div className="profile-stats">
                                <div className="stat-block">
                                    <h3 className="stat-label">{t('totalGames') || 'Total Games'}</h3>
                                    <div className="stat-value accent">{stats.totalGames}</div>
                                </div>
                                <div className="stat-block">
                                    <h3 className="stat-label">{t('hoursPlayed') || 'Hours Played'}</h3>
                                    <div className="stat-value highlight">{stats.totalHoursPlayed.toLocaleString()}</div>
                                </div>
                            </div>
                        )}
                    </>
                ) : (
                    <div className="empty-library">
                        <p>{t('steamNotConnected') || 'Steam not connected or configured.'}</p>
                    </div>
                )}
            </div>

            {recentGames.length > 0 && (
                <div className="recent-activity">
                    <h3 className="section-title">{t('recentActivity') || 'Recent Activity'}</h3>
                    <div className="recent-list">
                        {recentGames.map(game => (
                            <div key={game.appId} className="recent-card">
                                {game.iconUrl ? (
                                    <img src={game.iconUrl} alt={game.name} className="recent-icon" />
                                ) : (
                                    <div className="recent-icon placeholder" />
                                )}
                                <div className="recent-info">
                                    <div className="recent-header">
                                        <h4 className="recent-name">{game.name}</h4>
                                        <span className="recent-time">{Math.round(game.playtime2Weeks / 60)}h past 2 weeks</span>
                                    </div>

                                    {game.totalAchievements > 0 && (
                                        <div className="achievement-progress">
                                            <div className="achievement-header">
                                                <span>{t('achievements') || 'Achievements'}</span>
                                                <span>{game.achieved} / {game.totalAchievements} ({game.completionPercent}%)</span>
                                            </div>
                                            <div className="achievement-bar">
                                                <div className="achievement-fill" style={{ width: `${game.completionPercent}%` }} />
                                            </div>
                                        </div>
                                    )}

                                    {game.latestNews && (
                                        <div className="recent-news">
                                            <span className="news-label">
                                                {game.latestNews.feedLabel || 'NEWS'}
                                            </span>
                                            <a
                                                href={game.latestNews.url}
                                                target="_blank"
                                                rel="noopener noreferrer"
                                                className="news-link"
                                            >
                                                {game.latestNews.title}
                                            </a>
                                            <span className="news-date">
                                                {new Date(game.latestNews.date).toLocaleDateString()}
                                            </span>
                                        </div>
                                    )}
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {friends.length > 0 && (
                <div className="friends-section">
                    <h3 className="section-title">{t('friends') || 'Friends'} ({friends.length})</h3>
                    <div className="friends-grid">
                        {friends.map(friend => (
                            <div key={friend.steamId} className={`friend-card ${getStatusClass(friend.personaState, friend.gameExtraInfo)}`}>
                                <img
                                    src={friend.avatarUrl}
                                    alt={friend.personaName}
                                    className="friend-avatar"
                                />
                                <div className="friend-info">
                                    <div className="friend-name">{friend.personaName}</div>
                                    <div className={`friend-status ${getStatusClass(friend.personaState, friend.gameExtraInfo)}`}>
                                        {friend.gameExtraInfo ? `${t('playing') || 'Playing'} ${friend.gameExtraInfo}` : getStatusText(friend.personaState)}
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {!loading && !profile && (
                <div className="empty-library">
                    <h3>{t('userPageDesc')}</h3>
                </div>
            )}
        </div>
    );
};

export default User;
