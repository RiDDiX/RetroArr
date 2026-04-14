import React from 'react';
import { Sidebar } from './Sidebar';
import { Dock } from './Dock';
import './AppShell.css';

export function AppShell({ children }: { children: React.ReactNode }) {
  return (
    <div className="app-shell retro-vignette">
      <Sidebar />
      <main id="main-content" className="app-shell__main">
        {children}
      </main>
      <Dock />
    </div>
  );
}
