import React from 'react';

interface RegionFlagProps {
  region: string | null | undefined;
  languages?: string | null | undefined;
  size?: 'small' | 'medium';
  showLabel?: boolean;
}

// Map region names to ISO 3166-1 alpha-2 codes
const regionToCode: Record<string, string> = {
  'USA': 'US',
  'Europe': 'EU',
  'Japan': 'JP',
  'Germany': 'DE',
  'France': 'FR',
  'Spain': 'ES',
  'Italy': 'IT',
  'UK': 'GB',
  'Brazil': 'BR',
  'Australia': 'AU',
  'Korea': 'KR',
  'China': 'CN',
  'Taiwan': 'TW',
  'Russia': 'RU',
  'Netherlands': 'NL',
  'Sweden': 'SE',
  'Norway': 'NO',
  'Denmark': 'DK',
  'Finland': 'FI',
  'Poland': 'PL',
  'Portugal': 'PT',
  'Czech Republic': 'CZ',
  'Greece': 'GR',
  'Canada': 'CA',
  'Hong Kong': 'HK',
  'World': 'UN',
  'Asia': 'UN',
};

// Map language codes to country flag codes
const langToFlag: Record<string, string> = {
  'En': 'GB', 'Fr': 'FR', 'De': 'DE', 'Es': 'ES', 'It': 'IT',
  'Nl': 'NL', 'Sv': 'SE', 'No': 'NO', 'Da': 'DK', 'Fi': 'FI',
  'Pt': 'PT', 'Ja': 'JP', 'Zh': 'CN', 'Ko': 'KR', 'Ru': 'RU',
  'Pl': 'PL', 'Cs': 'CZ', 'Hu': 'HU', 'Ca': 'ES', 'El': 'GR',
  'Tr': 'TR', 'Ge': 'DE', 'Ro': 'RO', 'Hr': 'HR', 'Sk': 'SK',
  'Bg': 'BG', 'Uk': 'UA', 'Lt': 'LT', 'Lv': 'LV', 'Et': 'EE',
  'Sl': 'SI',
};

// Language code to display name
const langToName: Record<string, string> = {
  'En': 'English', 'Fr': 'French', 'De': 'German', 'Es': 'Spanish',
  'It': 'Italian', 'Nl': 'Dutch', 'Sv': 'Swedish', 'No': 'Norwegian',
  'Da': 'Danish', 'Fi': 'Finnish', 'Pt': 'Portuguese', 'Ja': 'Japanese',
  'Zh': 'Chinese', 'Ko': 'Korean', 'Ru': 'Russian', 'Pl': 'Polish',
  'Cs': 'Czech', 'Hu': 'Hungarian', 'Ca': 'Catalan', 'El': 'Greek',
  'Tr': 'Turkish', 'Ge': 'German', 'Ro': 'Romanian', 'Hr': 'Croatian',
  'Sk': 'Slovak', 'Bg': 'Bulgarian', 'Uk': 'Ukrainian', 'Lt': 'Lithuanian',
  'Lv': 'Latvian', 'Et': 'Estonian', 'Sl': 'Slovenian',
};

// Inline SVG flags
const flagSvgs: Record<string, React.ReactNode> = {
  'US': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#B22234"/>
      <rect y="1.23" width="24" height="1.23" fill="#fff"/>
      <rect y="3.69" width="24" height="1.23" fill="#fff"/>
      <rect y="6.15" width="24" height="1.23" fill="#fff"/>
      <rect y="8.62" width="24" height="1.23" fill="#fff"/>
      <rect y="11.08" width="24" height="1.23" fill="#fff"/>
      <rect y="13.54" width="24" height="1.23" fill="#fff"/>
      <rect width="10" height="8.62" fill="#3C3B6E"/>
    </svg>
  ),
  'EU': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#003399"/>
      {[...Array(12)].map((_, i) => {
        const angle = (i * 30 - 90) * Math.PI / 180;
        const cx = 12 + 4.5 * Math.cos(angle);
        const cy = 8 + 4.5 * Math.sin(angle);
        return <polygon key={i} points={starPoints(cx, cy, 0.8)} fill="#FFCC00"/>;
      })}
    </svg>
  ),
  'JP': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#fff"/>
      <circle cx="12" cy="8" r="4.8" fill="#BC002D"/>
    </svg>
  ),
  'DE': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="5.33" fill="#000"/>
      <rect y="5.33" width="24" height="5.33" fill="#DD0000"/>
      <rect y="10.67" width="24" height="5.33" fill="#FFCC00"/>
    </svg>
  ),
  'FR': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="8" height="16" fill="#002395"/>
      <rect x="8" width="8" height="16" fill="#fff"/>
      <rect x="16" width="8" height="16" fill="#ED2939"/>
    </svg>
  ),
  'ES': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="4" fill="#AA151B"/>
      <rect y="4" width="24" height="8" fill="#F1BF00"/>
      <rect y="12" width="24" height="4" fill="#AA151B"/>
    </svg>
  ),
  'IT': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="8" height="16" fill="#009246"/>
      <rect x="8" width="8" height="16" fill="#fff"/>
      <rect x="16" width="8" height="16" fill="#CE2B37"/>
    </svg>
  ),
  'GB': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#012169"/>
      <path d="M0,0 L24,16 M24,0 L0,16" stroke="#fff" strokeWidth="2.5"/>
      <path d="M0,0 L24,16 M24,0 L0,16" stroke="#C8102E" strokeWidth="1.5"/>
      <rect x="10" width="4" height="16" fill="#fff"/>
      <rect y="6" width="24" height="4" fill="#fff"/>
      <rect x="10.5" width="3" height="16" fill="#C8102E"/>
      <rect y="6.5" width="24" height="3" fill="#C8102E"/>
    </svg>
  ),
  'BR': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#009B3A"/>
      <polygon points="12,2 22,8 12,14 2,8" fill="#FEDF00"/>
      <circle cx="12" cy="8" r="3.2" fill="#002776"/>
    </svg>
  ),
  'AU': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#00008B"/>
      <rect width="10" height="7" fill="#012169"/>
      <rect x="4" width="2" height="7" fill="#fff"/>
      <rect y="2.5" width="10" height="2" fill="#fff"/>
      <rect x="4.25" width="1.5" height="7" fill="#C8102E"/>
      <rect y="2.75" width="10" height="1.5" fill="#C8102E"/>
    </svg>
  ),
  'KR': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#fff"/>
      <circle cx="12" cy="8" r="4" fill="#C60C30"/>
      <path d="M12,4 A4,4 0 0,1 12,12 A2,2 0 0,0 12,8 A2,2 0 0,1 12,4" fill="#003478"/>
    </svg>
  ),
  'CN': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#DE2910"/>
      <polygon points={starPoints(5, 4, 2)} fill="#FFDE00"/>
    </svg>
  ),
  'TW': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#FE0000"/>
      <rect width="10" height="8" fill="#000095"/>
      <circle cx="5" cy="4" r="2.5" fill="#fff"/>
    </svg>
  ),
  'RU': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="5.33" fill="#fff"/>
      <rect y="5.33" width="24" height="5.33" fill="#0039A6"/>
      <rect y="10.67" width="24" height="5.33" fill="#D52B1E"/>
    </svg>
  ),
  'NL': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="5.33" fill="#AE1C28"/>
      <rect y="5.33" width="24" height="5.33" fill="#fff"/>
      <rect y="10.67" width="24" height="5.33" fill="#21468B"/>
    </svg>
  ),
  'SE': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#006AA7"/>
      <rect x="7" width="3" height="16" fill="#FECC00"/>
      <rect y="6.5" width="24" height="3" fill="#FECC00"/>
    </svg>
  ),
  'NO': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#EF2B2D"/>
      <rect x="6" width="5" height="16" fill="#fff"/>
      <rect y="5.5" width="24" height="5" fill="#fff"/>
      <rect x="7" width="3" height="16" fill="#002868"/>
      <rect y="6.5" width="24" height="3" fill="#002868"/>
    </svg>
  ),
  'DK': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#C8102E"/>
      <rect x="6" width="3" height="16" fill="#fff"/>
      <rect y="6.5" width="24" height="3" fill="#fff"/>
    </svg>
  ),
  'FI': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#fff"/>
      <rect x="6" width="3" height="16" fill="#003580"/>
      <rect y="6.5" width="24" height="3" fill="#003580"/>
    </svg>
  ),
  'PL': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="8" fill="#fff"/>
      <rect y="8" width="24" height="8" fill="#DC143C"/>
    </svg>
  ),
  'PT': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="9" height="16" fill="#006600"/>
      <rect x="9" width="15" height="16" fill="#FF0000"/>
      <circle cx="9" cy="8" r="3" fill="#FFCC00"/>
    </svg>
  ),
  'CZ': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="8" fill="#fff"/>
      <rect y="8" width="24" height="8" fill="#D7141A"/>
      <polygon points="0,0 12,8 0,16" fill="#11457E"/>
    </svg>
  ),
  'GR': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#0D5EAF"/>
      <rect y="1.78" width="24" height="1.78" fill="#fff"/>
      <rect y="5.33" width="24" height="1.78" fill="#fff"/>
      <rect y="8.89" width="24" height="1.78" fill="#fff"/>
      <rect y="12.44" width="24" height="1.78" fill="#fff"/>
      <rect width="8.89" height="8.89" fill="#0D5EAF"/>
      <rect x="3.56" width="1.78" height="8.89" fill="#fff"/>
      <rect y="3.56" width="8.89" height="1.78" fill="#fff"/>
    </svg>
  ),
  'CA': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="6" height="16" fill="#FF0000"/>
      <rect x="6" width="12" height="16" fill="#fff"/>
      <rect x="18" width="6" height="16" fill="#FF0000"/>
      <polygon points="12,3 12.8,6 11.2,6" fill="#FF0000"/>
      <polygon points="12,13 12.8,10 11.2,10" fill="#FF0000"/>
      <polygon points="9,8 11,7.2 11,8.8" fill="#FF0000"/>
      <polygon points="15,8 13,7.2 13,8.8" fill="#FF0000"/>
    </svg>
  ),
  'HK': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#DE2910"/>
      <polygon points={starPoints(12, 8, 3)} fill="#fff"/>
    </svg>
  ),
  'UN': (
    <svg viewBox="0 0 24 16" width="100%" height="100%">
      <rect width="24" height="16" fill="#4b92db"/>
      <circle cx="12" cy="8" r="5" fill="none" stroke="#fff" strokeWidth="0.8"/>
    </svg>
  ),
};

function starPoints(cx: number, cy: number, r: number): string {
  const pts: string[] = [];
  for (let i = 0; i < 5; i++) {
    const outerAngle = (i * 72 - 90) * Math.PI / 180;
    const innerAngle = ((i * 72) + 36 - 90) * Math.PI / 180;
    pts.push(`${cx + r * Math.cos(outerAngle)},${cy + r * Math.sin(outerAngle)}`);
    pts.push(`${cx + r * 0.4 * Math.cos(innerAngle)},${cy + r * 0.4 * Math.sin(innerAngle)}`);
  }
  return pts.join(' ');
}

function FallbackBadge({ code, label }: { code: string; label: string }) {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '100%',
        height: '100%',
        backgroundColor: 'var(--ctp-surface1)',
        color: 'var(--ctp-text)',
        fontSize: '8px',
        fontWeight: 700,
        borderRadius: '2px',
        letterSpacing: '0.5px',
      }}
      title={label}
    >
      {code}
    </span>
  );
}

function SingleFlag({ code, label, dimensions }: { code: string; label: string; dimensions: { width: number; height: number } }) {
  const svg = flagSvgs[code] || null;
  return (
    <span
      style={{
        display: 'inline-block',
        width: `${dimensions.width}px`,
        height: `${dimensions.height}px`,
        borderRadius: '2px',
        overflow: 'hidden',
        border: '1px solid rgba(255,255,255,0.15)',
        flexShrink: 0,
        lineHeight: 0,
      }}
      title={label}
    >
      {svg || <FallbackBadge code={code} label={label} />}
    </span>
  );
}

const RegionFlag: React.FC<RegionFlagProps> = ({ region, languages, size = 'small', showLabel = false }) => {
  if (!region && !languages) return null;

  const dimensions = size === 'small' ? { width: 20, height: 14 } : { width: 28, height: 19 };

  // Parse multi-region: "USA, Europe" -> ["USA", "Europe"]
  const regions = region ? region.split(',').map(r => r.trim()).filter(Boolean) : [];
  const regionCodes = regions.map(r => regionToCode[r] || null).filter(Boolean) as string[];

  // Parse language codes: "En, Fr, De, Es, It" -> ["En", "Fr", ...]
  const langCodes = languages ? languages.split(',').map(l => l.trim()).filter(Boolean) : [];

  return (
    <span
      className="region-flag"
      style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', flexWrap: 'wrap' }}
      aria-label={`Region: ${region || 'Unknown'}${languages ? `, Languages: ${languages}` : ''}`}
      tabIndex={0}
      role="img"
    >
      {regionCodes.map((code, i) => (
        <SingleFlag key={`r-${code}-${i}`} code={code} label={regions[i]} dimensions={dimensions} />
      ))}
      {showLabel && region && (
        <span style={{ fontSize: '12px', color: 'var(--ctp-subtext0)' }}>{region}</span>
      )}
      {showLabel && langCodes.length > 0 && (
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '2px', marginLeft: '4px' }}>
          {langCodes.map((code, i) => {
            const flagCode = langToFlag[code];
            if (!flagCode) return null;
            const langDim = { width: Math.round(dimensions.width * 0.75), height: Math.round(dimensions.height * 0.75) };
            return (
              <SingleFlag key={`l-${code}-${i}`} code={flagCode} label={langToName[code] || code} dimensions={langDim} />
            );
          })}
        </span>
      )}
    </span>
  );
};

export default RegionFlag;
