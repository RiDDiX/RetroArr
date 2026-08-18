import React from 'react';

interface Props {
  children: React.ReactNode;
}

interface State {
  failed: boolean;
}

// A lazy-loaded route chunk can 404 after a new build is deployed while a tab
// still runs the old bundle: navigating fires import('./pages/X') for a chunk
// hash that no longer exists on the server, the promise rejects, and without a
// boundary the route just hangs on the Suspense spinner -- forcing the user to
// hard-refresh. This boundary catches that failure and reloads once (fetching a
// fresh index.html that references the current chunk hashes). A short-lived
// sessionStorage guard prevents a reload loop if the reload does not fix it.
const RELOAD_GUARD_KEY = 'ra_chunk_reload_ts';
const RELOAD_GUARD_WINDOW_MS = 15_000;

function isChunkLoadError(error: unknown): boolean {
  if (!error) return false;
  const name = (error as { name?: string }).name || '';
  const msg = (error as { message?: string }).message || '';
  return (
    name === 'ChunkLoadError' ||
    /loading chunk [\d]+ failed/i.test(msg) ||
    /failed to fetch dynamically imported module/i.test(msg) ||
    /error loading dynamically imported module/i.test(msg) ||
    /importing a module script failed/i.test(msg)
  );
}

class ChunkErrorBoundary extends React.Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { failed: false };
  }

  static getDerivedStateFromError(error: unknown): State {
    if (isChunkLoadError(error)) {
      let recentlyReloaded = false;
      try {
        const last = Number(sessionStorage.getItem(RELOAD_GUARD_KEY) || '0');
        recentlyReloaded = last > 0 && Date.now() - last < RELOAD_GUARD_WINDOW_MS;
        if (!recentlyReloaded) {
          sessionStorage.setItem(RELOAD_GUARD_KEY, String(Date.now()));
        }
      } catch {
        // sessionStorage unavailable -> fall through to a manual reload prompt.
      }
      if (!recentlyReloaded) {
        window.location.reload();
        return { failed: false }; // reload in flight; keep spinner
      }
      // Already reloaded once and it still failed -> show a manual fallback.
      return { failed: true };
    }
    // Non-chunk render errors: surface a fallback rather than a blank screen.
    return { failed: true };
  }

  render() {
    if (this.state.failed) {
      return (
        <div className="page-loading" style={{ flexDirection: 'column', gap: 16, padding: 32, textAlign: 'center' }}>
          <p>This page failed to load. The app was likely updated in the background.</p>
          <button className="btn-primary" onClick={() => window.location.reload()}>
            Reload
          </button>
        </div>
      );
    }
    return this.props.children;
  }
}

export default ChunkErrorBoundary;
