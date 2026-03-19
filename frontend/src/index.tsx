import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './index.css';


const root = ReactDOM.createRoot(
  document.getElementById('root') as HTMLElement
);

root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

// Global link interceptor for Photino (Native Window)
// We specifically look for links with target="_blank" to open them in the system browser
document.addEventListener('click', function (e) {
  const target = (e.target as Element).closest('a');

  if (target && target.getAttribute('target') === '_blank') {
    const href = target.getAttribute('href');
    if (href) {
      // Check if running inside Photino (window.external won't be standard, but Photino injects it)
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const ext = (window as any).external;
      if (ext && typeof ext.sendMessage === 'function') {
        e.preventDefault();
        ext.sendMessage("OPEN_URL:" + href);
        return;
      }
    }
  }
});

// Define global API for Photino to call into React
// This avoids race conditions with window.external or event listeners
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(window as any).RetroArr_API = {
  onFolderSelected: (path: string) => {
    console.log("Global onFolderSelected called with:", path);
    // Dispatch event for components to listen to (redundant but safe fallback)
    window.dispatchEvent(new CustomEvent('FOLDER_SELECTED_EVENT', { detail: path }));
  },
  onSettingsUpdated: () => {
    console.log("Global onSettingsUpdated called");
    window.dispatchEvent(new Event('SETTINGS_UPDATED_EVENT'));
  },
  onLibraryUpdated: () => {
    console.log("Global onLibraryUpdated called");
    window.dispatchEvent(new Event('LIBRARY_UPDATED_EVENT'));
  }
};

// Global Photino Message Receiver (Legacy/Backup)
const attachExternalHandler = () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  if ((window as any).external) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).external.receiveMessage = (message: string) => {
      console.log(`[PHOTINO] Message Received: ${message}`);
      if (message.startsWith('FOLDER_SELECTED:')) {
        const path = message.substring('FOLDER_SELECTED:'.length);
        // Call our stable global API
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (window as any).RetroArr_API.onFolderSelected(path);
      } else if (message === 'SETTINGS_UPDATED') {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (window as any).RetroArr_API.onSettingsUpdated();
      } else if (message === 'LIBRARY_UPDATED') {
        console.log("[PHOTINO] Library update requested by backend");
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (window as any).RetroArr_API.onLibraryUpdated();
      }
    };
    console.log("Photino external interface attached successfully.");
    return true; // Attached
  }
  return false; // Not yet
};

// Try to attach immediately
if (!attachExternalHandler()) {
  // Retry every 100ms for 2 seconds
  const interval = setInterval(() => {
    if (attachExternalHandler()) {
      clearInterval(interval);
    }
  }, 100);

  // Clear interval after 5 seconds to stop polling
  setTimeout(() => clearInterval(interval), 5000);
}
