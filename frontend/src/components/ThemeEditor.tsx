import React, { useState } from 'react';
import { useTheme, THEMES, ThemeColors, Theme } from '../context/ThemeContext';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faPalette, faCheck, faUndo, faSave, faCopy } from '@fortawesome/free-solid-svg-icons';
import './ThemeEditor.css';

interface ColorGroup {
  name: string;
  colors: (keyof ThemeColors)[];
}

const COLOR_GROUPS: ColorGroup[] = [
  {
    name: 'Base Colors',
    colors: ['base', 'mantle', 'crust']
  },
  {
    name: 'Surface Colors',
    colors: ['surface0', 'surface1', 'surface2']
  },
  {
    name: 'Overlay Colors',
    colors: ['overlay0', 'overlay1', 'overlay2']
  },
  {
    name: 'Text Colors',
    colors: ['text', 'subtext0', 'subtext1']
  },
  {
    name: 'Primary Accent',
    colors: ['primary', 'primaryHover']
  },
  {
    name: 'Accent Colors',
    colors: ['blue', 'lavender', 'sapphire', 'sky', 'teal', 'green', 'yellow', 'peach', 'maroon', 'red', 'mauve', 'pink', 'flamingo', 'rosewater']
  }
];

const COLOR_LABELS: Record<keyof ThemeColors, string> = {
  base: 'Background',
  mantle: 'Mantle',
  crust: 'Crust (Darkest)',
  surface0: 'Surface 0',
  surface1: 'Surface 1',
  surface2: 'Surface 2',
  overlay0: 'Overlay 0',
  overlay1: 'Overlay 1',
  overlay2: 'Overlay 2',
  text: 'Text',
  subtext0: 'Subtext 0',
  subtext1: 'Subtext 1',
  blue: 'Blue',
  lavender: 'Lavender',
  sapphire: 'Sapphire',
  sky: 'Sky',
  teal: 'Teal',
  green: 'Green',
  yellow: 'Yellow',
  peach: 'Peach',
  maroon: 'Maroon',
  red: 'Red',
  mauve: 'Mauve',
  pink: 'Pink',
  flamingo: 'Flamingo',
  rosewater: 'Rosewater',
  primary: 'Primary',
  primaryHover: 'Primary Hover'
};

const ThemeEditor: React.FC = () => {
  const { currentTheme, setTheme, themes, customTheme, setCustomTheme, updateCustomColor, resetCustomTheme } = useTheme();
  const [activeTab, setActiveTab] = useState<'presets' | 'customize'>('presets');
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const [previewTheme, setPreviewTheme] = useState<Theme | null>(null);
  const [notification, setNotification] = useState<string | null>(null);

  const showNotification = (message: string) => {
    setNotification(message);
    setTimeout(() => setNotification(null), 3000);
  };

  const handleThemeSelect = (themeId: string) => {
    setTheme(themeId);
    showNotification(`Theme "${themes.find(t => t.id === themeId)?.name}" applied!`);
  };

  const handleStartCustomize = (baseTheme: Theme) => {
    setCustomTheme({
      ...baseTheme,
      id: 'custom',
      name: 'Custom Theme',
      description: `Based on ${baseTheme.name}`,
      isCustom: true
    });
    setActiveTab('customize');
  };

  const handleColorChange = (colorKey: keyof ThemeColors, value: string) => {
    updateCustomColor(colorKey, value);
  };

  const handleApplyCustomTheme = () => {
    if (customTheme) {
      setTheme('custom');
      showNotification('Custom theme applied!');
    }
  };

  const handleResetCustomTheme = () => {
    if (window.confirm('Reset custom theme to default?')) {
      resetCustomTheme();
      setTheme(THEMES[0].id);
      showNotification('Custom theme reset');
    }
  };

  const handleExportTheme = () => {
    const theme = customTheme || currentTheme;
    const json = JSON.stringify(theme, null, 2);
    navigator.clipboard.writeText(json);
    showNotification('Theme copied to clipboard!');
  };

  const handlePreview = (theme: Theme) => {
    setPreviewTheme(theme);
    // Temporarily apply theme for preview
    const root = document.documentElement;
    Object.entries(theme.colors).forEach(([key, value]) => {
      root.style.setProperty(`--ctp-${key.replace(/([A-Z])/g, '-$1').toLowerCase()}`, value);
    });
  };

  const handlePreviewEnd = () => {
    setPreviewTheme(null);
    // Revert to current theme
    const root = document.documentElement;
    Object.entries(currentTheme.colors).forEach(([key, value]) => {
      root.style.setProperty(`--ctp-${key.replace(/([A-Z])/g, '-$1').toLowerCase()}`, value);
    });
  };

  return (
    <div className="theme-editor">
      {notification && (
        <div className="theme-notification">
          <FontAwesomeIcon icon={faCheck} />
          {notification}
        </div>
      )}

      <div className="theme-editor-header">
        <h2>
          <FontAwesomeIcon icon={faPalette} />
          Theme Settings
        </h2>
        <p>Customize the appearance of RetroArr</p>
      </div>

      <div className="theme-tabs">
        <button 
          className={`theme-tab ${activeTab === 'presets' ? 'active' : ''}`}
          onClick={() => setActiveTab('presets')}
        >
          Preset Themes
        </button>
        <button 
          className={`theme-tab ${activeTab === 'customize' ? 'active' : ''}`}
          onClick={() => setActiveTab('customize')}
        >
          Customize Colors
        </button>
      </div>

      {activeTab === 'presets' && (
        <div className="theme-presets">
          <div className="theme-grid">
            {THEMES.map(theme => (
              <div 
                key={theme.id}
                className={`theme-card ${currentTheme.id === theme.id ? 'active' : ''}`}
                onMouseEnter={() => handlePreview(theme)}
                onMouseLeave={handlePreviewEnd}
              >
                <div className="theme-preview" style={{ backgroundColor: theme.colors.base }}>
                  <div className="theme-preview-header" style={{ backgroundColor: theme.colors.mantle }}>
                    <div className="preview-dot" style={{ backgroundColor: theme.colors.red }}></div>
                    <div className="preview-dot" style={{ backgroundColor: theme.colors.yellow }}></div>
                    <div className="preview-dot" style={{ backgroundColor: theme.colors.green }}></div>
                  </div>
                  <div className="theme-preview-content">
                    <div className="preview-sidebar" style={{ backgroundColor: theme.colors.surface0 }}>
                      <div className="preview-nav-item" style={{ backgroundColor: theme.colors.primary }}></div>
                      <div className="preview-nav-item" style={{ backgroundColor: theme.colors.surface1 }}></div>
                      <div className="preview-nav-item" style={{ backgroundColor: theme.colors.surface1 }}></div>
                    </div>
                    <div className="preview-main">
                      <div className="preview-card" style={{ backgroundColor: theme.colors.surface0 }}>
                        <div className="preview-text" style={{ backgroundColor: theme.colors.text }}></div>
                        <div className="preview-text short" style={{ backgroundColor: theme.colors.subtext0 }}></div>
                      </div>
                    </div>
                  </div>
                </div>
                <div className="theme-info">
                  <h3>{theme.name}</h3>
                  <p>{theme.description}</p>
                  {theme.author && <span className="theme-author">by {theme.author}</span>}
                </div>
                <div className="theme-actions">
                  <button 
                    className="btn-apply"
                    onClick={() => handleThemeSelect(theme.id)}
                    disabled={currentTheme.id === theme.id}
                  >
                    {currentTheme.id === theme.id ? (
                      <><FontAwesomeIcon icon={faCheck} /> Active</>
                    ) : (
                      'Apply'
                    )}
                  </button>
                  <button 
                    className="btn-customize"
                    onClick={() => handleStartCustomize(theme)}
                    title="Use as base for customization"
                  >
                    <FontAwesomeIcon icon={faPalette} />
                  </button>
                </div>
                <div className="theme-colors-preview">
                  {['primary', 'blue', 'green', 'yellow', 'red', 'mauve'].map(color => (
                    <div 
                      key={color}
                      className="color-dot"
                      style={{ backgroundColor: theme.colors[color as keyof ThemeColors] }}
                      title={color}
                    ></div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {activeTab === 'customize' && (
        <div className="theme-customize">
          <div className="customize-actions">
            <button className="btn-action" onClick={handleApplyCustomTheme}>
              <FontAwesomeIcon icon={faSave} /> Apply Custom Theme
            </button>
            <button className="btn-action secondary" onClick={handleExportTheme}>
              <FontAwesomeIcon icon={faCopy} /> Export Theme
            </button>
            <button className="btn-action danger" onClick={handleResetCustomTheme}>
              <FontAwesomeIcon icon={faUndo} /> Reset
            </button>
          </div>

          {!customTheme ? (
            <div className="customize-empty">
              <FontAwesomeIcon icon={faPalette} size="3x" />
              <h3>No Custom Theme</h3>
              <p>Select a preset theme and click the palette icon to start customizing.</p>
            </div>
          ) : (
            <div className="color-groups">
              {COLOR_GROUPS.map(group => (
                <div key={group.name} className="color-group">
                  <h3>{group.name}</h3>
                  <div className="color-inputs">
                    {group.colors.map(colorKey => (
                      <div key={colorKey} className="color-input-row">
                        <label>{COLOR_LABELS[colorKey]}</label>
                        <div className="color-input-wrapper">
                          <input
                            type="color"
                            value={customTheme.colors[colorKey]}
                            onChange={(e) => handleColorChange(colorKey, e.target.value)}
                          />
                          <input
                            type="text"
                            value={customTheme.colors[colorKey]}
                            onChange={(e) => handleColorChange(colorKey, e.target.value)}
                            placeholder="#000000"
                          />
                          <div 
                            className="color-preview-box"
                            style={{ backgroundColor: customTheme.colors[colorKey] }}
                          ></div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default ThemeEditor;
