import { useEffect } from 'react';
import { progressHub } from '../api/progressHub';

// Single place that turns the SignalR libraryUpdated push into a
// window-level LIBRARY_UPDATED_EVENT for the pages. Mount once so every
// page benefits without each one having to subscribe manually.
const ProgressHubBridge = () => {
  useEffect(() => {
    // The server sends libraryUpdated right after scanFinished, so only this one reloads the pages
    return progressHub.on('libraryUpdated', () => {
      window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
    });
  }, []);

  return null;
};

export default ProgressHubBridge;
