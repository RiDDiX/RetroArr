import React, { useEffect, useState } from 'react';
import { useUI } from '../context/UIContext';
import apiClient from '../api/client';
import './KofiOverlay.css';

interface ChangelogRelease {
    version: string;
    date: string;
    changes: string[];
}

function parseChangelog(md: string): ChangelogRelease[] {
    const releases: ChangelogRelease[] = [];
    const sections = md.split(/^## /m).filter(Boolean);

    for (const section of sections) {
        const lines = section.split('\n');
        const header = lines[0]?.trim() || '';

        // Parse "v1.0.8 (2026-03-15)" format
        const match = header.match(/^v?([\d.]+)\s*\((\d{4}-\d{2}-\d{2})\)/);
        if (!match) continue;

        const version = match[1];
        const date = match[2];
        const changes: string[] = [];

        for (let i = 1; i < lines.length; i++) {
            const line = lines[i].trim();
            if (line.startsWith('- ')) {
                // Strip trailing commit hash like (abc1234)
                const cleaned = line.substring(2).replace(/\s*\([a-f0-9]{7,}\)\s*$/, '').trim();
                // Skip version bump commits
                if (cleaned.startsWith('chore:') || cleaned.includes('[skip ci]')) continue;
                if (cleaned) changes.push(cleaned);
            }
        }

        if (changes.length > 0) {
            releases.push({ version, date, changes });
        }
    }

    return releases;
}

const VersionOverlay: React.FC = () => {
    const { isKofiOpen, closeKofi } = useUI();
    const [appVersion, setAppVersion] = useState<string>('...');
    const [changelog, setChangelog] = useState<ChangelogRelease[]>([]);
    const [loaded, setLoaded] = useState(false);

    useEffect(() => {
        if (!isKofiOpen || loaded) return;

        const fetchData = async () => {
            try {
                const [statusRes, changelogRes] = await Promise.all([
                    apiClient.get('/system/status'),
                    apiClient.get('/system/changelog')
                ]);
                if (statusRes.data?.version) {
                    setAppVersion(statusRes.data.version);
                }
                if (changelogRes.data?.changelog) {
                    setChangelog(parseChangelog(changelogRes.data.changelog));
                }
            } catch (err) {
                console.error('[VersionOverlay] Failed to load system info:', err);
            } finally {
                setLoaded(true);
            }
        };
        fetchData();
    }, [isKofiOpen, loaded]);

    if (!isKofiOpen) return null;

    return (
        <div className="kofi-overlay-backdrop" onClick={closeKofi}>
            <div className="kofi-overlay-content" onClick={(e) => e.stopPropagation()} style={{ padding: '2rem', maxWidth: '500px', maxHeight: '80vh', overflow: 'auto' }}>
                <button className="kofi-close-btn" onClick={closeKofi}>&times;</button>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem', color: 'var(--ctp-text)' }}>
                    <div style={{ textAlign: 'center' }}>
                        <h2 style={{ margin: 0, fontSize: '1.8rem', marginBottom: '0.5rem' }}>RetroArr</h2>
                        <div style={{ 
                            display: 'inline-block',
                            padding: '4px 12px', 
                            background: 'rgba(137, 180, 250, 0.2)', 
                            borderRadius: '20px',
                            border: '1px solid rgba(137, 180, 250, 0.4)',
                            color: 'var(--ctp-blue)',
                            fontWeight: 'bold'
                        }}>
                            v{appVersion}
                        </div>
                        <p style={{ opacity: 0.7, marginTop: '0.5rem', fontSize: '0.9rem' }}>A PVR for Video Games</p>
                    </div>

                    <div>
                        <h3 style={{ margin: '0 0 1rem 0', fontSize: '1.1rem', borderBottom: '1px solid rgba(255,255,255,0.1)', paddingBottom: '0.5rem' }}>
                            Changelog
                        </h3>
                        {changelog.length === 0 && loaded && (
                            <p style={{ opacity: 0.5, fontSize: '0.85rem' }}>No changelog available.</p>
                        )}
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                            {changelog.map((release, idx) => (
                                <div key={release.version} style={{ 
                                    padding: '0.75rem',
                                    background: idx === 0 ? 'rgba(137, 180, 250, 0.1)' : 'rgba(255,255,255,0.03)',
                                    borderRadius: '8px',
                                    border: idx === 0 ? '1px solid rgba(137, 180, 250, 0.2)' : '1px solid rgba(255,255,255,0.05)'
                                }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                                        <span style={{ fontWeight: 'bold', color: idx === 0 ? 'var(--ctp-blue)' : 'var(--ctp-text)' }}>v{release.version}</span>
                                        <span style={{ fontSize: '0.8rem', opacity: 0.6 }}>{release.date}</span>
                                    </div>
                                    <ul style={{ margin: 0, paddingLeft: '1.2rem', fontSize: '0.85rem', opacity: 0.85 }}>
                                        {release.changes.map((change, i) => (
                                            <li key={i} style={{ marginBottom: '0.25rem' }}>{change}</li>
                                        ))}
                                    </ul>
                                </div>
                            ))}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default VersionOverlay;
