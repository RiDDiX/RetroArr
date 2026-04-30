import { useEffect, useRef, useState } from 'react';
import { Link, NavLink } from 'react-router-dom';
import { useTranslation } from '../../i18n/translations';
import { useUI } from '../../context/UIContext';
import apiClient from '../../api/client';
import { progressHub, type ConnState } from '../../api/progressHub';
import appLogo from '../../assets/app_logo.png';
import './Sidebar.css';

type Group = {
  id: string;
  label: string;
  items: { to: string; label: string; hash?: string }[];
};

export function Sidebar() {
  const { t } = useTranslation();
  const { toggleKofi } = useUI();
  const [activeBadge, setActiveBadge] = useState(0);
  const [connState, setConnState] = useState<ConnState>(progressHub.getConnectionState());
  const prevActive = useRef<number | null>(null);

  useEffect(() => progressHub.onConnectionChange(setConnState), []);

  useEffect(() => {
    const pull = () => {
      apiClient
        .get<{ active?: number; failed?: number; unmapped?: number }>('/downloadclient/counts')
        .then(({ data: d }) => {
          const active = d.active || 0;
          const total  = active + (d.failed || 0) + (d.unmapped || 0);
          setActiveBadge(total);
          if (prevActive.current !== null && active < prevActive.current) {
            window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
          }
          prevActive.current = active;
        })
        .catch(() => {});
    };
    pull();
    const iv = setInterval(pull, 15000);
    return () => clearInterval(iv);
  }, []);

  const groups: Group[] = [
    {
      id: 'library',
      label: t('library') || 'Library',
      items: [
        { to: '/dashboard',   label: t('dashboard')   || 'Dashboard' },
        { to: '/library',     label: t('library')     || 'Library'   },
        { to: '/platforms',   label: t('platforms')   || 'Platforms' },
        { to: '/collections', label: t('collections') || 'Collections' },
      ],
    },
    {
      id: 'downloads',
      label: t('downloads') || 'Downloads',
      items: [
        { to: '/status',          label: t('downloadsQueue')   || 'Queue' },
        { to: '/status#history',  label: t('downloadsHistory') || 'History' },
        { to: '/status#failed',   label: t('downloadsFailed')  || 'Failed' },
        { to: '/status#blacklist',label: t('downloadsBlacklist') || 'Blacklist' },
      ],
    },
    {
      id: 'tools',
      label: t('tools') || 'Tools',
      items: [
        { to: '/statistics',      label: t('statistics') || 'Statistics' },
        { to: '/problems',        label: 'Problems' },
        { to: '/library-resort',  label: 'Resort' },
        { to: '/metadata-review', label: 'Review' },
        { to: '/review-import',   label: 'Review Import' },
        { to: '/trash',           label: t('trashTitle') || 'Trash' },
      ],
    },
    {
      id: 'settings',
      label: t('settings') || 'Settings',
      items: [
        { to: '/settings',         label: 'All settings' },
        { to: '/settings#media',   label: t('settingsMedia') || 'Media' },
        { to: '/settings#connections', label: t('settingsConnections') || 'Connections' },
        { to: '/settings#indexers',label: t('settingsIndexers') || 'Indexers' },
        { to: '/settings#webhooks',label: 'Webhooks' },
        { to: '/settings#apiaccess', label: 'API access' },
        { to: '/user',             label: t('user') || 'User' },
        { to: '/about',            label: t('about') || 'About' },
      ],
    },
  ];

  return (
    <aside className="sidebar" aria-label="Main navigation">
      <a href="#main-content" className="skip-to-content">Skip to content</a>

      <div className="sidebar__brand">
        <button
          className="sidebar__logo-btn"
          onClick={toggleKofi}
          type="button"
          aria-label="RetroArr - show info"
        >
          <img src={appLogo} alt="" className="sidebar__logo-eye" />
          <span className="sidebar__wordmark">RetroArr</span>
        </button>
        <span className="sidebar__beta pixel">BETA</span>
      </div>

      <div className="sidebar__conn" role="status" aria-live="polite">
        <span
          className="retro-led"
          data-state={
            connState === 'connected'    ? undefined
          : connState === 'reconnecting' ? 'warn'
          :                                'error'
          }
        />
        <span className="sidebar__conn-label pixel">
          {connState === 'connected'    ? (t('live')        || 'LIVE')
         : connState === 'reconnecting' ? (t('reconnecting')|| 'RECONNECTING')
         :                                (t('offline')     || 'OFFLINE')}
        </span>
      </div>

      <nav className="sidebar__nav" role="menu">
        {groups.map(group => (
          <div key={group.id} className="sidebar__group" role="none">
            <div className="sidebar__group-label">{group.label}</div>
            <ul className="sidebar__list" role="none">
              {group.items.map(item => (
                <li key={item.to + (item.hash || '')} role="none">
                  {item.to === '/status' && !item.hash ? (
                    <NavLink
                      to={item.to}
                      role="menuitem"
                      className={({ isActive }) =>
                        `sidebar__link ${isActive ? 'is-active' : ''}`
                      }
                      end
                    >
                      <span>{item.label}</span>
                      {activeBadge > 0 && (
                        <span className="sidebar__badge pixel">{activeBadge}</span>
                      )}
                    </NavLink>
                  ) : (
                    <Link to={item.to} role="menuitem" className="sidebar__link">
                      {item.label}
                    </Link>
                  )}
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>
    </aside>
  );
}
