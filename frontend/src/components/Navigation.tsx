import { Link, NavLink } from 'react-router-dom';
import { useTranslation } from '../i18n/translations';
import { useState, useRef, useEffect, useCallback } from 'react';
import './Navigation.css';
import appLogo from '../assets/app_logo.png';

import { useUI } from '../context/UIContext';

const Navigation: React.FC = () => {
  const { t } = useTranslation();
  const { toggleKofi } = useUI();
  const [showSettingsDropdown, setShowSettingsDropdown] = useState(false);
  const [showDownloadsDropdown, setShowDownloadsDropdown] = useState(false);
  const [showToolsDropdown, setShowToolsDropdown] = useState(false);
  const dropdownRef = useRef<HTMLLIElement>(null);
  const downloadsDropdownRef = useRef<HTMLLIElement>(null);
  const toolsDropdownRef = useRef<HTMLLIElement>(null);
  const [statusBadge, setStatusBadge] = useState(0);
  const prevActiveRef = useRef<number | null>(null);

  useEffect(() => {
    const fetchCounts = () => {
      fetch('/api/v3/downloadclient/counts')
        .then(r => r.json())
        .then(data => {
          const active = data.active || 0;
          const total = active + (data.failed || 0) + (data.unmapped || 0);
          setStatusBadge(total);

          // Detect download completion: active count dropped → a download finished
          if (prevActiveRef.current !== null && active < prevActiveRef.current) {
            console.log('[Nav] Download completed detected. Triggering library refresh...');
            window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
          }
          prevActiveRef.current = active;
        })
        .catch(() => {});
    };
    fetchCounts();
    const iv = setInterval(fetchCounts, 15000);
    return () => clearInterval(iv);
  }, []);

  const handleDropdownItemClick = () => {
    setShowSettingsDropdown(false);
  };

  const handleDownloadsDropdownItemClick = () => {
    setShowDownloadsDropdown(false);
  };

  const handleToolsDropdownItemClick = () => {
    setShowToolsDropdown(false);
  };

  const handleDropdownKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setShowSettingsDropdown(false);
    }
  }, []);

  const handleDownloadsDropdownKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setShowDownloadsDropdown(false);
    }
  }, []);

  const handleToolsDropdownKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setShowToolsDropdown(false);
    }
  }, []);

  // Close dropdown on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setShowSettingsDropdown(false);
      }
      if (downloadsDropdownRef.current && !downloadsDropdownRef.current.contains(e.target as Node)) {
        setShowDownloadsDropdown(false);
      }
      if (toolsDropdownRef.current && !toolsDropdownRef.current.contains(e.target as Node)) {
        setShowToolsDropdown(false);
      }
    };
    if (showSettingsDropdown || showDownloadsDropdown || showToolsDropdown) {
      document.addEventListener('mousedown', handleClickOutside);
    }
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [showSettingsDropdown, showDownloadsDropdown, showToolsDropdown]);

  return (
    <>
      <a href="#main-content" className="skip-to-content">
        Skip to content
      </a>
      <nav className="navigation" aria-label="Main navigation">
        <div className="nav-brand">
          <button
            className="nav-logo-link"
            onClick={toggleKofi}
            type="button"
            aria-label="RetroArr — Show info"
          >
            <img src={appLogo} alt="" className="nav-logo-eye" />
            <span className="nav-logo-text">RetroArr</span>
          </button>
        </div>
        <ul className="nav-links" role="menubar">
          <li role="none"><NavLink to="/dashboard" role="menuitem">{t('dashboard') || 'Dashboard'}</NavLink></li>
          <li role="none"><NavLink to="/library" role="menuitem">{t('library')}</NavLink></li>
          <li
            ref={downloadsDropdownRef}
            className="nav-item-dropdown"
            role="none"
            onMouseEnter={() => setShowDownloadsDropdown(true)}
            onMouseLeave={() => setShowDownloadsDropdown(false)}
            onKeyDown={handleDownloadsDropdownKeyDown}
          >
            <NavLink
              to="/status"
              role="menuitem"
              aria-haspopup="true"
              aria-expanded={showDownloadsDropdown}
              className="nav-link-with-arrow nav-status-link"
              onFocus={() => setShowDownloadsDropdown(true)}
            >
              {t('downloads') || 'Downloads'}
              {statusBadge > 0 && <span className="nav-badge">{statusBadge}</span>}
              <span className={`dropdown-arrow ${showDownloadsDropdown ? 'open' : ''}`} aria-hidden="true">▾</span>
            </NavLink>
            <div className={`dropdown-menu ${showDownloadsDropdown ? 'show' : ''}`} role="menu" aria-label="Downloads sections">
              <Link to="/status#activity" role="menuitem" onClick={handleDownloadsDropdownItemClick}>{t('downloadsQueue') || 'Queue'}</Link>
              <Link to="/status#history" role="menuitem" onClick={handleDownloadsDropdownItemClick}>{t('downloadsHistory') || 'History'}</Link>
              <Link to="/status#failed" role="menuitem" onClick={handleDownloadsDropdownItemClick}>{t('downloadsFailed') || 'Failed'}</Link>
              <Link to="/status#unmapped" role="menuitem" onClick={handleDownloadsDropdownItemClick}>{t('downloadsUnmapped') || 'Unmapped'}</Link>
              <Link to="/status#unmapped-files" role="menuitem" onClick={handleDownloadsDropdownItemClick}>{t('notMappedFiles') || 'Not Mapped Files'}</Link>
              <Link to="/status#blacklist" role="menuitem" onClick={handleDownloadsDropdownItemClick}>{t('downloadsBlacklist') || 'Blacklist'}</Link>
            </div>
          </li>
          <li
            ref={toolsDropdownRef}
            className="nav-item-dropdown"
            role="none"
            onMouseEnter={() => setShowToolsDropdown(true)}
            onMouseLeave={() => setShowToolsDropdown(false)}
            onKeyDown={handleToolsDropdownKeyDown}
          >
            <button
              role="menuitem"
              aria-haspopup="true"
              aria-expanded={showToolsDropdown}
              className="nav-link-with-arrow nav-dropdown-btn"
              onFocus={() => setShowToolsDropdown(true)}
            >
              {t('tools') || 'Tools'}
              <span className={`dropdown-arrow ${showToolsDropdown ? 'open' : ''}`} aria-hidden="true">▾</span>
            </button>
            <div className={`dropdown-menu ${showToolsDropdown ? 'show' : ''}`} role="menu" aria-label="Tools">
              <Link to="/statistics" role="menuitem" onClick={handleToolsDropdownItemClick}>{t('statistics') || 'Statistics'}</Link>
              <Link to="/problems" role="menuitem" onClick={handleToolsDropdownItemClick}>Problems</Link>
              <Link to="/library-resort" role="menuitem" onClick={handleToolsDropdownItemClick}>Resort</Link>
              <Link to="/metadata-review" role="menuitem" onClick={handleToolsDropdownItemClick}>Review</Link>
              <Link to="/review-import" role="menuitem" onClick={handleToolsDropdownItemClick}>Review Import</Link>
              <Link to="/collections" role="menuitem" onClick={handleToolsDropdownItemClick}>{t('collections') || 'Collections'}</Link>
            </div>
          </li>
          <li
            ref={dropdownRef}
            className="nav-item-dropdown"
            role="none"
            onMouseEnter={() => setShowSettingsDropdown(true)}
            onMouseLeave={() => setShowSettingsDropdown(false)}
            onKeyDown={handleDropdownKeyDown}
          >
            <NavLink
              to="/settings"
              role="menuitem"
              aria-haspopup="true"
              aria-expanded={showSettingsDropdown}
              className="nav-link-with-arrow"
              onFocus={() => setShowSettingsDropdown(true)}
            >
              {t('settings')} <span className={`dropdown-arrow ${showSettingsDropdown ? 'open' : ''}`} aria-hidden="true">▾</span>
            </NavLink>
            <div className={`dropdown-menu ${showSettingsDropdown ? 'show' : ''}`} role="menu" aria-label="Settings sections">
              <Link to="/settings#media" role="menuitem" onClick={handleDropdownItemClick}>{t('settingsMedia')}</Link>
              <Link to="/settings#platforms" role="menuitem" onClick={handleDropdownItemClick}>{t('settingsPlatforms') || 'Platforms'}</Link>
              <Link to="/settings#connections" role="menuitem" onClick={handleDropdownItemClick}>{t('settingsConnections')}</Link>
              <Link to="/settings#indexers" role="menuitem" onClick={handleDropdownItemClick}>{t('settingsIndexers')}</Link>
              <Link to="/settings#language" role="menuitem" onClick={handleDropdownItemClick}>{t('settingsLanguage')}</Link>
              <Link to="/settings#importexport" role="menuitem" onClick={handleDropdownItemClick}>{t('importExport') || 'Import/Export'}</Link>
              <Link to="/settings#emulatorjs" role="menuitem" onClick={handleDropdownItemClick}>EmulatorJS</Link>
              <Link to="/settings#themes" role="menuitem" onClick={handleDropdownItemClick}>Themes</Link>
              <Link to="/settings#logging" role="menuitem" onClick={handleDropdownItemClick}>{t('logging') || 'Logging'}</Link>
              <Link to="/settings#database" role="menuitem" onClick={handleDropdownItemClick}>{t('databaseSettings') || 'Database'}</Link>
              <Link to="/settings#cache" role="menuitem" onClick={handleDropdownItemClick}>Cache</Link>
              <Link to="/settings#debug" role="menuitem" onClick={handleDropdownItemClick}>{t('debug') || 'Debug'}</Link>
              <div className="dropdown-divider"></div>
              <Link to="/user" role="menuitem" onClick={handleDropdownItemClick}>{t('user')}</Link>
              <Link to="/about" role="menuitem" onClick={handleDropdownItemClick}>{t('about')}</Link>
            </div>
          </li>
        </ul>
        <div className="nav-branch-tag" aria-label="Beta release">
          BETA Release
        </div>
      </nav>
    </>
  );
};

export default Navigation;
