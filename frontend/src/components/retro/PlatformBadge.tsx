import React from 'react';
import './PlatformBadge.css';

type Props = {
  slug: string;
  label?: string;
  size?: 'sm' | 'md';
};

const SHORT: Record<string, string> = {
  nes: 'NES', snes: 'SNES', n64: 'N64', gcn: 'GCN',
  wii: 'WII', wiiu: 'WIIU', switch: 'NSW', switch2: 'NSW2',
  gameboy: 'GB', gbc: 'GBC', gba: 'GBA', nds: 'NDS', '3ds': '3DS',
  ps1: 'PS1', ps2: 'PS2', ps3: 'PS3', ps4: 'PS4', ps5: 'PS5',
  psp: 'PSP', vita: 'PSV',
  xbox: 'XBX', xbox360: 'X360', xboxone: 'XB1', xboxseries: 'XSX',
  genesis: 'MD', megadrive: 'MD', saturn: 'SAT', dreamcast: 'DC',
  gamegear: 'GG', mastersystem: 'SMS', segacd: 'SCD',
  atari2600: 'A26', atari5200: 'A52', atari7800: 'A78',
  jaguar: 'JAG', lynx: 'LYNX', neogeo: 'NEO',
  pcengine: 'PCE', turbografx16: 'TG16',
  steam: 'STM', gog: 'GOG', arcade: 'ARC', mame: 'MAME',
  dos: 'DOS', dosbox: 'DOS', scummvm: 'SCVM',
  amiga: 'AMI', c64: 'C64', zxspectrum: 'ZX', msx: 'MSX',
};

export function PlatformBadge({ slug, label, size = 'sm' }: Props) {
  const short = label || SHORT[slug] || slug.slice(0, 4).toUpperCase();
  return (
    <span className={`platform-badge platform-badge--${size} plat-${slug}`} title={label || slug}>
      <span className="platform-badge__dot" aria-hidden="true" />
      <span className="platform-badge__text pixel">{short}</span>
    </span>
  );
}
