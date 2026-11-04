import { useEffect } from 'react';
import { emulatorApi } from '../api/client';
import { setCoreMappingFromApi } from './EmulatorPlayer';

// Pulls the core mapping once and feeds it into EmulatorPlayer so the
// static fallback map doesn't go stale.
const CoreMappingBootstrap = () => {
  useEffect(() => {
    emulatorApi.getCoreMapping().then(r => setCoreMappingFromApi(r.data)).catch(() => {});
  }, []);

  return null;
};

export default CoreMappingBootstrap;
