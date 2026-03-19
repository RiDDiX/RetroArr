import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';

// Theme color definition
export interface ThemeColors {
  // Base colors
  base: string;
  mantle: string;
  crust: string;
  
  // Surface colors
  surface0: string;
  surface1: string;
  surface2: string;
  
  // Overlay colors
  overlay0: string;
  overlay1: string;
  overlay2: string;
  
  // Text colors
  text: string;
  subtext0: string;
  subtext1: string;
  
  // Accent colors
  blue: string;
  lavender: string;
  sapphire: string;
  sky: string;
  teal: string;
  green: string;
  yellow: string;
  peach: string;
  maroon: string;
  red: string;
  mauve: string;
  pink: string;
  flamingo: string;
  rosewater: string;
  
  // Primary accent (customizable)
  primary: string;
  primaryHover: string;
}

export interface Theme {
  id: string;
  name: string;
  description: string;
  author?: string;
  colors: ThemeColors;
  isCustom?: boolean;
}

// Predefined themes
export const THEMES: Theme[] = [
  {
    id: 'retroarr',
    name: 'RetroArr',
    description: 'The signature RetroArr look — deep dark with warm amber accents',
    author: 'RetroArr',
    colors: {
      base: '#0d1117',
      mantle: '#0a0e14',
      crust: '#070a0f',
      surface0: '#161b22',
      surface1: '#1c2333',
      surface2: '#2d3548',
      overlay0: '#484f5e',
      overlay1: '#5a6270',
      overlay2: '#6e7681',
      text: '#e6edf3',
      subtext0: '#8b949e',
      subtext1: '#b1bac4',
      blue: '#58a6ff',
      lavender: '#d2a8ff',
      sapphire: '#33b3de',
      sky: '#56d4dd',
      teal: '#39d2c0',
      green: '#3fb950',
      yellow: '#d29922',
      peach: '#f0883e',
      maroon: '#f47067',
      red: '#f85149',
      mauve: '#bc8cff',
      pink: '#f778ba',
      flamingo: '#ff9baa',
      rosewater: '#ffc2c7',
      primary: '#f0883e',
      primaryHover: '#f7b955',
    }
  },
  {
    id: 'catppuccin-mocha',
    name: 'Catppuccin Mocha',
    description: 'Soothing pastel theme for the high-spirited!',
    author: 'Catppuccin',
    colors: {
      base: '#1e1e2e',
      mantle: '#181825',
      crust: '#11111b',
      surface0: '#313244',
      surface1: '#45475a',
      surface2: '#585b70',
      overlay0: '#6c7086',
      overlay1: '#7f849c',
      overlay2: '#9399b2',
      text: '#cdd6f4',
      subtext0: '#a6adc8',
      subtext1: '#bac2de',
      blue: '#89b4fa',
      lavender: '#b4befe',
      sapphire: '#74c7ec',
      sky: '#89dceb',
      teal: '#94e2d5',
      green: '#a6e3a1',
      yellow: '#f9e2af',
      peach: '#fab387',
      maroon: '#eba0ac',
      red: '#f38ba8',
      mauve: '#cba6f7',
      pink: '#f5c2e7',
      flamingo: '#f2cdcd',
      rosewater: '#f5e0dc',
      primary: '#89b4fa',
      primaryHover: '#b4befe',
    }
  },
  {
    id: 'catppuccin-macchiato',
    name: 'Catppuccin Macchiato',
    description: 'Medium-dark variant with warm undertones',
    author: 'Catppuccin',
    colors: {
      base: '#24273a',
      mantle: '#1e2030',
      crust: '#181926',
      surface0: '#363a4f',
      surface1: '#494d64',
      surface2: '#5b6078',
      overlay0: '#6e738d',
      overlay1: '#8087a2',
      overlay2: '#939ab7',
      text: '#cad3f5',
      subtext0: '#a5adcb',
      subtext1: '#b8c0e0',
      blue: '#8aadf4',
      lavender: '#b7bdf8',
      sapphire: '#7dc4e4',
      sky: '#91d7e3',
      teal: '#8bd5ca',
      green: '#a6da95',
      yellow: '#eed49f',
      peach: '#f5a97f',
      maroon: '#ee99a0',
      red: '#ed8796',
      mauve: '#c6a0f6',
      pink: '#f5bde6',
      flamingo: '#f0c6c6',
      rosewater: '#f4dbd6',
      primary: '#8aadf4',
      primaryHover: '#b7bdf8',
    }
  },
  {
    id: 'riddix-dark',
    name: 'RiDDiX Dark',
    description: 'Hacker-inspired theme with green accents on deep black',
    author: 'RiDDiX',
    colors: {
      base: '#000000',
      mantle: '#000000',
      crust: '#000000',
      surface0: '#0a0a0a',
      surface1: '#0f0f0f',
      surface2: '#141414',
      overlay0: '#1a1a1a',
      overlay1: '#222222',
      overlay2: '#2a2a2a',
      text: '#00ff00',
      subtext0: '#00cc00',
      subtext1: '#00dd00',
      blue: '#00ffcc',
      lavender: '#00ff88',
      sapphire: '#00ccff',
      sky: '#00ffff',
      teal: '#00ffaa',
      green: '#00ff00',
      yellow: '#ccff00',
      peach: '#ffcc00',
      maroon: '#ff0044',
      red: '#ff0033',
      mauve: '#00ff66',
      pink: '#ff00ff',
      flamingo: '#ff0088',
      rosewater: '#ff99aa',
      primary: '#00ff00',
      primaryHover: '#33ff33',
    }
  },
  {
    id: 'nord',
    name: 'Nord',
    description: 'Arctic, north-bluish color palette',
    author: 'Arctic Ice Studio',
    colors: {
      base: '#2e3440',
      mantle: '#282c34',
      crust: '#242831',
      surface0: '#3b4252',
      surface1: '#434c5e',
      surface2: '#4c566a',
      overlay0: '#616e88',
      overlay1: '#6e7a94',
      overlay2: '#7b87a0',
      text: '#eceff4',
      subtext0: '#d8dee9',
      subtext1: '#e5e9f0',
      blue: '#81a1c1',
      lavender: '#b48ead',
      sapphire: '#5e81ac',
      sky: '#88c0d0',
      teal: '#8fbcbb',
      green: '#a3be8c',
      yellow: '#ebcb8b',
      peach: '#d08770',
      maroon: '#c97c7c',
      red: '#bf616a',
      mauve: '#b48ead',
      pink: '#c9a0c9',
      flamingo: '#d4a0a0',
      rosewater: '#e0c0c0',
      primary: '#88c0d0',
      primaryHover: '#8fbcbb',
    }
  },
  {
    id: 'dracula',
    name: 'Dracula',
    description: 'Dark theme with vibrant colors',
    author: 'Dracula Theme',
    colors: {
      base: '#282a36',
      mantle: '#21222c',
      crust: '#191a21',
      surface0: '#343746',
      surface1: '#3e4155',
      surface2: '#484b64',
      overlay0: '#6272a4',
      overlay1: '#7082b4',
      overlay2: '#7e92c4',
      text: '#f8f8f2',
      subtext0: '#bfbfbf',
      subtext1: '#d9d9d9',
      blue: '#8be9fd',
      lavender: '#bd93f9',
      sapphire: '#62d6e8',
      sky: '#8be9fd',
      teal: '#50fa7b',
      green: '#50fa7b',
      yellow: '#f1fa8c',
      peach: '#ffb86c',
      maroon: '#ff6e6e',
      red: '#ff5555',
      mauve: '#bd93f9',
      pink: '#ff79c6',
      flamingo: '#ff9999',
      rosewater: '#ffcccc',
      primary: '#bd93f9',
      primaryHover: '#ff79c6',
    }
  },
  {
    id: 'gruvbox-dark',
    name: 'Gruvbox Dark',
    description: 'Retro groove color scheme',
    author: 'morhetz',
    colors: {
      base: '#282828',
      mantle: '#1d2021',
      crust: '#141617',
      surface0: '#3c3836',
      surface1: '#504945',
      surface2: '#665c54',
      overlay0: '#7c6f64',
      overlay1: '#928374',
      overlay2: '#a89984',
      text: '#ebdbb2',
      subtext0: '#bdae93',
      subtext1: '#d5c4a1',
      blue: '#83a598',
      lavender: '#d3869b',
      sapphire: '#458588',
      sky: '#83a598',
      teal: '#8ec07c',
      green: '#b8bb26',
      yellow: '#fabd2f',
      peach: '#fe8019',
      maroon: '#cc241d',
      red: '#fb4934',
      mauve: '#d3869b',
      pink: '#d3869b',
      flamingo: '#fb4934',
      rosewater: '#f9c2c2',
      primary: '#fabd2f',
      primaryHover: '#fe8019',
    }
  },
  {
    id: 'tokyo-night',
    name: 'Tokyo Night',
    description: 'Clean dark theme inspired by Tokyo at night',
    author: 'enkia',
    colors: {
      base: '#1a1b26',
      mantle: '#16161e',
      crust: '#13131a',
      surface0: '#24283b',
      surface1: '#2f3549',
      surface2: '#3a4057',
      overlay0: '#565f89',
      overlay1: '#6b7394',
      overlay2: '#80879f',
      text: '#c0caf5',
      subtext0: '#9aa5ce',
      subtext1: '#a9b1d6',
      blue: '#7aa2f7',
      lavender: '#bb9af7',
      sapphire: '#7dcfff',
      sky: '#7dcfff',
      teal: '#73daca',
      green: '#9ece6a',
      yellow: '#e0af68',
      peach: '#ff9e64',
      maroon: '#f7768e',
      red: '#f7768e',
      mauve: '#bb9af7',
      pink: '#ff007c',
      flamingo: '#f7768e',
      rosewater: '#ffc0cb',
      primary: '#7aa2f7',
      primaryHover: '#bb9af7',
    }
  },
  {
    id: 'one-dark',
    name: 'One Dark',
    description: 'Atom One Dark inspired theme',
    author: 'Atom',
    colors: {
      base: '#282c34',
      mantle: '#21252b',
      crust: '#1b1f23',
      surface0: '#31363f',
      surface1: '#393f4a',
      surface2: '#414855',
      overlay0: '#5c6370',
      overlay1: '#6b7280',
      overlay2: '#7a8190',
      text: '#abb2bf',
      subtext0: '#8b929e',
      subtext1: '#9da4af',
      blue: '#61afef',
      lavender: '#c678dd',
      sapphire: '#56b6c2',
      sky: '#56b6c2',
      teal: '#56b6c2',
      green: '#98c379',
      yellow: '#e5c07b',
      peach: '#d19a66',
      maroon: '#be5046',
      red: '#e06c75',
      mauve: '#c678dd',
      pink: '#c678dd',
      flamingo: '#e06c75',
      rosewater: '#f0c0c0',
      primary: '#61afef',
      primaryHover: '#c678dd',
    }
  },
  {
    id: 'midnight-oled',
    name: 'Midnight OLED',
    description: 'Pure black for OLED screens with high contrast',
    colors: {
      base: '#000000',
      mantle: '#050508',
      crust: '#0a0a0f',
      surface0: '#111118',
      surface1: '#1a1a24',
      surface2: '#252530',
      overlay0: '#3a3a4a',
      overlay1: '#4a4a5c',
      overlay2: '#5c5c6e',
      text: '#e8e8f0',
      subtext0: '#a0a0b8',
      subtext1: '#c0c0d0',
      blue: '#3b82f6',
      lavender: '#818cf8',
      sapphire: '#0ea5e9',
      sky: '#38bdf8',
      teal: '#2dd4bf',
      green: '#34d399',
      yellow: '#fbbf24',
      peach: '#fb923c',
      maroon: '#f43f5e',
      red: '#ef4444',
      mauve: '#a78bfa',
      pink: '#f472b6',
      flamingo: '#fb7185',
      rosewater: '#fda4af',
      primary: '#3b82f6',
      primaryHover: '#60a5fa',
    }
  },
  {
    id: 'slate',
    name: 'Slate',
    description: 'Cool gray tones with a teal accent',
    colors: {
      base: '#0f1419',
      mantle: '#0b0e12',
      crust: '#070a0d',
      surface0: '#1a2028',
      surface1: '#242c38',
      surface2: '#2e3848',
      overlay0: '#4a5568',
      overlay1: '#5a6578',
      overlay2: '#6b7688',
      text: '#e2e8f0',
      subtext0: '#94a3b8',
      subtext1: '#b0bec5',
      blue: '#38bdf8',
      lavender: '#a78bfa',
      sapphire: '#0ea5e9',
      sky: '#7dd3fc',
      teal: '#2dd4bf',
      green: '#4ade80',
      yellow: '#fbbf24',
      peach: '#fb923c',
      maroon: '#f43f5e',
      red: '#f87171',
      mauve: '#c084fc',
      pink: '#f472b6',
      flamingo: '#fb7185',
      rosewater: '#fda4af',
      primary: '#2dd4bf',
      primaryHover: '#5eead4',
    }
  },
  {
    id: 'light-clean',
    name: 'Light Clean',
    description: 'Crisp white theme with blue accents',
    colors: {
      base: '#ffffff',
      mantle: '#f8fafc',
      crust: '#f1f5f9',
      surface0: '#e2e8f0',
      surface1: '#cbd5e1',
      surface2: '#94a3b8',
      overlay0: '#64748b',
      overlay1: '#475569',
      overlay2: '#334155',
      text: '#0f172a',
      subtext0: '#334155',
      subtext1: '#1e293b',
      blue: '#3b82f6',
      lavender: '#818cf8',
      sapphire: '#0ea5e9',
      sky: '#38bdf8',
      teal: '#14b8a6',
      green: '#22c55e',
      yellow: '#eab308',
      peach: '#f97316',
      maroon: '#be123c',
      red: '#ef4444',
      mauve: '#a855f7',
      pink: '#ec4899',
      flamingo: '#f43f5e',
      rosewater: '#fb7185',
      primary: '#3b82f6',
      primaryHover: '#2563eb',
    }
  },
  {
    id: 'light-warm',
    name: 'Light Warm',
    description: 'Warm tones with an amber accent, easy on the eyes',
    colors: {
      base: '#fefcf9',
      mantle: '#faf7f2',
      crust: '#f5f0e8',
      surface0: '#e8e0d4',
      surface1: '#d4cab8',
      surface2: '#b8aa96',
      overlay0: '#8c7e6a',
      overlay1: '#6e6252',
      overlay2: '#534a3c',
      text: '#1c1917',
      subtext0: '#44403c',
      subtext1: '#292524',
      blue: '#2563eb',
      lavender: '#7c3aed',
      sapphire: '#0284c7',
      sky: '#0ea5e9',
      teal: '#0d9488',
      green: '#16a34a',
      yellow: '#ca8a04',
      peach: '#ea580c',
      maroon: '#9f1239',
      red: '#dc2626',
      mauve: '#9333ea',
      pink: '#db2777',
      flamingo: '#e11d48',
      rosewater: '#f43f5e',
      primary: '#d97706',
      primaryHover: '#b45309',
    }
  },
  {
    id: 'retro-arcade',
    name: 'Retro Arcade',
    description: 'Deep purple with neon accents',
    colors: {
      base: '#1a0a2e',
      mantle: '#150826',
      crust: '#10061e',
      surface0: '#251340',
      surface1: '#301c52',
      surface2: '#3c2664',
      overlay0: '#5a3d8a',
      overlay1: '#6e50a0',
      overlay2: '#8264b6',
      text: '#f0e6ff',
      subtext0: '#c4b0e0',
      subtext1: '#d8caf0',
      blue: '#22d3ee',
      lavender: '#c084fc',
      sapphire: '#06b6d4',
      sky: '#67e8f9',
      teal: '#2dd4bf',
      green: '#4ade80',
      yellow: '#fde047',
      peach: '#fb923c',
      maroon: '#f43f5e',
      red: '#ff6b9d',
      mauve: '#e879f9',
      pink: '#f0abfc',
      flamingo: '#fb7185',
      rosewater: '#fda4af',
      primary: '#e879f9',
      primaryHover: '#f0abfc',
    }
  },
  {
    id: 'console-dark',
    name: 'Console Dark',
    description: 'Game launcher inspired dark blue-gray theme',
    colors: {
      base: '#1b2838',
      mantle: '#171f2b',
      crust: '#13191f',
      surface0: '#243447',
      surface1: '#2d4056',
      surface2: '#374c65',
      overlay0: '#4c6480',
      overlay1: '#5c7490',
      overlay2: '#6c84a0',
      text: '#c7d5e0',
      subtext0: '#8f98a0',
      subtext1: '#a8b4be',
      blue: '#66c0f4',
      lavender: '#9db4ff',
      sapphire: '#4dabf7',
      sky: '#74d1f6',
      teal: '#5ec4b6',
      green: '#a4d233',
      yellow: '#e5c263',
      peach: '#d4886c',
      maroon: '#c44040',
      red: '#d94040',
      mauve: '#8e6fbf',
      pink: '#d487c4',
      flamingo: '#e06c88',
      rosewater: '#e8a0a0',
      primary: '#66c0f4',
      primaryHover: '#8ad0f8',
    }
  },
];

interface ThemeContextType {
  currentTheme: Theme;
  setTheme: (themeId: string) => void;
  themes: Theme[];
  customTheme: Theme | null;
  setCustomTheme: (theme: Theme) => void;
  updateCustomColor: (colorKey: keyof ThemeColors, value: string) => void;
  resetCustomTheme: () => void;
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

const THEME_STORAGE_KEY = 'retroarr-theme';
const CUSTOM_THEME_STORAGE_KEY = 'retroarr-custom-theme';

// Compute relative luminance of a hex color for contrast decisions
const hexToLuminance = (hex: string): number => {
  const rgb = parseInt(hex.replace('#', ''), 16);
  const r = ((rgb >> 16) & 0xff) / 255;
  const g = ((rgb >> 8) & 0xff) / 255;
  const b = (rgb & 0xff) / 255;
  const [rl, gl, bl] = [r, g, b].map(c =>
    c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)
  );
  return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
};

// Apply theme to CSS variables
const applyTheme = (theme: Theme) => {
  const root = document.documentElement;
  const colors = theme.colors;
  
  // Apply all Catppuccin colors as CSS variables
  root.style.setProperty('--ctp-base', colors.base);
  root.style.setProperty('--ctp-mantle', colors.mantle);
  root.style.setProperty('--ctp-crust', colors.crust);
  root.style.setProperty('--ctp-surface0', colors.surface0);
  root.style.setProperty('--ctp-surface1', colors.surface1);
  root.style.setProperty('--ctp-surface2', colors.surface2);
  root.style.setProperty('--ctp-overlay0', colors.overlay0);
  root.style.setProperty('--ctp-overlay1', colors.overlay1);
  root.style.setProperty('--ctp-overlay2', colors.overlay2);
  root.style.setProperty('--ctp-text', colors.text);
  root.style.setProperty('--ctp-subtext0', colors.subtext0);
  root.style.setProperty('--ctp-subtext1', colors.subtext1);
  root.style.setProperty('--ctp-blue', colors.blue);
  root.style.setProperty('--ctp-lavender', colors.lavender);
  root.style.setProperty('--ctp-sapphire', colors.sapphire);
  root.style.setProperty('--ctp-sky', colors.sky);
  root.style.setProperty('--ctp-teal', colors.teal);
  root.style.setProperty('--ctp-green', colors.green);
  root.style.setProperty('--ctp-yellow', colors.yellow);
  root.style.setProperty('--ctp-peach', colors.peach);
  root.style.setProperty('--ctp-maroon', colors.maroon);
  root.style.setProperty('--ctp-red', colors.red);
  root.style.setProperty('--ctp-mauve', colors.mauve);
  root.style.setProperty('--ctp-pink', colors.pink);
  root.style.setProperty('--ctp-flamingo', colors.flamingo);
  root.style.setProperty('--ctp-rosewater', colors.rosewater);
  root.style.setProperty('--ctp-primary', colors.primary);
  root.style.setProperty('--ctp-primary-hover', colors.primaryHover);
  
  // Apply semantic/common variable names used throughout the app
  root.style.setProperty('--background', colors.base);
  root.style.setProperty('--background-secondary', colors.mantle);
  root.style.setProperty('--surface-0', colors.crust);
  root.style.setProperty('--surface-1', colors.surface0);
  root.style.setProperty('--surface-2', colors.surface1);
  root.style.setProperty('--surface-3', colors.surface2);
  root.style.setProperty('--border', colors.surface0);
  root.style.setProperty('--text-primary', colors.text);
  root.style.setProperty('--text-secondary', colors.subtext0);
  root.style.setProperty('--text-muted', colors.subtext1);
  root.style.setProperty('--accent', colors.primary);
  root.style.setProperty('--accent-hover', colors.primaryHover);
  root.style.setProperty('--success', colors.green);
  root.style.setProperty('--warning', colors.yellow);
  root.style.setProperty('--error', colors.red);
  root.style.setProperty('--info', colors.blue);

  // Legacy aliases used by Status.css, Problems.css, DatabaseSettings.css, UninstallModal.css
  root.style.setProperty('--accent-color', colors.primary);
  root.style.setProperty('--bg-secondary', colors.mantle);
  root.style.setProperty('--border-color', colors.surface0);
  root.style.setProperty('--border-light', colors.surface1);

  // Computed variables that reference theme colors
  root.style.setProperty('--gradient-primary', `linear-gradient(135deg, ${colors.primary} 0%, ${colors.primaryHover} 100%)`);
  root.style.setProperty('--accent-glow', `${colors.primary}66`);
  root.style.setProperty('--shadow-glow', `0 0 40px ${colors.primary}66`);

  // Semantic background tints
  const hexToRgb = (hex: string) => {
    const n = parseInt(hex.replace('#', ''), 16);
    return `${(n >> 16) & 0xff}, ${(n >> 8) & 0xff}, ${n & 0xff}`;
  };
  root.style.setProperty('--success-bg', `rgba(${hexToRgb(colors.green)}, 0.1)`);
  root.style.setProperty('--warning-bg', `rgba(${hexToRgb(colors.yellow)}, 0.1)`);
  root.style.setProperty('--error-bg', `rgba(${hexToRgb(colors.red)}, 0.1)`);
  root.style.setProperty('--info-bg', `rgba(${hexToRgb(colors.blue)}, 0.1)`);

  // Accent contrast: light text for dark accents, dark text for light accents
  const accentLum = hexToLuminance(colors.primary);
  root.style.setProperty('--accent-contrast', accentLum > 0.35 ? '#11111b' : '#ffffff');
  
  // Apply directly to body for immediate visual feedback
  document.body.style.backgroundColor = colors.base;
  document.body.style.color = colors.text;
  
  // Store theme ID for persistence
  localStorage.setItem(THEME_STORAGE_KEY, theme.id);
};

export const ThemeProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [currentTheme, setCurrentTheme] = useState<Theme>(THEMES[0]);
  const [customTheme, setCustomThemeState] = useState<Theme | null>(null);

  // Load theme on mount
  useEffect(() => {
    const savedThemeId = localStorage.getItem(THEME_STORAGE_KEY);
    const savedCustomTheme = localStorage.getItem(CUSTOM_THEME_STORAGE_KEY);
    
    if (savedCustomTheme) {
      try {
        const parsed = JSON.parse(savedCustomTheme);
        setCustomThemeState(parsed);
      } catch (e) {
        console.error('Failed to parse custom theme:', e);
      }
    }
    
    if (savedThemeId) {
      if (savedThemeId === 'custom' && savedCustomTheme) {
        const parsed = JSON.parse(savedCustomTheme);
        setCurrentTheme(parsed);
        applyTheme(parsed);
      } else {
        const theme = THEMES.find(t => t.id === savedThemeId);
        if (theme) {
          setCurrentTheme(theme);
          applyTheme(theme);
        }
      }
    } else {
      applyTheme(THEMES[0]);
    }
  }, []);

  const setTheme = (themeId: string) => {
    if (themeId === 'custom' && customTheme) {
      setCurrentTheme(customTheme);
      applyTheme(customTheme);
    } else {
      const theme = THEMES.find(t => t.id === themeId);
      if (theme) {
        setCurrentTheme(theme);
        applyTheme(theme);
      }
    }
  };

  const setCustomTheme = (theme: Theme) => {
    const customized = { ...theme, id: 'custom', isCustom: true };
    setCustomThemeState(customized);
    localStorage.setItem(CUSTOM_THEME_STORAGE_KEY, JSON.stringify(customized));
  };

  const updateCustomColor = (colorKey: keyof ThemeColors, value: string) => {
    const base = customTheme || { ...THEMES[0], id: 'custom', name: 'Custom Theme', isCustom: true };
    const updated: Theme = {
      ...base,
      colors: {
        ...base.colors,
        [colorKey]: value
      }
    };
    setCustomThemeState(updated);
    localStorage.setItem(CUSTOM_THEME_STORAGE_KEY, JSON.stringify(updated));
    
    // If custom theme is active, apply changes immediately
    if (currentTheme.id === 'custom') {
      setCurrentTheme(updated);
      applyTheme(updated);
    }
  };

  const resetCustomTheme = () => {
    setCustomThemeState(null);
    localStorage.removeItem(CUSTOM_THEME_STORAGE_KEY);
  };

  const allThemes = customTheme ? [...THEMES, customTheme] : THEMES;

  return (
    <ThemeContext.Provider value={{
      currentTheme,
      setTheme,
      themes: allThemes,
      customTheme,
      setCustomTheme,
      updateCustomColor,
      resetCustomTheme
    }}>
      {children}
    </ThemeContext.Provider>
  );
};

export const useTheme = () => {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return context;
};

export default ThemeContext;
