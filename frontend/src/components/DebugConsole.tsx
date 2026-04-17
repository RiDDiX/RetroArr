import React, { useState, useEffect, useRef } from 'react';
import apiClient, { getErrorMessage, isAxiosError } from '../api/client';
import { Language } from '../i18n/translations';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faSync, faTrash, faPlay, faPause, faSearch, faGamepad, faServer, faDownload } from '@fortawesome/free-solid-svg-icons';

interface LogEntry {
  timestamp: string;
  level: string;
  category: string;
  message: string;
}

interface ScanProgress {
  isScanning: boolean;
  currentDirectory: string | null;
  currentFile: string | null;
  filesScanned: number;
  gamesFound: number;
  lastGameFound: string | null;
}

interface ApiTestResult {
  success: boolean;
  message: string;
  data?: unknown;
  duration?: number;
}

interface DebugConsoleProps {
  language: Language;
}

const DebugConsole: React.FC<DebugConsoleProps> = () => {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [scanProgress, setScanProgress] = useState<ScanProgress | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [filter, setFilter] = useState<string>('');
  const [levelFilter, setLevelFilter] = useState<string>('');
  const logContainerRef = useRef<HTMLDivElement>(null);
  
  // API Testing States
  const [activeTestTab, setActiveTestTab] = useState<'logs' | 'prowlarr' | 'gog' | 'indexers'>('logs');
  const [testQuery, setTestQuery] = useState('Mario Kart');
  const [testCategories, setTestCategories] = useState('1000,1030');
  const [testGogId, setTestGogId] = useState('');
  const [testResults, setTestResults] = useState<ApiTestResult | null>(null);
  const [isTesting, setIsTesting] = useState(false);

  // API Test Functions
  const testProwlarrSearch = async () => {
    setIsTesting(true);
    setTestResults(null);
    const startTime = Date.now();
    try {
      const response = await apiClient.get('/search', {
        params: { query: testQuery, categories: testCategories },
        timeout: 120000 // 120s to match backend Prowlarr timeout
      });
      const duration = Date.now() - startTime;
      setTestResults({
        success: true,
        message: `Found ${response.data?.length || 0} results in ${duration}ms`,
        data: response.data,
        duration
      });
    } catch (err: unknown) {
      const duration = Date.now() - startTime;
      setTestResults({
        success: false,
        message: getErrorMessage(err, 'Unknown error'),
        data: isAxiosError(err) ? err.response?.data : undefined,
        duration
      });
    } finally {
      setIsTesting(false);
    }
  };

  // Debug: Get raw Prowlarr response
  const testProwlarrRaw = async () => {
    setIsTesting(true);
    setTestResults(null);
    const startTime = Date.now();
    try {
      const response = await apiClient.get('/search/debug/prowlarr', {
        params: { query: testQuery, categories: testCategories },
        timeout: 120000 // 120s to match backend Prowlarr timeout
      });
      const duration = Date.now() - startTime;
      setTestResults({
        success: response.data?.success || false,
        message: `Raw response: ${response.data?.responseLength || 0} bytes, Status: ${response.data?.statusCode}`,
        data: response.data,
        duration
      });
    } catch (err: unknown) {
      const duration = Date.now() - startTime;
      setTestResults({
        success: false,
        message: getErrorMessage(err, 'Unknown error'),
        data: isAxiosError(err) ? err.response?.data : undefined,
        duration
      });
    } finally {
      setIsTesting(false);
    }
  };

  const testGogDownloads = async () => {
    if (!testGogId) {
      setTestResults({ success: false, message: 'Enter a GOG Game ID' });
      return;
    }
    setIsTesting(true);
    setTestResults(null);
    const startTime = Date.now();
    try {
      const response = await apiClient.get(`/settings/gog/downloads/${testGogId}`);
      const duration = Date.now() - startTime;
      setTestResults({
        success: response.data?.success || false,
        message: response.data?.success 
          ? `Found ${response.data?.downloads?.length || 0} downloads in ${duration}ms`
          : response.data?.message || 'No downloads found',
        data: response.data,
        duration
      });
    } catch (err: unknown) {
      const duration = Date.now() - startTime;
      setTestResults({
        success: false,
        message: getErrorMessage(err, 'Unknown error'),
        data: isAxiosError(err) ? err.response?.data : undefined,
        duration
      });
    } finally {
      setIsTesting(false);
    }
  };

  const testGogStatus = async () => {
    setIsTesting(true);
    setTestResults(null);
    const startTime = Date.now();
    try {
      const response = await apiClient.get('/gog/status');
      const duration = Date.now() - startTime;
      setTestResults({
        success: response.data?.isConnected || false,
        message: response.data?.isConnected 
          ? `Connected as ${response.data?.username || 'Unknown'} with ${response.data?.gamesCount || 0} games`
          : 'Not connected to GOG',
        data: response.data,
        duration
      });
    } catch (err: unknown) {
      const duration = Date.now() - startTime;
      setTestResults({
        success: false,
        message: getErrorMessage(err, 'Unknown error'),
        data: isAxiosError(err) ? err.response?.data : undefined,
        duration
      });
    } finally {
      setIsTesting(false);
    }
  };

  const testIndexerConnection = async () => {
    setIsTesting(true);
    setTestResults(null);
    const startTime = Date.now();
    try {
      // Test Prowlarr connection using saved settings
      const prowlarrResponse = await apiClient.get('/search/test/prowlarr');
      const duration = Date.now() - startTime;
      setTestResults({
        success: prowlarrResponse.data?.connected || false,
        message: prowlarrResponse.data?.connected 
          ? `Prowlarr connected: ${prowlarrResponse.data?.indexerCount || 0} indexers available`
          : prowlarrResponse.data?.message || 'Connection failed',
        data: prowlarrResponse.data,
        duration
      });
    } catch (err: unknown) {
      const duration = Date.now() - startTime;
      setTestResults({
        success: false,
        message: getErrorMessage(err, 'Unknown error'),
        data: isAxiosError(err) ? err.response?.data : undefined,
        duration
      });
    } finally {
      setIsTesting(false);
    }
  };

  const fetchLogs = async () => {
    try {
      const response = await apiClient.get('/debug/logs', {
        params: { count: 200 }
      });
      setLogs(response.data);
    } catch (error) {
      console.error('Error fetching logs:', error);
    }
  };

  const fetchScanProgress = async () => {
    try {
      const response = await apiClient.get('/debug/scan-progress');
      setScanProgress(response.data);
    } catch (error) {
      console.error('Error fetching scan progress:', error);
    }
  };

  const clearLogs = async () => {
    try {
      await apiClient.delete('/debug/logs');
      setLogs([]);
    } catch (error) {
      console.error('Error clearing logs:', error);
    }
  };

  useEffect(() => {
    fetchLogs();
    fetchScanProgress();

    let intervalId: NodeJS.Timeout | null = null;
    if (autoRefresh) {
      intervalId = setInterval(() => {
        fetchLogs();
        fetchScanProgress();
      }, 1000);
    }

    return () => {
      if (intervalId) clearInterval(intervalId);
    };
  }, [autoRefresh]);

  useEffect(() => {
    // Auto-scroll to bottom when new logs arrive
    if (logContainerRef.current && autoRefresh) {
      logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight;
    }
  }, [logs, autoRefresh]);

  const filteredLogs = logs.filter(log => {
    const matchesText = !filter || 
      log.message.toLowerCase().includes(filter.toLowerCase()) ||
      log.category.toLowerCase().includes(filter.toLowerCase());
    const matchesLevel = !levelFilter || log.level === levelFilter;
    return matchesText && matchesLevel;
  });

  const getLevelColor = (level: string) => {
    switch (level) {
      case 'error': return 'var(--ctp-red)';
      case 'warning': return 'var(--ctp-yellow)';
      case 'info': return 'var(--ctp-green)';
      case 'debug': return 'var(--ctp-overlay0)';
      default: return '#fff';
    }
  };

  const getLevelBadge = (level: string) => {
    const colors: Record<string, string> = {
      error: 'var(--ctp-red)',
      warning: 'var(--ctp-yellow)',
      info: 'var(--ctp-green)',
      debug: 'var(--ctp-overlay0)'
    };
    return (
      <span style={{
        backgroundColor: colors[level] || 'var(--ctp-overlay0)',
        color: 'var(--accent-contrast)',
        padding: '2px 6px',
        borderRadius: '3px',
        fontSize: '10px',
        fontWeight: 'bold',
        textTransform: 'uppercase',
        marginRight: '8px'
      }}>
        {level}
      </span>
    );
  };

  return (
    <div className="settings-section" id="debug">
      <div className="section-header-with-logo">
        <h3>🔧 Debug Console</h3>
      </div>
      <p className="settings-description">
        Live system logs, scan progress, and API diagnostics. Use this to diagnose issues with scanning, metadata fetching, or downloads.
      </p>

      {/* Tab Navigation */}
      <div style={{ display: 'flex', gap: '5px', marginBottom: '20px', borderBottom: '1px solid #444', paddingBottom: '10px' }}>
        <button
          onClick={() => { setActiveTestTab('logs'); setTestResults(null); }}
          style={{
            padding: '8px 16px',
            borderRadius: '4px 4px 0 0',
            border: 'none',
            background: activeTestTab === 'logs' ? 'var(--ctp-green)' : 'var(--ctp-surface0)',
            color: activeTestTab === 'logs' ? '#000' : '#fff',
            cursor: 'pointer',
            fontWeight: activeTestTab === 'logs' ? 'bold' : 'normal'
          }}
        >
          <FontAwesomeIcon icon={faServer} style={{ marginRight: '6px' }} />
          System Logs
        </button>
        <button
          onClick={() => { setActiveTestTab('prowlarr'); setTestResults(null); }}
          style={{
            padding: '8px 16px',
            borderRadius: '4px 4px 0 0',
            border: 'none',
            background: activeTestTab === 'prowlarr' ? 'var(--ctp-green)' : 'var(--ctp-surface0)',
            color: activeTestTab === 'prowlarr' ? '#000' : '#fff',
            cursor: 'pointer',
            fontWeight: activeTestTab === 'prowlarr' ? 'bold' : 'normal'
          }}
        >
          <FontAwesomeIcon icon={faSearch} style={{ marginRight: '6px' }} />
          Prowlarr Search
        </button>
        <button
          onClick={() => { setActiveTestTab('gog'); setTestResults(null); }}
          style={{
            padding: '8px 16px',
            borderRadius: '4px 4px 0 0',
            border: 'none',
            background: activeTestTab === 'gog' ? 'var(--ctp-green)' : 'var(--ctp-surface0)',
            color: activeTestTab === 'gog' ? '#000' : '#fff',
            cursor: 'pointer',
            fontWeight: activeTestTab === 'gog' ? 'bold' : 'normal'
          }}
        >
          <FontAwesomeIcon icon={faGamepad} style={{ marginRight: '6px' }} />
          GOG API
        </button>
        <button
          onClick={() => { setActiveTestTab('indexers'); setTestResults(null); }}
          style={{
            padding: '8px 16px',
            borderRadius: '4px 4px 0 0',
            border: 'none',
            background: activeTestTab === 'indexers' ? 'var(--ctp-green)' : 'var(--ctp-surface0)',
            color: activeTestTab === 'indexers' ? '#000' : '#fff',
            cursor: 'pointer',
            fontWeight: activeTestTab === 'indexers' ? 'bold' : 'normal'
          }}
        >
          <FontAwesomeIcon icon={faDownload} style={{ marginRight: '6px' }} />
          Indexer Test
        </button>
      </div>

      {/* Prowlarr Search Test Tab */}
      {activeTestTab === 'prowlarr' && (
        <div style={{ background: 'var(--ctp-surface0)', borderRadius: '8px', padding: '20px', marginBottom: '20px' }}>
          <h4 style={{ margin: '0 0 15px 0', color: 'var(--ctp-green)' }}>
            <FontAwesomeIcon icon={faSearch} style={{ marginRight: '8px' }} />
            Prowlarr Search Test
          </h4>
          <div style={{ display: 'flex', gap: '10px', marginBottom: '15px', flexWrap: 'wrap' }}>
            <input
              type="text"
              placeholder="Search query (e.g., Mario Kart)"
              value={testQuery}
              onChange={(e) => setTestQuery(e.target.value)}
              style={{
                flex: 2,
                minWidth: '200px',
                padding: '10px 12px',
                borderRadius: '4px',
                border: '1px solid var(--ctp-surface1)',
                background: 'var(--ctp-crust)',
                color: 'var(--text-primary)'
              }}
            />
            <input
              type="text"
              placeholder="Categories (e.g., 1000,1030)"
              value={testCategories}
              onChange={(e) => setTestCategories(e.target.value)}
              style={{
                flex: 1,
                minWidth: '150px',
                padding: '10px 12px',
                borderRadius: '4px',
                border: '1px solid var(--ctp-surface1)',
                background: 'var(--ctp-crust)',
                color: 'var(--text-primary)'
              }}
            />
            <button
              onClick={testProwlarrSearch}
              disabled={isTesting}
              style={{
                padding: '10px 20px',
                borderRadius: '4px',
                border: 'none',
                background: isTesting ? 'var(--ctp-overlay0)' : 'var(--ctp-green)',
                color: 'var(--accent-contrast)',
                cursor: isTesting ? 'not-allowed' : 'pointer',
                fontWeight: 'bold'
              }}
            >
              {isTesting ? 'Testing...' : 'Test Search'}
            </button>
            <button
              onClick={testProwlarrRaw}
              disabled={isTesting}
              style={{
                padding: '10px 20px',
                borderRadius: '4px',
                border: 'none',
                background: isTesting ? 'var(--ctp-overlay0)' : 'var(--ctp-peach)',
                color: 'var(--accent-contrast)',
                cursor: isTesting ? 'not-allowed' : 'pointer',
                fontWeight: 'bold'
              }}
            >
              {isTesting ? 'Testing...' : 'Debug Raw Response'}
            </button>
          </div>
          <div style={{ fontSize: '12px', color: 'var(--ctp-overlay0)', marginBottom: '10px' }}>
            <strong>Common Categories:</strong> 1000=Console, 1030=Wii/Switch, 4000=PC, 4050=Games
          </div>
        </div>
      )}

      {/* GOG API Test Tab */}
      {activeTestTab === 'gog' && (
        <div style={{ background: 'var(--ctp-surface0)', borderRadius: '8px', padding: '20px', marginBottom: '20px' }}>
          <h4 style={{ margin: '0 0 15px 0', color: 'var(--ctp-green)' }}>
            <FontAwesomeIcon icon={faGamepad} style={{ marginRight: '8px' }} />
            GOG API Test
          </h4>
          <div style={{ display: 'flex', gap: '10px', marginBottom: '15px', flexWrap: 'wrap' }}>
            <button
              onClick={testGogStatus}
              disabled={isTesting}
              style={{
                padding: '10px 20px',
                borderRadius: '4px',
                border: 'none',
                background: isTesting ? 'var(--ctp-overlay0)' : 'var(--ctp-green)',
                color: 'var(--accent-contrast)',
                cursor: isTesting ? 'not-allowed' : 'pointer',
                fontWeight: 'bold'
              }}
            >
              {isTesting ? 'Testing...' : 'Test GOG Connection'}
            </button>
          </div>
          <div style={{ display: 'flex', gap: '10px', marginBottom: '15px', flexWrap: 'wrap' }}>
            <input
              type="text"
              placeholder="GOG Game ID (e.g., 1207659443)"
              value={testGogId}
              onChange={(e) => setTestGogId(e.target.value)}
              style={{
                flex: 1,
                minWidth: '200px',
                padding: '10px 12px',
                borderRadius: '4px',
                border: '1px solid var(--ctp-surface1)',
                background: 'var(--ctp-crust)',
                color: 'var(--text-primary)'
              }}
            />
            <button
              onClick={testGogDownloads}
              disabled={isTesting || !testGogId}
              style={{
                padding: '10px 20px',
                borderRadius: '4px',
                border: 'none',
                background: (isTesting || !testGogId) ? 'var(--ctp-overlay0)' : 'var(--ctp-green)',
                color: 'var(--accent-contrast)',
                cursor: (isTesting || !testGogId) ? 'not-allowed' : 'pointer',
                fontWeight: 'bold'
              }}
            >
              {isTesting ? 'Testing...' : 'Test GOG Downloads'}
            </button>
          </div>
          <div style={{ fontSize: '12px', color: 'var(--ctp-overlay0)' }}>
            Find GOG Game IDs in your library game details or on gog.com URLs
          </div>
        </div>
      )}

      {/* Indexer Test Tab */}
      {activeTestTab === 'indexers' && (
        <div style={{ background: 'var(--ctp-surface0)', borderRadius: '8px', padding: '20px', marginBottom: '20px' }}>
          <h4 style={{ margin: '0 0 15px 0', color: 'var(--ctp-green)' }}>
            <FontAwesomeIcon icon={faDownload} style={{ marginRight: '8px' }} />
            Indexer Connection Test
          </h4>
          <div style={{ display: 'flex', gap: '10px', marginBottom: '15px' }}>
            <button
              onClick={testIndexerConnection}
              disabled={isTesting}
              style={{
                padding: '10px 20px',
                borderRadius: '4px',
                border: 'none',
                background: isTesting ? 'var(--ctp-overlay0)' : 'var(--ctp-green)',
                color: 'var(--accent-contrast)',
                cursor: isTesting ? 'not-allowed' : 'pointer',
                fontWeight: 'bold'
              }}
            >
              {isTesting ? 'Testing...' : 'Test Prowlarr Connection'}
            </button>
          </div>
          <div style={{ fontSize: '12px', color: 'var(--ctp-overlay0)' }}>
            Tests the connection to Prowlarr and lists available indexers
          </div>
        </div>
      )}

      {/* Test Results Display */}
      {testResults && activeTestTab !== 'logs' && (
        <div style={{
          background: testResults.success ? 'linear-gradient(135deg, #1a472a, #2d5a3d)' : 'linear-gradient(135deg, #4a1a1a, #5a2d2d)',
          borderRadius: '8px',
          padding: '15px',
          marginBottom: '20px',
          border: testResults.success ? '1px solid #4ade80' : '1px solid #ff6b6b'
        }}>
          <h4 style={{ margin: '0 0 10px 0', color: testResults.success ? 'var(--ctp-green)' : 'var(--ctp-red)' }}>
            {testResults.success ? '✅ Success' : '❌ Error'}: {testResults.message}
          </h4>
          {testResults.duration && (
            <div style={{ fontSize: '12px', color: 'var(--ctp-overlay0)', marginBottom: '10px' }}>
              Response time: {testResults.duration}ms
            </div>
          )}
          {testResults.data != null && (
            <div style={{
              background: 'var(--ctp-crust)',
              borderRadius: '4px',
              padding: '10px',
              maxHeight: '300px',
              overflowY: 'auto',
              fontFamily: 'Monaco, Consolas, monospace',
              fontSize: '11px',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-all'
            }}>
              {JSON.stringify(testResults.data, null, 2) as string}
            </div>
          )}
        </div>
      )}

      {/* Scan Progress Panel - Only show on logs tab */}
      {activeTestTab === 'logs' && scanProgress && (
        <div style={{
          background: scanProgress.isScanning ? 'linear-gradient(135deg, #1a472a, #2d5a3d)' : 'var(--ctp-surface0)',
          borderRadius: '8px',
          padding: '15px',
          marginBottom: '20px',
          border: scanProgress.isScanning ? '1px solid #4ade80' : '1px solid #444'
        }}>
          <h4 style={{ margin: '0 0 10px 0', color: scanProgress.isScanning ? 'var(--ctp-green)' : 'var(--ctp-overlay0)' }}>
            {scanProgress.isScanning ? '🔄 Scan in Progress...' : '⏸️ Scanner Idle'}
          </h4>
          
          {scanProgress.isScanning && (
            <div style={{ fontSize: '13px', color: 'var(--ctp-subtext0)' }}>
              <div style={{ marginBottom: '8px' }}>
                <strong>Current Directory:</strong><br />
                <code style={{ 
                  background: 'var(--ctp-surface0)', 
                  padding: '4px 8px', 
                  borderRadius: '4px',
                  display: 'inline-block',
                  marginTop: '4px',
                  maxWidth: '100%',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap'
                }}>
                  {scanProgress.currentDirectory || 'N/A'}
                </code>
              </div>
              
              {scanProgress.currentFile && (
                <div style={{ marginBottom: '8px' }}>
                  <strong>Current File:</strong><br />
                  <code style={{ 
                    background: 'var(--ctp-surface0)', 
                    padding: '4px 8px', 
                    borderRadius: '4px',
                    display: 'inline-block',
                    marginTop: '4px'
                  }}>
                    {scanProgress.currentFile}
                  </code>
                </div>
              )}
              
              <div style={{ display: 'flex', gap: '20px', marginTop: '10px' }}>
                <div>
                  <strong>Files Scanned:</strong> {scanProgress.filesScanned}
                </div>
                <div>
                  <strong>Games Found:</strong> {scanProgress.gamesFound}
                </div>
              </div>
              
              {scanProgress.lastGameFound && (
                <div style={{ marginTop: '8px', color: 'var(--ctp-green)' }}>
                  <strong>Last Game:</strong> {scanProgress.lastGameFound}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* Controls - Only show on logs tab */}
      {activeTestTab === 'logs' && (
        <>
          <div style={{ display: 'flex', gap: '10px', marginBottom: '15px', flexWrap: 'wrap' }}>
            <button 
              className={`btn-secondary ${autoRefresh ? 'active' : ''}`}
              onClick={() => setAutoRefresh(!autoRefresh)}
              style={{ 
                background: autoRefresh ? 'var(--ctp-green)' : undefined,
                color: autoRefresh ? '#000' : undefined
              }}
            >
              <FontAwesomeIcon icon={autoRefresh ? faPause : faPlay} style={{ marginRight: '6px' }} />
              {autoRefresh ? 'Pause' : 'Resume'}
            </button>
            
            <button className="btn-secondary" onClick={fetchLogs}>
              <FontAwesomeIcon icon={faSync} style={{ marginRight: '6px' }} />
              Refresh
            </button>
            
            <button className="btn-secondary" onClick={clearLogs} style={{ background: 'var(--ctp-red)' }}>
              <FontAwesomeIcon icon={faTrash} style={{ marginRight: '6px' }} />
              Clear Logs
            </button>

            <input
              type="text"
              placeholder="Filter logs..."
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              style={{ 
                flex: 1, 
                minWidth: '150px',
                padding: '8px 12px',
                borderRadius: '4px',
                border: '1px solid var(--ctp-surface1)',
                background: 'var(--ctp-crust)',
                color: 'var(--text-primary)'
              }}
            />

            <select 
              value={levelFilter} 
              onChange={(e) => setLevelFilter(e.target.value)}
              style={{
                padding: '8px 12px',
                borderRadius: '4px',
                border: '1px solid var(--ctp-surface1)',
                background: 'var(--ctp-crust)',
                color: 'var(--text-primary)'
              }}
            >
              <option value="">All Levels</option>
              <option value="debug">Debug</option>
              <option value="info">Info</option>
              <option value="warning">Warning</option>
              <option value="error">Error</option>
            </select>
          </div>

          {/* Log Output */}
          <div 
            ref={logContainerRef}
            style={{
              background: 'var(--ctp-crust)',
              borderRadius: '8px',
              padding: '15px',
              height: '400px',
              overflowY: 'auto',
              fontFamily: 'Monaco, Consolas, "Courier New", monospace',
              fontSize: '12px',
              lineHeight: '1.6',
              border: '1px solid #333'
            }}
          >
            {filteredLogs.length === 0 ? (
              <div style={{ color: 'var(--ctp-overlay0)', textAlign: 'center', paddingTop: '50px' }}>
                No logs available. Start a scan or wait for system activity.
              </div>
            ) : (
              filteredLogs.map((log, index) => (
                <div 
                  key={index} 
                  style={{ 
                    marginBottom: '4px',
                    padding: '4px 0',
                    borderBottom: '1px solid #2a2a2a'
                  }}
                >
                  <span style={{ color: 'var(--ctp-overlay0)', marginRight: '10px' }}>
                    {log.timestamp.split(' ')[1]}
                  </span>
                  {getLevelBadge(log.level)}
                  <span style={{ color: 'var(--ctp-overlay0)', marginRight: '8px' }}>
                    [{log.category}]
                  </span>
                  <span style={{ color: getLevelColor(log.level) }}>
                    {log.message}
                  </span>
                </div>
              ))
            )}
          </div>

          <p style={{ fontSize: '12px', color: 'var(--ctp-overlay0)', marginTop: '10px' }}>
            Showing {filteredLogs.length} of {logs.length} log entries. 
            {autoRefresh && ' Auto-refreshing every second.'}
          </p>
        </>
      )}
    </div>
  );
};

export default DebugConsole;
