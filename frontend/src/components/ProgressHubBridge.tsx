import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { progressHub } from '../api/progressHub';
import { queryKeys } from '../api/hooks';

// Single place that turns SignalR push events into cache invalidations
// and a window-level LIBRARY_UPDATED_EVENT for legacy listeners. Mount
// once inside QueryClientProvider so every page benefits without each
// one having to subscribe manually.
const ProgressHubBridge = () => {
  const queryClient = useQueryClient();

  useEffect(() => {
    const offLibrary = progressHub.on('libraryUpdated', () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.games.all });
      queryClient.invalidateQueries({ queryKey: queryKeys.games.problems });
      queryClient.invalidateQueries({ queryKey: queryKeys.platforms.all });
      queryClient.invalidateQueries({ queryKey: queryKeys.platforms.enabled });
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.stats });
      queryClient.invalidateQueries({ queryKey: queryKeys.collections.all });
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
    });

    const offScanFinished = progressHub.on('scanFinished', () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.games.all });
      queryClient.invalidateQueries({ queryKey: queryKeys.games.problems });
      queryClient.invalidateQueries({ queryKey: queryKeys.dashboard.stats });
      queryClient.invalidateQueries({ queryKey: queryKeys.media.scanStatus });
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
    });

    const offDownloadSnapshot = progressHub.on('downloadSnapshot', () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.downloadClients.all });
    });

    return () => {
      offLibrary();
      offScanFinished();
      offDownloadSnapshot();
    };
  }, [queryClient]);

  return null;
};

export default ProgressHubBridge;
