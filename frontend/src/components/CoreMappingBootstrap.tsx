import { useEffect } from 'react';
import { useCoreMapping } from '../api/hooks';
import { setCoreMappingFromApi } from './EmulatorPlayer';

// Pulls the EmulatorJS core mapping from the backend once and feeds it into
// the EmulatorPlayer module so getEmulatorCore can resolve via slug. Without
// this, the player falls back to the static map and any platform that's
// only in the backend (or only added there later) shows "not supported".
const CoreMappingBootstrap = () => {
  const { data } = useCoreMapping();

  useEffect(() => {
    if (!data) return;
    setCoreMappingFromApi(data);
  }, [data]);

  return null;
};

export default CoreMappingBootstrap;
