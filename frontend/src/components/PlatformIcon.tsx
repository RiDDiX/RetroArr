import React from 'react';

interface PlatformIconProps {
    platformSlug?: string;
    platformName?: string;
    platformId?: number;
    size?: number;
    className?: string;
    style?: React.CSSProperties;
}

// Map platform slugs/names to icon filenames
const PLATFORM_ICON_MAP: Record<string, string> = {
    // Nintendo
    'nes': 'nes.svg',
    'famicom': 'nes.svg',
    'nintendo-entertainment-system': 'nes.svg',
    'snes': 'snes.svg',
    'super-nintendo': 'snes.svg',
    'super-famicom': 'snes.svg',
    'n64': 'n64.svg',
    'nintendo-64': 'n64.svg',
    'gb': 'gb.svg',
    'gameboy': 'gb.svg',
    'game-boy': 'gb.svg',
    'gbc': 'gbc.svg',
    'gameboy-color': 'gbc.svg',
    'game-boy-color': 'gbc.svg',
    'gba': 'gba.svg',
    'gameboy-advance': 'gba.svg',
    'game-boy-advance': 'gba.svg',
    'nds': 'nds.svg',
    'nintendo-ds': 'nds.svg',
    'ds': 'nds.svg',
    '3ds': 'nds.svg',
    'nintendo-3ds': 'nds.svg',
    'wii': 'wii.svg',
    'wiiu': 'wiiu.svg',
    'wii-u': 'wiiu.svg',
    'switch': 'switch.svg',
    'nintendo-switch': 'switch.svg',
    'ngc': 'ngc.svg',
    'gamecube': 'ngc.svg',
    'gc': 'ngc.svg',
    'virtual-boy': 'vb.ico',
    'virtualboy': 'vb.ico',
    'vb': 'vb.ico',
    
    // Sega
    'genesis': 'genesis.svg',
    'mega-drive': 'genesis.svg',
    'megadrive': 'genesis.svg',
    'md': 'genesis.svg',
    'sega-genesis': 'genesis.svg',
    'sega-mega-drive': 'genesis.svg',
    'game-gear': 'gamegear.svg',
    'gamegear': 'gamegear.svg',
    'gg': 'gamegear.svg',
    'sega-game-gear': 'gamegear.svg',
    'saturn': 'saturn.ico',
    'sega-saturn': 'saturn.ico',
    'sega-32x': 'sega32.svg',
    '32x': 'sega32.svg',
    'sega32x': 'sega32.svg',
    'sega-cd': 'segacd.ico',
    'segacd': 'segacd.ico',
    'mega-cd': 'segacd.ico',
    'master-system': 'sms.ico',
    'sms': 'sms.ico',
    'sega-master-system': 'sms.ico',
    'dreamcast': 'dc.svg',
    'dc': 'dc.svg',
    'sega-dreamcast': 'dc.svg',
    
    // Sony
    'playstation': 'psx.svg',
    'psx': 'psx.svg',
    'ps1': 'psx.svg',
    'ps2': 'ps2.svg',
    'playstation-2': 'ps2.svg',
    'ps3': 'ps3.svg',
    'playstation-3': 'ps3.svg',
    'ps4': 'ps4.svg',
    'playstation-4': 'ps4.svg',
    'ps5': 'ps5.svg',
    'playstation-5': 'ps5.svg',
    'psp': 'psp.svg',
    'playstation-portable': 'psp.svg',
    'vita': 'psp.svg',
    'ps-vita': 'psp.svg',
    'playstation-vita': 'psp.svg',
    
    // Microsoft
    'xbox': 'xbox.svg',
    'xbox360': 'xbox360.svg',
    'xbox-360': 'xbox360.svg',
    'xboxone': 'xboxone.svg',
    'xbox-one': 'xboxone.svg',
    'xbox-series-x': 'xboxone.svg',
    'xbox-series-s': 'xboxone.svg',
    
    // Atari
    'atari-2600': 'atari2600.svg',
    'atari2600': 'atari2600.svg',
    'atari-5200': 'atari5200.ico',
    'atari5200': 'atari5200.ico',
    'atari-7800': 'atari7800.svg',
    'atari7800': 'atari7800.svg',
    'lynx': 'lynx.svg',
    'atari-lynx': 'lynx.svg',
    'jaguar': 'jaguar.svg',
    'atari-jaguar': 'jaguar.svg',
    
    // PC
    'pc': 'win.ico',
    'windows': 'win.ico',
    'win': 'win.ico',
    'mac': 'mac.ico',
    'macos': 'mac.ico',
    'linux': 'linux.svg',
    'dos': 'dos.ico',
    
    // Other
    'arcade': 'arcade.svg',
    'mame': 'arcade.svg',
    '3do': '3do.svg',
    'pce': 'pce.ico',
    'pc-engine': 'pce.ico',
    'turbografx-16': 'tg16.svg',
    'tg16': 'tg16.svg',
};

// Map platform IDs to slugs (IGDB-based)
const PLATFORM_ID_MAP: Record<number, string> = {
    // Nintendo
    18: 'nes',
    19: 'snes',
    4: 'n64',
    33: 'gb',
    22: 'gbc',
    24: 'gba',
    20: 'nds',
    37: '3ds',
    5: 'wii',
    41: 'wiiu',
    130: 'switch',
    21: 'ngc',
    87: 'vb',
    
    // Sega
    29: 'genesis',
    35: 'gamegear',
    32: 'saturn',
    30: '32x',
    78: 'segacd',
    64: 'sms',
    23: 'dreamcast',
    
    // Sony
    7: 'psx',
    8: 'ps2',
    9: 'ps3',
    48: 'ps4',
    167: 'ps5',
    38: 'psp',
    46: 'vita',
    
    // Microsoft
    11: 'xbox',
    12: 'xbox360',
    49: 'xboxone',
    169: 'xbox-series-x',
    
    // Atari
    59: 'atari2600',
    66: 'atari5200',
    60: 'atari7800',
    61: 'lynx',
    62: 'jaguar',
    
    // PC
    6: 'pc',
    14: 'mac',
    3: 'linux',
    13: 'dos',
    
    // Other
    52: 'arcade',
    50: '3do',
    86: 'pce',
    58: 'tg16',
};

const getIconPath = (slug?: string, name?: string, id?: number): string | null => {
    // Try by ID first
    if (id && PLATFORM_ID_MAP[id]) {
        const mappedSlug = PLATFORM_ID_MAP[id];
        if (PLATFORM_ICON_MAP[mappedSlug]) {
            return `/platforms/${PLATFORM_ICON_MAP[mappedSlug]}`;
        }
    }
    
    // Try by slug
    if (slug) {
        const normalizedSlug = slug.toLowerCase().replace(/[\s_]/g, '-');
        if (PLATFORM_ICON_MAP[normalizedSlug]) {
            return `/platforms/${PLATFORM_ICON_MAP[normalizedSlug]}`;
        }
    }
    
    // Try by name
    if (name) {
        const normalizedName = name.toLowerCase().replace(/[\s_]/g, '-');
        if (PLATFORM_ICON_MAP[normalizedName]) {
            return `/platforms/${PLATFORM_ICON_MAP[normalizedName]}`;
        }
        
        // Try partial matches
        for (const [key, icon] of Object.entries(PLATFORM_ICON_MAP)) {
            if (normalizedName.includes(key) || key.includes(normalizedName)) {
                return `/platforms/${icon}`;
            }
        }
    }
    
    return null;
};

const PlatformIcon: React.FC<PlatformIconProps> = ({
    platformSlug,
    platformName,
    platformId,
    size = 20,
    className = '',
    style = {}
}) => {
    const iconPath = getIconPath(platformSlug, platformName, platformId);
    
    if (!iconPath) {
        return null;
    }
    
    return (
        <img
            src={iconPath}
            alt={platformName || platformSlug || 'Platform'}
            className={`platform-icon ${className}`}
            style={{
                width: size,
                height: size,
                objectFit: 'contain',
                ...style
            }}
            onError={(e) => {
                // Hide if image fails to load
                (e.target as HTMLImageElement).style.display = 'none';
            }}
        />
    );
};

export default PlatformIcon;
export { getIconPath, PLATFORM_ICON_MAP, PLATFORM_ID_MAP };
