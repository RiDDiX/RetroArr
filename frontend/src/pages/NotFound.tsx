import { useNavigate } from 'react-router-dom';
import { EmptyState } from '../components/retro';
import { t } from '../i18n/translations';
import './NotFound.css';

export default function NotFound() {
  const navigate = useNavigate();

  return (
    <div className="page not-found">
      <span className="not-found__marquee pixel" aria-hidden="true">GAME&nbsp;OVER</span>
      <EmptyState
        sprite="gameover"
        title={t('notFoundTitle') || 'Game Over'}
        body={t('notFoundBody') || 'That route is not in the cartridge. Head back to the library and keep playing.'}
        primary={{
          label: t('continue') || 'Continue',
          onClick: () => navigate('/library'),
        }}
        secondary={{
          label: t('dashboard') || 'Dashboard',
          onClick: () => navigate('/'),
        }}
      />
    </div>
  );
}
