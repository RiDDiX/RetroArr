import React from 'react';

interface ProtonDbBadgeProps {
  tier: string | null | undefined;
  size?: 'small' | 'medium' | 'large';
  showLabel?: boolean;
}

const tierConfig: Record<string, { color: string; bg: string; label: string }> = {
  platinum: { color: '#b4c7dc', bg: '#1a1a2e', label: 'Platinum' },
  gold:     { color: '#1a1a1a', bg: '#cfb53b', label: 'Gold' },
  silver:   { color: '#1a1a1a', bg: '#a8a8a8', label: 'Silver' },
  bronze:   { color: '#1a1a1a', bg: '#cd7f32', label: 'Bronze' },
  borked:   { color: '#ffffff', bg: '#d32f2f', label: 'Borked' },
  native:   { color: '#ffffff', bg: '#4caf50', label: 'Native' },
  pending:  { color: '#aaaaaa', bg: '#444444', label: 'Pending' },
};

const ProtonDbBadge: React.FC<ProtonDbBadgeProps> = ({ tier, size = 'small', showLabel = false }) => {
  if (!tier) return null;

  const config = tierConfig[tier.toLowerCase()] || tierConfig.pending;
  
  const fontSize = size === 'large' ? '13px' : size === 'medium' ? '11px' : '9px';
  const padding = size === 'large' ? '3px 8px' : size === 'medium' ? '2px 6px' : '1px 5px';

  return (
    <span
      title={`ProtonDB: ${config.label}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '3px',
        backgroundColor: config.bg,
        color: config.color,
        fontSize,
        fontWeight: 700,
        padding,
        borderRadius: '3px',
        whiteSpace: 'nowrap',
        letterSpacing: '0.3px',
        lineHeight: 1.2,
        border: tier.toLowerCase() === 'platinum' ? '1px solid #4a6a8a' : 'none',
      }}
    >
      {showLabel ? config.label : config.label.charAt(0)}
    </span>
  );
};

export default ProtonDbBadge;
