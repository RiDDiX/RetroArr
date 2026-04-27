import { useEffect } from 'react';
import { useCoreMapping } from '../api/hooks';
import { setCoreMappingFromApi } from './EmulatorPlayer';

// Pulls the core mapping once and feeds it into EmulatorPlayer so the
// static fallback map doesn't go stale.
const CoreMappingBootstrap = () => {
  const { data } = useCoreMapping();

  useEffect(() => {
    if (!data) return;
    setCoreMappingFromApi(data);
  }, [data]);

  return null;
};

export default CoreMappingBootstrap;
