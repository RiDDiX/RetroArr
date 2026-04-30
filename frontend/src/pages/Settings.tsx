import React, { useState, useEffect } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { t as translate, Language, getLanguage as getSavedLanguage, setLanguage as setGlobalLanguage } from '../i18n/translations';
import './Settings.css';
import languageIcon from '../assets/language_icon.png';
import DebugConsole from '../components/DebugConsole';
import EmulatorJsSettings from '../components/EmulatorJsSettings';
import ThemeEditor from '../components/ThemeEditor';
import DatabaseSettings from '../components/DatabaseSettings';
import CacheSettings from '../components/CacheSettings';
import { MediaTab, PlatformsTab, MetadataProvidersTab, AccountsTab, IndexersTab, DownloadersTab, ImportExportTab, LoggingTab, WebhooksTab, ApiAccessTab } from '../components/settings';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import {
  faFolderOpen, faLayerGroup, faTags, faUserCircle, faSearch, faDownload, faGamepad,
  faPalette, faGlobe, faFileImport, faTerminal, faDatabase,
  faMemory, faClipboardList, faBell, faKey
} from '@fortawesome/free-solid-svg-icons';
import type { IconDefinition } from '@fortawesome/fontawesome-svg-core';

interface TabDef {
  id: string;
  label: string;
  icon: IconDefinition;
  group: string;
}

const Settings: React.FC = () => {
  const location = useLocation();
  const navigate = useNavigate();
  const rawHash = location.hash.replace('#', '') || 'media';
  // legacy hash aliases so existing bookmarks keep working
  const HASH_ALIASES: Record<string, string> = {
    connections: 'accounts',
    'download-clients': 'downloaders',
  };
  const currentTab = HASH_ALIASES[rawHash] || rawHash;
  const [language, setLanguage] = useState<Language>(getSavedLanguage());

  const t = (key: string) => translate(key as Parameters<typeof translate>[0], language);

  useEffect(() => {
    const savedLang = localStorage.getItem('RetroArr_language');
    if (savedLang) setLanguage(savedLang as Language);
  }, []);

  const handleSaveLanguage = () => {
    setGlobalLanguage(language);
    alert(t('languageSaved'));
  };

  const tabs: TabDef[] = [
    { id: 'media',        label: t('settingsMedia') || 'Media',                       icon: faFolderOpen,    group: 'library' },
    { id: 'platforms',    label: t('platforms') || 'Platforms',                        icon: faLayerGroup,    group: 'library' },
    { id: 'metadata',     label: t('settingsMetadataProviders') || 'Metadata Providers', icon: faTags,         group: 'connections' },
    { id: 'accounts',     label: t('settingsAccounts') || 'Accounts',                   icon: faUserCircle,    group: 'connections' },
    { id: 'indexers',     label: t('settingsIndexers') || 'Indexers',                   icon: faSearch,        group: 'downloads' },
    { id: 'downloaders',  label: t('settingsDownloaders') || 'Downloaders',             icon: faDownload,      group: 'downloads' },
    { id: 'emulatorjs',   label: 'EmulatorJS',                                          icon: faGamepad,       group: 'emulator' },
    { id: 'themes',       label: t('settingsThemes') || 'Themes',                       icon: faPalette,       group: 'interface' },
    { id: 'language',     label: t('settingsLanguage') || 'Language',                   icon: faGlobe,         group: 'interface' },
    { id: 'importexport', label: t('settingsImportExport') || 'Import/Export',          icon: faFileImport,    group: 'system' },
    { id: 'database',     label: t('settingsDatabase') || 'Database',                   icon: faDatabase,      group: 'system' },
    { id: 'cache',        label: t('settingsCache') || 'Cache',                         icon: faMemory,        group: 'system' },
    { id: 'logging',      label: t('logging') || 'Logging',                             icon: faClipboardList, group: 'system' },
    { id: 'webhooks',     label: t('webhooks') || 'Webhooks',                           icon: faBell,          group: 'system' },
    { id: 'apiaccess',    label: t('apiAccess') || 'API access',                        icon: faKey,           group: 'system' },
    { id: 'debug',        label: 'Debug',                                               icon: faTerminal,      group: 'system' },
  ];

  const groups = [
    { key: 'library',     label: t('library') || 'Library' },
    { key: 'connections', label: t('settingsConnections') || 'Connections' },
    { key: 'downloads',   label: t('downloads') || 'Downloads' },
    { key: 'emulator',    label: 'Emulator' },
    { key: 'interface',   label: 'Interface' },
    { key: 'system',      label: 'System' },
  ];

  const switchTab = (id: string) => {
    navigate(`/settings#${id}`, { replace: true });
  };

  return (
    <div className="settings-layout">
      <nav className="settings-sidebar">
        {groups.map(group => {
          const groupTabs = tabs.filter(tab => tab.group === group.key);
          if (groupTabs.length === 0) return null;
          return (
            <div key={group.key} className="sidebar-group">
              <div className="sidebar-group-label">{group.label}</div>
              {groupTabs.map(tab => (
                <button
                  key={tab.id}
                  className={`sidebar-tab ${currentTab === tab.id ? 'active' : ''}`}
                  onClick={() => switchTab(tab.id)}
                >
                  <FontAwesomeIcon icon={tab.icon} className="sidebar-tab-icon" />
                  <span>{tab.label}</span>
                </button>
              ))}
            </div>
          );
        })}
      </nav>

      <div className="settings-content">
        {currentTab === 'media' && (
          <MediaTab language={language} t={t} />
        )}

        {currentTab === 'platforms' && (
          <PlatformsTab language={language} t={t} />
        )}

        {currentTab === 'metadata' && (
          <MetadataProvidersTab language={language} t={t} />
        )}

        {currentTab === 'accounts' && (
          <AccountsTab language={language} t={t} />
        )}

        {currentTab === 'language' && (
          <div className="settings-section" id="language">
            <div className="section-header-with-logo">
              <img src={languageIcon} alt="Language" className="language-icon" />
            </div>
            <p className="settings-description">
              {t('languageDesc')}
            </p>
            <div className="form-group">
              <label htmlFor="language-select">{t('languageTitle')}</label>
              <select
                id="language-select"
                value={language}
                onChange={(e) => setLanguage(e.target.value as Language)}
              >
                <option value="es">Español</option>
                <option value="en">English</option>
                <option value="fr">Français</option>
                <option value="de">Deutsch</option>
                <option value="ru">Русский</option>
                <option value="zh">中文</option>
                <option value="ja">日本語</option>
              </select>
            </div>
            <button type="button" className="btn-primary" onClick={handleSaveLanguage}>{t('saveLanguage')}</button>
          </div>
        )}

        {currentTab === 'importexport' && (
          <ImportExportTab language={language} t={t} />
        )}

        {currentTab === 'indexers' && (
          <IndexersTab language={language} t={t} />
        )}

        {currentTab === 'downloaders' && (
          <DownloadersTab language={language} t={t} />
        )}

        {currentTab === 'debug' && (
          <DebugConsole language={language} />
        )}

        {currentTab === 'emulatorjs' && (
          <EmulatorJsSettings language={language} />
        )}

        {currentTab === 'themes' && (
          <ThemeEditor />
        )}

        {currentTab === 'database' && (
          <DatabaseSettings language={language} />
        )}

        {currentTab === 'logging' && (
          <LoggingTab language={language} t={t} />
        )}

        {currentTab === 'cache' && (
          <CacheSettings />
        )}

        {currentTab === 'webhooks' && (
          <WebhooksTab language={language} t={t} />
        )}

        {currentTab === 'apiaccess' && (
          <ApiAccessTab language={language} t={t} />
        )}
      </div>
    </div>
  );
};

export default Settings;
