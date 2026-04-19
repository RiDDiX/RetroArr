import React, { useState, useEffect } from 'react';
import apiClient from '../../api/client';

interface Platform {
  id: number;
  name: string;
  slug: string;
  folderName: string;
  category?: string;
  enabled: boolean;
  igdbPlatformId?: number;
  preferredMetadataSource?: string;
}

interface PlatformsTabProps {
  language: string;
  t: (key: string) => string;
}

const PlatformsTab: React.FC<PlatformsTabProps> = ({ t }) => {
  const [platforms, setPlatforms] = useState<Platform[]>([]);
  const [platformCategories, setPlatformCategories] = useState<string[]>([]);
  const [expandedCategories, setExpandedCategories] = useState<Set<string>>(new Set());

  useEffect(() => {
    loadPlatforms();
  }, []);

  const loadPlatforms = async () => {
    try {
      const response = await apiClient.get('/platform');
      setPlatforms(response.data);
      const categories = [...new Set(response.data.map((p: Platform) => p.category).filter(Boolean))] as string[];
      setPlatformCategories(categories);
      setExpandedCategories(new Set(categories));
    } catch (error) {
      console.error('Error loading platforms:', error);
    }
  };

  const togglePlatformEnabled = async (platform: Platform) => {
    const newEnabled = !platform.enabled;
    setPlatforms(prev => prev.map(p => p.id === platform.id ? { ...p, enabled: newEnabled } : p));
    try {
      await apiClient.put(`/platform/${platform.id}/toggle`, { enabled: newEnabled });
      // Tell the rest of the app so sidebar, library filter and shelves
      // refresh without waiting for a full page reload.
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
    } catch {
      setPlatforms(prev => prev.map(p => p.id === platform.id ? { ...p, enabled: !newEnabled } : p));
    }
  };

  const changeMetadataSource = async (platform: Platform, source: string) => {
    const prev = platform.preferredMetadataSource || 'igdb';
    setPlatforms(ps => ps.map(p => p.id === platform.id ? { ...p, preferredMetadataSource: source } : p));
    try {
      await apiClient.put(`/platform/${platform.id}/metadata-source`, { source });
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
    } catch {
      setPlatforms(ps => ps.map(p => p.id === platform.id ? { ...p, preferredMetadataSource: prev } : p));
    }
  };

  const toggleCategory = (category: string) => {
    setExpandedCategories(prev => {
      const newSet = new Set(prev);
      if (newSet.has(category)) newSet.delete(category);
      else newSet.add(category);
      return newSet;
    });
  };

  const getPlatformsByCategory = (category: string): Platform[] => {
    return platforms.filter(p => p.category === category);
  };

  return (
    <div className="settings-section" id="platforms">
      <div className="section-header-with-logo">
        <h3>{t('platformsTitle') || 'Platforms'}</h3>
      </div>
      <p className="settings-description">
        {t('platformsDesc') || 'Enable or disable platforms for your game library. Enabled platforms will appear in the library filter and when adding games.'}
      </p>
      
      <div className="platforms-list" style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        {platformCategories.map(category => (
          <div key={category} className="platform-category" style={{
            backgroundColor: 'var(--ctp-base)',
            borderRadius: '8px',
            overflow: 'hidden',
            border: '1px solid var(--ctp-surface0)'
          }}>
            <div 
              className="category-header" 
              onClick={() => toggleCategory(category)}
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '0.75rem 1rem',
                backgroundColor: 'var(--ctp-surface0)',
                cursor: 'pointer',
                userSelect: 'none'
              }}
            >
              <h4 style={{ margin: 0, display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <span style={{ 
                  transform: expandedCategories.has(category) ? 'rotate(90deg)' : 'rotate(0deg)',
                  transition: 'transform 0.2s',
                  display: 'inline-block'
                }}>▶</span>
                {category}
                <span style={{ fontSize: '0.8rem', color: 'var(--ctp-overlay0)', fontWeight: 'normal' }}>
                  ({getPlatformsByCategory(category).filter(p => p.enabled).length}/{getPlatformsByCategory(category).length})
                </span>
              </h4>
            </div>
            
            {expandedCategories.has(category) && (
              <div className="category-platforms" style={{ padding: '0.5rem' }}>
                {getPlatformsByCategory(category).map(platform => (
                  <div 
                    key={platform.id} 
                    className="platform-item"
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      padding: '0.5rem 0.75rem',
                      borderRadius: '4px',
                      backgroundColor: platform.enabled ? 'rgba(137, 180, 250, 0.1)' : 'transparent',
                      marginBottom: '0.25rem'
                    }}
                  >
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <span style={{ fontWeight: platform.enabled ? 'bold' : 'normal' }}>
                        {platform.name}
                      </span>
                      <span style={{ fontSize: '0.75rem', color: 'var(--ctp-overlay0)', marginLeft: '0.5rem' }}>
                        /{platform.folderName}/
                      </span>
                    </div>
                    <select
                      value={platform.preferredMetadataSource || 'igdb'}
                      onChange={(e) => changeMetadataSource(platform, e.target.value)}
                      style={{
                        backgroundColor: 'var(--ctp-surface0)',
                        color: 'var(--ctp-text)',
                        border: '1px solid var(--ctp-surface1)',
                        borderRadius: '4px',
                        padding: '2px 6px',
                        fontSize: '0.75rem',
                        marginRight: '0.75rem',
                        cursor: 'pointer',
                        minWidth: '110px'
                      }}
                    >
                      <option value="igdb">IGDB</option>
                      <option value="screenscraper">ScreenScraper</option>
                    </select>
                    <label className="toggle-switch" style={{ position: 'relative', display: 'inline-block', width: '40px', height: '20px', flexShrink: 0 }}>
                      <input
                        type="checkbox"
                        checked={platform.enabled}
                        onChange={() => togglePlatformEnabled(platform)}
                        style={{ opacity: 0, width: 0, height: 0 }}
                      />
                      <span style={{
                        position: 'absolute',
                        cursor: 'pointer',
                        top: 0, left: 0, right: 0, bottom: 0,
                        backgroundColor: platform.enabled ? 'var(--ctp-blue)' : 'var(--ctp-surface1)',
                        transition: '0.3s',
                        borderRadius: '20px'
                      }}>
                        <span style={{
                          position: 'absolute',
                          content: '""',
                          height: '16px', width: '16px',
                          left: platform.enabled ? '22px' : '2px',
                          bottom: '2px',
                          backgroundColor: 'white',
                          transition: '0.3s',
                          borderRadius: '50%'
                        }}></span>
                      </span>
                    </label>
                  </div>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>
      
      <p style={{ marginTop: '1rem', fontSize: '0.85rem', color: 'var(--ctp-overlay0)' }}>
        {t('platformsNote') || 'Note: Folder names are used for RetroArch/Batocera compatible ROM organization.'}
      </p>
    </div>
  );
};

export default PlatformsTab;
