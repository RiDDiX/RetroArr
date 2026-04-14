import React, { Suspense } from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const Library = React.lazy(() => import('./pages/Library'));
const Settings = React.lazy(() => import('./pages/Settings'));
const GameDetails = React.lazy(() => import('./pages/GameDetails'));
const About = React.lazy(() => import('./pages/About'));
const User = React.lazy(() => import('./pages/User'));
const Status = React.lazy(() => import('./pages/Status'));
const Collections = React.lazy(() => import('./pages/Collections'));
const Dashboard = React.lazy(() => import('./pages/Dashboard'));
const Statistics = React.lazy(() => import('./pages/Statistics'));
const Problems = React.lazy(() => import('./pages/Problems'));
const LibraryResort = React.lazy(() => import('./pages/LibraryResort'));
const MetadataReview = React.lazy(() => import('./pages/MetadataReview'));
const ReviewImport = React.lazy(() => import('./pages/ReviewImport'));
const SaveStates = React.lazy(() => import('./pages/SaveStates'));
const Platforms = React.lazy(() => import('./pages/Platforms'));
const NotFound = React.lazy(() => import('./pages/NotFound'));
import { AppShell } from './components/layout/AppShell';
import { BootScreen } from './components/retro';
import ScannerStatus from './components/ScannerStatus';
import { UIProvider } from './context/UIContext';
import { ThemeProvider } from './context/ThemeContext';
import KofiOverlay from './components/KofiOverlay';
import LanguageSwitcher from './components/LanguageSwitcher';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
});

function App() {
  return (
    <QueryClientProvider client={queryClient}>
    <ThemeProvider>
    <UIProvider>
      <Router>
        <BootScreen />
        <KofiOverlay />
        <ScannerStatus />
        <LanguageSwitcher />
        <AppShell>
          <Suspense fallback={<div className="page-loading"><div className="loading-spinner" /></div>}>
            <Routes>
              <Route path="/" element={<Dashboard />} />
              <Route path="/dashboard" element={<Dashboard />} />
              <Route path="/library" element={<Library />} />
              <Route path="/platforms" element={<Platforms />} />
              <Route path="/statistics" element={<Statistics />} />
              <Route path="/status" element={<Status />} />
              <Route path="/problems" element={<Problems />} />
              <Route path="/library-resort" element={<LibraryResort />} />
              <Route path="/metadata-review" element={<MetadataReview />} />
              <Route path="/review-import" element={<ReviewImport />} />
              <Route path="/user" element={<User />} />
              <Route path="/game/:id" element={<GameDetails />} />
              <Route path="/game/:id/saves" element={<SaveStates />} />
              <Route path="/collections" element={<Collections />} />
              <Route path="/settings" element={<Settings />} />
              <Route path="/about" element={<About />} />
              <Route path="*" element={<NotFound />} />
            </Routes>
          </Suspense>
        </AppShell>
      </Router>
    </UIProvider>
    </ThemeProvider>
    </QueryClientProvider>
  );
}

export default App;
