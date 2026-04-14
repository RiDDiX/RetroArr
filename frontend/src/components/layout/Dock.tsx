import { NavLink } from 'react-router-dom';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import {
  faGamepad, faDownload, faGears, faHouse, faLayerGroup,
} from '@fortawesome/free-solid-svg-icons';
import type { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import { useTranslation } from '../../i18n/translations';
import './Dock.css';

type DockItem = { to: string; label: string; icon: IconDefinition };

export function Dock() {
  const { t } = useTranslation();

  const items: DockItem[] = [
    { to: '/dashboard', label: t('dashboard') || 'Home',     icon: faHouse },
    { to: '/library',   label: t('library')   || 'Library',  icon: faGamepad },
    { to: '/platforms', label: t('platforms') || 'Platforms',icon: faLayerGroup },
    { to: '/status',    label: t('downloads') || 'Queue',    icon: faDownload },
    { to: '/settings',  label: t('settings')  || 'Settings', icon: faGears },
  ];

  return (
    <nav className="dock" aria-label="Primary">
      <ul className="dock__list">
        {items.map((item, idx) => (
          <li key={idx} className="dock__item">
            <NavLink
              to={item.to}
              className={({ isActive }) =>
                `dock__link ${isActive ? 'is-active' : ''}`
              }
              end
            >
              <FontAwesomeIcon icon={item.icon} className="dock__icon" />
              <span className="dock__label">{item.label}</span>
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
