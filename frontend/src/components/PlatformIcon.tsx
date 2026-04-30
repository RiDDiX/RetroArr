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
    // Nintendo - Home Consoles
    'nes': 'nes.svg',
    'famicom': 'nes.svg',
    'nintendo-entertainment-system': 'nes.svg',
    'fds': 'nes.svg',
    'snes': 'snes.svg',
    'super-nintendo': 'snes.svg',
    'super-famicom': 'snes.svg',
    'sfc': 'snes.svg',
    'satellaview': 'snes.svg',
    'sufami': 'snes.svg',
    'n64': 'n64.svg',
    'nintendo-64': 'n64.svg',
    'n64dd': 'n64.svg',
    'gamecube': 'ngc.svg',
    'ngc': 'ngc.svg',
    'gc': 'ngc.svg',
    'wii': 'wii.svg',
    'wiiu': 'wiiu.svg',
    'wii-u': 'wiiu.svg',
    'switch': 'switch.svg',
    'nintendo-switch': 'switch.svg',
    'switch2': 'switch.svg',

    // Nintendo - Handhelds
    'gb': 'gb.svg',
    'gameboy': 'gb.svg',
    'game-boy': 'gb.svg',
    'sgb': 'gb.svg',
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
    'virtual-boy': 'vb.ico',
    'virtualboy': 'vb.ico',
    'vb': 'vb.ico',
    'pokemini': 'gb.svg',
    'gameandwatch': 'nes.svg',

    // Sega
    'sg1000': 'sms.ico',
    'mastersystem': 'sms.ico',
    'master-system': 'sms.ico',
    'sms': 'sms.ico',
    'sega-master-system': 'sms.ico',
    'genesis': 'genesis.svg',
    'mega-drive': 'genesis.svg',
    'megadrive': 'genesis.svg',
    'md': 'genesis.svg',
    'sega-genesis': 'genesis.svg',
    'sega-mega-drive': 'genesis.svg',
    'segacd': 'segacd.ico',
    'sega-cd': 'segacd.ico',
    'mega-cd': 'segacd.ico',
    '32x': 'sega32.svg',
    'sega-32x': 'sega32.svg',
    'sega32x': 'sega32.svg',
    'gamegear': 'gamegear.svg',
    'game-gear': 'gamegear.svg',
    'gg': 'gamegear.svg',
    'sega-game-gear': 'gamegear.svg',
    'saturn': 'saturn.ico',
    'sega-saturn': 'saturn.ico',
    'dreamcast': 'dc.svg',
    'dc': 'dc.svg',
    'sega-dreamcast': 'dc.svg',
    'naomi': 'dc.svg',
    'naomi2': 'dc.svg',
    'atomiswave': 'dc.svg',
    'segastv': 'saturn.ico',
    'chihiro': 'xbox.svg',

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
    'xboxseriesx': 'xboxone.svg',
    'xbox-series-x': 'xboxone.svg',
    'xbox-series-s': 'xboxone.svg',

    // Atari
    'atari2600': 'atari2600.svg',
    'atari-2600': 'atari2600.svg',
    'atari5200': 'atari5200.ico',
    'atari-5200': 'atari5200.ico',
    'atari7800': 'atari7800.svg',
    'atari-7800': 'atari7800.svg',
    'lynx': 'lynx.svg',
    'atari-lynx': 'lynx.svg',
    'jaguar': 'jaguar.svg',
    'atari-jaguar': 'jaguar.svg',
    'jaguarcd': 'jaguar.svg',
    'atarist': 'atari2600.svg',
    'atari800': 'atari2600.svg',
    'xegs': 'atari2600.svg',

    // NEC / PC Engine
    'pcengine': 'pce.ico',
    'pcenginecd': 'pce.ico',
    'supergrafx': 'pce.ico',
    'pcfx': 'pce.ico',
    'pce': 'pce.ico',
    'pc-engine': 'pce.ico',
    'turbografx-16': 'tg16.svg',
    'tg16': 'tg16.svg',

    // Arcade
    'arcade': 'arcade.svg',
    'mame': 'arcade.svg',
    'fbneo': 'arcade.svg',
    'neogeo': 'arcade.svg',
    'neogeocd': 'arcade.svg',
    'neogeo64': 'arcade.svg',
    'cps1': 'arcade.svg',
    'cps2': 'arcade.svg',
    'cps3': 'arcade.svg',
    'daphne': 'arcade.svg',
    'hbmame': 'arcade.svg',
    'cave': 'arcade.svg',
    'zinc': 'arcade.svg',
    'namco2x6': 'arcade.svg',
    'teknoparrot': 'arcade.svg',
    'gaelco': 'arcade.svg',

    // PC / Computer
    'pc': 'win.ico',
    'windows': 'win.ico',
    'win': 'win.ico',
    'mac': 'mac.ico',
    'macos': 'mac.ico',
    'linux': 'linux.svg',
    'dos': 'dos.ico',
    'dosbox': 'dos.ico',
    'scummvm': 'dos.ico',

    // Other consoles
    '3do': '3do.svg',
};

// Fallback: resolve by backend platform id when a caller can't supply a slug yet
// (e.g. ReviewImport rendering items before the platform list is loaded).
// Keep in sync with PlatformDefinitions.cs - only ids that have a corresponding
// icon in PLATFORM_ICON_MAP are listed here; everything else returns null.
const PLATFORM_ID_TO_SLUG: Record<number, string> = {
    1: 'pc', 2: 'macos', 3: 'dos', 4: 'amiga', 5: 'cd32', 6: 'c64',
    7: 'vic20', 8: 'zxspectrum', 9: 'msx', 10: 'msx2', 11: 'x68000',
    12: 'apple2', 13: 'bbcmicro', 14: '3do', 15: 'amigacdtv', 16: 'amiga1200',
    17: 'amiga4000', 18: 'amstradcpc', 19: 'apple2gs',
    20: 'ps1', 21: 'ps2', 22: 'ps3', 23: 'ps4', 24: 'ps5',
    25: 'psp', 26: 'vita',
    30: 'xbox', 31: 'xbox360', 32: 'xboxone', 33: 'xboxseriesx',
    40: 'nes', 41: 'snes', 42: 'n64', 43: 'gamecube', 44: 'wii', 45: 'wiiu',
    46: 'switch', 47: 'switch2', 48: 'fds', 49: 'sfc',
    50: 'gb', 51: 'gbc', 52: 'gba', 53: 'nds', 54: '3ds',
    55: 'virtualboy', 56: 'pokemini', 57: 'n64dd',
    58: 'satellaview', 59: 'sufami',
    60: 'sg1000', 61: 'mastersystem', 62: 'megadrive', 63: 'segacd',
    64: '32x', 65: 'gamegear', 66: 'saturn', 67: 'dreamcast',
    68: 'naomi', 69: 'naomi2', 70: 'atomiswave',
    80: 'atari2600', 81: 'atari5200', 82: 'atari7800', 83: 'jaguar',
    84: 'jaguarcd', 85: 'lynx', 86: 'atarist', 87: 'atari800', 88: 'xegs',
    90: 'pcengine', 91: 'pcenginecd', 92: 'supergrafx', 93: 'pcfx',
    100: 'arcade', 101: 'fbneo', 102: 'neogeo', 103: 'neogeocd',
    104: 'cps1', 105: 'cps2', 106: 'cps3', 107: 'daphne', 108: 'hbmame',
    109: 'neogeo64',
    110: 'wonderswan', 111: 'wonderswancolor', 112: 'ngp', 113: 'ngpc',
    118: 'gameandwatch',
    120: 'scummvm', 121: 'dosbox',
    125: 'pc', 126: 'pc', 127: 'sgb',
    128: 'cave', 129: 'zinc', 130: 'namco2x6', 131: 'teknoparrot',
    132: 'gaelco',
};

const getIconPath = (slug?: string, name?: string, id?: number): string | null => {
    // Prefer the slug from the backend; it's the single source of truth.
    if (slug) {
        const normalizedSlug = slug.toLowerCase().replace(/[\s_]/g, '-');
        if (PLATFORM_ICON_MAP[normalizedSlug]) {
            return `/platforms/${PLATFORM_ICON_MAP[normalizedSlug]}`;
        }
        const rawSlug = slug.toLowerCase();
        if (PLATFORM_ICON_MAP[rawSlug]) {
            return `/platforms/${PLATFORM_ICON_MAP[rawSlug]}`;
        }
    }

    // Fallback: resolve by id when the caller has no slug
    if (id && PLATFORM_ID_TO_SLUG[id]) {
        const mapped = PLATFORM_ID_TO_SLUG[id];
        if (PLATFORM_ICON_MAP[mapped]) {
            return `/platforms/${PLATFORM_ICON_MAP[mapped]}`;
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
export { getIconPath, PLATFORM_ICON_MAP };
