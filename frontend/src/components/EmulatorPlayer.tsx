import { useEffect, useRef, useState } from 'react';
import './EmulatorPlayer.css';

interface EmulatorPlayerProps {
    romUrl: string;
    gameTitle: string;
    // Platform.Slug. Drives core lookup. Must be the slug, not the display name.
    platform: string;
    // Display name for the header badge. Falls back to the slug.
    platformName?: string;
    gameId: number;
    onClose: () => void;
}

// Fallback for UI hints before /cores/mapping resolves. Keys are slugs.
const PLATFORM_TO_CORE: Record<string, string> = {
    // Nintendo
    nes: 'nes',
    fds: 'nes',         // FDS runs on the NES core
    snes: 'snes',
    sfc: 'snes',        // Super Famicom = JP SNES, same core
    n64: 'n64',
    gb: 'gb',
    gbc: 'gbc',
    gba: 'gba',
    nds: 'nds',
    virtualboy: 'vb',

    // Sega
    mastersystem: 'segaMS',
    megadrive: 'segaMD',
    gamegear: 'segaGG',
    saturn: 'segaSaturn',
    '32x': 'sega32x',
    segacd: 'segaCD',

    // Sony
    ps1: 'psx',
    psp: 'psp',

    // Atari
    atari2600: 'atari2600',
    atari5200: 'atari5200',
    atari7800: 'atari7800',
    lynx: 'lynx',
    jaguar: 'jaguar',

    // Other
    arcade: 'arcade',
    '3do': '3do',
    pcengine: 'pce',
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

const EmulatorPlayer = ({ romUrl, gameTitle, platform, platformName, gameId, onClose }: EmulatorPlayerProps) => {
    const iframeRef = useRef<HTMLIFrameElement>(null);
    const [error, setError] = useState<string | null>(null);

    const core = getEmulatorCore(platform);
    const displayName = platformName || platform;

    useEffect(() => {
        if (!core) {
            setError(`Platform "${displayName}" is not supported for web emulation.`);
            return;
        }

        // Backend player endpoint sets COOP/COEP for SharedArrayBuffer.
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
                        <span className="emulator-platform-badge">{displayName}</span>
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
