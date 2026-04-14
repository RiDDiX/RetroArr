import React from 'react';
import './EmptyState.css';

export type EmptySprite = 'cartridge' | 'unplugged' | 'gameover' | 'disc' | 'memcard';

type Action = { label: string; onClick?: () => void; href?: string };

type Props = {
  sprite?: EmptySprite;
  title: string;
  body?: string;
  primary?: Action;
  secondary?: Action;
};

function ActionBtn({ action, tier }: { action: Action; tier: 'primary' | 'secondary' }) {
  const cls = `retro-btn retro-btn--${tier}`;
  if (action.href) return <a className={cls} href={action.href}>{action.label}</a>;
  return <button className={cls} onClick={action.onClick}>{action.label}</button>;
}

export function EmptyState({ sprite = 'cartridge', title, body, primary, secondary }: Props) {
  return (
    <div className="empty-state">
      <div className={`empty-state__sprite empty-state__sprite--${sprite}`} aria-hidden="true" />
      <h3 className="empty-state__title">{title}</h3>
      {body && <p className="empty-state__body">{body}</p>}
      {(primary || secondary) && (
        <div className="empty-state__actions">
          {primary   && <ActionBtn action={primary}   tier="primary"   />}
          {secondary && <ActionBtn action={secondary} tier="secondary" />}
        </div>
      )}
    </div>
  );
}
