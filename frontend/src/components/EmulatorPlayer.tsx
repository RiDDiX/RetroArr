import { useEffect, useRef, useState } from 'react';
import './EmulatorPlayer.css';

interface EmulatorPlayerProps {
    romUrl: string;
    gameTitle: string;
    platform: string;
    gameId: number;
    onClose: () => void;
}

// Fallback map for synchronous UI hints (e.g. showing "Play in Browser" button).
// Authoritative source: GET /api/v3/emulator/cores/mapping (backend EmulatorController.PlatformIdToCore)
const PLATFORM_TO_CORE: Record<string, string> = {
    // Nintendo
    'nes': 'nes',
    'famicom': 'nes',
    'snes': 'snes',
    'super_nintendo': 'snes',
    'nintendo_64': 'n64',
    'n64': 'n64',
    'gameboy': 'gb',
    'game_boy': 'gb',
    'gameboy_color': 'gbc',
    'game_boy_color': 'gbc',
    'gameboy_advance': 'gba',
    'game_boy_advance': 'gba',
    'nintendo_ds': 'nds',
    'nds': 'nds',
    'virtual_boy': 'vb',
    
    // Sega
    'sega_master_system': 'segaMS',
    'master_system': 'segaMS',
    'sega_genesis': 'segaMD',
    'mega_drive': 'segaMD',
    'genesis': 'segaMD',
    'sega_game_gear': 'segaGG',
    'game_gear': 'segaGG',
    'sega_saturn': 'segaSaturn',
    'saturn': 'segaSaturn',
    'sega_32x': 'sega32x',
    '32x': 'sega32x',
    'sega_cd': 'segaCD',
    
    // Sony
    'playstation': 'psx',
    'ps1': 'psx',
    'psx': 'psx',
    'psp': 'psp',
    'playstation_portable': 'psp',
    
    // Atari
    'atari_2600': 'atari2600',
    'atari_5200': 'atari5200',
    'atari_7800': 'atari7800',
    'atari_lynx': 'lynx',
    'lynx': 'lynx',
    'atari_jaguar': 'jaguar',
    'jaguar': 'jaguar',
    
    // Other
    'arcade': 'arcade',
    'mame': 'mame2003',
    '3do': '3do',
    'pc_engine': 'pce',
    'turbografx_16': 'pce',
};

// Build a slug→core map from the API response for dynamic resolution
let _dynamicCoreMap: Record<string, string> | null = null;

export const setCoreMappingFromApi = (mapping: Array<{ slug: string; core: string }>) => {
    _dynamicCoreMap = {};
    for (const entry of mapping) {
        _dynamicCoreMap[entry.slug] = entry.core;
    }
};

export const isWebPlayable = (platformSlug: string): boolean => {
    const normalizedSlug = platformSlug.toLowerCase().replace(/[\s-]/g, '_');
    if (_dynamicCoreMap && normalizedSlug in _dynamicCoreMap) return true;
    return normalizedSlug in PLATFORM_TO_CORE;
};

export const getEmulatorCore = (platformSlug: string): string | null => {
    const normalizedSlug = platformSlug.toLowerCase().replace(/[\s-]/g, '_');
    if (_dynamicCoreMap && normalizedSlug in _dynamicCoreMap) return _dynamicCoreMap[normalizedSlug];
    return PLATFORM_TO_CORE[normalizedSlug] || null;
};

const EmulatorPlayer = ({ romUrl, gameTitle, platform, gameId, onClose }: EmulatorPlayerProps) => {
    const iframeRef = useRef<HTMLIFrameElement>(null);
    const [error, setError] = useState<string | null>(null);

    const core = getEmulatorCore(platform);

    useEffect(() => {
        if (!core) {
            setError(`Platform "${platform}" is not supported for web emulation.`);
            return;
        }

        // Point iframe at the backend player endpoint which sets COOP/COEP
        // headers for SharedArrayBuffer (needed by threaded cores like PSP).
        if (iframeRef.current) {
            const params = new URLSearchParams({
                rom: romUrl,
                core,
                title: gameTitle,
            });
            iframeRef.current.src = `/api/v3/emulator/player?${params.toString()}`;
        }
    }, [romUrl, core, gameTitle, gameId]);

    return (
        <div className="emulator-overlay">
            <div className="emulator-modal">
                <div className="emulator-header">
                    <h2>{gameTitle}</h2>
                    <div className="emulator-controls">
                        <span className="emulator-platform-badge">{platform}</span>
                        <button className="emulator-close-btn" onClick={onClose}>
                            &times;
                        </button>
                    </div>
                </div>

                <div className="emulator-content">
                    {error && (
                        <div className="emulator-error">
                            <p>{error}</p>
                            <button onClick={onClose}>Close</button>
                        </div>
                    )}

                    {/* Iframe isolates EmulatorJS from browser extension conflicts */}
                    {!error && (
                        <iframe
                            ref={iframeRef}
                            title={gameTitle}
                            style={{
                                width: '100%',
                                height: '100%',
                                border: 'none',
                                minHeight: '480px'
                            }}
                            allow="fullscreen; gamepad"
                        />
                    )}
                </div>

                <div className="emulator-footer">
                    <p>Press F11 for fullscreen • Powered by EmulatorJS</p>
                </div>
            </div>
        </div>
    );
};

export default EmulatorPlayer;
