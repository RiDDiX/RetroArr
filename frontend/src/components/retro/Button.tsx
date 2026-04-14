import React from 'react';
import './Button.css';

export type ButtonTier = 'primary' | 'secondary' | 'ghost' | 'danger';

type Props = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  tier?: ButtonTier;
  loading?: boolean;
  icon?: React.ReactNode;
};

export function Button({
  tier = 'primary',
  loading,
  icon,
  children,
  className = '',
  disabled,
  ...rest
}: Props) {
  return (
    <button
      {...rest}
      disabled={disabled || loading}
      className={`retro-btn retro-btn--${tier} ${loading ? 'is-loading' : ''} ${className}`.trim()}
    >
      {icon && <span className="retro-btn__icon">{icon}</span>}
      <span className="retro-btn__label">{children}</span>
    </button>
  );
}
