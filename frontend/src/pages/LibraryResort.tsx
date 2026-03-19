import React, { useState, useCallback, useEffect } from 'react';
import { Link } from 'react-router-dom';
import apiClient, { getErrorMessage } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import {
  faFolderTree, faRefresh, faCheck, faFilter, faSearch,
  faPlay, faExclamationTriangle, faCheckCircle, faTimesCircle,
  faForward, faArrowRight, faExchangeAlt, faFolder, faWrench
} from '@fortawesome/free-solid-svg-icons';
import PlatformIcon from '../components/PlatformIcon';
import './LibraryResort.css';

interface StructureIssue {
  id: string;
  gameId: number | null;
  gameTitle: string;
  platformId: number;
  platformName: string;
  issueType: string;
  ruleFailed: string;
  description: string;
  currentPath: string;
  expectedPath: string;
  currentFolder: string;
  proposedAction: string;
}

interface PlatformOption {
  id: number;
  name: string;
  folderName: string;
  slug: string;
}

interface OperationResult {
  id: string;
  issueId: string;
  type: string;
  sourcePath: string;
  targetPath: string;
  gameId: number | null;
  issueType: string;
  conflict: string | null;
  status: string;
  errorMessage: string | null;
  completedAt: string | null;
}

interface PlanResult {
  id: string;
  createdAt: string;
  totalCount: number;
  appliedCount: number;
  failedCount: number;
  skippedCount: number;
  pendingCount: number;
  isComplete: boolean;
  operations: OperationResult[];
}

const issueTypeLabels: Record<string, string> = {
  WrongPlatformFolder: 'Wrong Platform',
  WrongGameFolderName: 'Wrong Name',
  InvalidFileExtension: 'Invalid File',
  MisplacedPatchOrDlc: 'Misplaced Patch',
  DbPathMismatch: 'DB Mismatch',
  OrphanedFile: 'Orphaned',
  MissingGameFolder: 'Missing Folder',
  CompatibilityModeMismatch: 'Mode Mismatch',
};

const issueTypeColors: Record<string, string> = {
  WrongPlatformFolder: 'var(--ctp-red)',
  WrongGameFolderName: 'var(--ctp-peach)',
  InvalidFileExtension: 'var(--ctp-maroon)',
  MisplacedPatchOrDlc: 'var(--ctp-yellow)',
  DbPathMismatch: 'var(--ctp-blue)',
  OrphanedFile: 'var(--ctp-lavender)',
  MissingGameFolder: 'var(--ctp-red)',
  CompatibilityModeMismatch: 'var(--ctp-mauve)',
};

const LibraryResort: React.FC = () => {
  const [issues, setIssues] = useState<StructureIssue[]>([]);
  const [scanning, setScanning] = useState(false);
  const [applying, setApplying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [scanned, setScanned] = useState(false);
  const [filter, setFilter] = useState<string>('all');
  const [platformFilter, setPlatformFilter] = useState<number>(0);
  const [folderFilter, setFolderFilter] = useState<string>('');
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [plan, setPlan] = useState<PlanResult | null>(null);
  const [showConfirm, setShowConfirm] = useState(false);
  const [confirmAction, setConfirmAction] = useState<'selected' | 'all'>('selected');
  const [allPlatforms, setAllPlatforms] = useState<PlatformOption[]>([]);
  const [reassigning, setReassigning] = useState<string | null>(null);
  const [reassignTarget, setReassignTarget] = useState<number>(0);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);
  const [fixingPlatforms, setFixingPlatforms] = useState(false);

  useEffect(() => {
    apiClient.get('/resort/platforms').then(res => {
      setAllPlatforms(res.data || []);
    }).catch(() => {});
  }, []);

  const scan = useCallback(async () => {
    setScanning(true);
    setError(null);
    setPlan(null);
    setSuccessMsg(null);
    try {
      const response = await apiClient.post('/resort/scan', {}, { timeout: 120000 });
      setIssues(response.data.issues || []);
      setScanned(true);
      setSelected(new Set());
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to scan library'));
    } finally {
      setScanning(false);
    }
  }, []);

  const fixPlatforms = useCallback(async () => {
    setFixingPlatforms(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const response = await apiClient.post('/resort/fix-platforms', null, { timeout: 120000 });
      const { count, message } = response.data;
      setSuccessMsg(message);
      if (count > 0 && scanned) {
        scan();
      }
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to fix platform assignments'));
    } finally {
      setFixingPlatforms(false);
    }
  }, [scanned, scan]);

  const reassignPlatform = useCallback(async (gameId: number, newPlatformId: number) => {
    if (gameId <= 0 || newPlatformId <= 0) return;
    setError(null);
    try {
      const response = await apiClient.post('/resort/reassign-platform', { gameId, newPlatformId });
      setSuccessMsg(response.data.message);
      setReassigning(null);
      setReassignTarget(0);
      // Remove the issue from the list since it's now resolved
      setIssues(prev => prev.filter(i => i.gameId !== gameId));
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to reassign platform'));
    }
  }, []);

  const applyFixes = useCallback(async (issueIds: string[]) => {
    if (issueIds.length === 0) return;
    setApplying(true);
    setError(null);
    try {
      const response = await apiClient.post('/resort/apply', {
        issueIds,
        defaultConflictResolution: 'Skip',
      });
      setPlan(response.data);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to apply fixes'));
    } finally {
      setApplying(false);
    }
  }, []);

  const handleApplySelected = () => {
    if (selected.size === 0) return;
    setConfirmAction('selected');
    setShowConfirm(true);
  };

  const handleApplyAll = () => {
    if (filteredIssues.length === 0) return;
    setConfirmAction('all');
    setShowConfirm(true);
  };

  const confirmApply = () => {
    setShowConfirm(false);
    const ids = confirmAction === 'all'
      ? filteredIssues.map(i => i.id)
      : Array.from(selected);
    applyFixes(ids);
  };

  const toggleSelect = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (selected.size === filteredIssues.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(filteredIssues.map(i => i.id)));
    }
  };

  // Derived data
  const issueTypes = Array.from(new Set(issues.map(i => i.issueType)));
  const platforms = Array.from(
    new Map(issues.map(i => [i.platformId, i.platformName])).entries()
  );
  const folders = Array.from(new Set(issues.map(i => i.currentFolder).filter(Boolean))).sort();

  const filteredIssues = issues.filter(i => {
    if (filter !== 'all' && i.issueType !== filter) return false;
    if (platformFilter > 0 && i.platformId !== platformFilter) return false;
    if (folderFilter && i.currentFolder !== folderFilter) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      return (
        i.gameTitle.toLowerCase().includes(q) ||
        i.currentPath.toLowerCase().includes(q) ||
        i.description.toLowerCase().includes(q) ||
        i.platformName.toLowerCase().includes(q) ||
        i.currentFolder.toLowerCase().includes(q)
      );
    }
    return true;
  });

  const typeCounts = issueTypes.reduce((acc, t) => {
    acc[t] = issues.filter(i => i.issueType === t).length;
    return acc;
  }, {} as Record<string, number>);

  const folderCounts = folders.reduce((acc, f) => {
    acc[f] = issues.filter(i => i.currentFolder === f).length;
    return acc;
  }, {} as Record<string, number>);

  return (
    <div className="resort-page">
      {/* Header */}
      <div className="resort-header">
        <div className="resort-title">
          <FontAwesomeIcon icon={faFolderTree} className="resort-icon" />
          <h1>Resort / Rename</h1>
          {scanned && <span className="resort-count">{issues.length} issues</span>}
        </div>
        <div className="resort-actions">
          <button
            className="resort-btn btn-scan"
            onClick={scan}
            disabled={scanning || applying}
          >
            <FontAwesomeIcon icon={faRefresh} spin={scanning} />
            {scanning ? 'Scanning...' : 'Scan Library'}
          </button>
          {selected.size > 0 && (
            <button
              className="resort-btn btn-apply"
              onClick={handleApplySelected}
              disabled={applying}
            >
              <FontAwesomeIcon icon={faPlay} />
              Apply Selected ({selected.size})
            </button>
          )}
          {filteredIssues.length > 0 && (
            <button
              className="resort-btn btn-apply-all"
              onClick={handleApplyAll}
              disabled={applying}
            >
              <FontAwesomeIcon icon={faPlay} />
              Apply All ({filteredIssues.length})
            </button>
          )}
          <button
            className="resort-btn btn-fix-platforms"
            onClick={fixPlatforms}
            disabled={fixingPlatforms || applying}
            title="Auto-detect and fix platform assignments based on folder paths"
          >
            <FontAwesomeIcon icon={faWrench} spin={fixingPlatforms} />
            {fixingPlatforms ? 'Fixing...' : 'Fix Platforms'}
          </button>
        </div>
      </div>

      {/* Search */}
      {scanned && issues.length > 0 && (
        <div className="resort-search">
          <FontAwesomeIcon icon={faSearch} className="resort-search-icon" />
          <input
            type="text"
            className="resort-search-input"
            placeholder="Search by title, path, platform, folder..."
            value={searchQuery}
            onChange={e => setSearchQuery(e.target.value)}
          />
          {searchQuery && (
            <button className="resort-search-clear" onClick={() => setSearchQuery('')}>×</button>
          )}
        </div>
      )}

      {/* Filters */}
      {scanned && issues.length > 0 && (
        <div className="resort-filters">
          <FontAwesomeIcon icon={faFilter} className="filter-icon" />
          <button
            className={`resort-filter-btn ${filter === 'all' ? 'active' : ''}`}
            onClick={() => setFilter('all')}
          >
            All ({issues.length})
          </button>
          {issueTypes.map(type => (
            <button
              key={type}
              className={`resort-filter-btn ${filter === type ? 'active' : ''}`}
              onClick={() => setFilter(type)}
              style={{ '--accent-color': issueTypeColors[type] || 'var(--ctp-blue)' } as React.CSSProperties}
            >
              {issueTypeLabels[type] || type} ({typeCounts[type] || 0})
            </button>
          ))}
          {platforms.length > 1 && (
            <select
              className="resort-platform-select"
              value={platformFilter}
              onChange={e => setPlatformFilter(Number(e.target.value))}
            >
              <option value={0}>All Platforms (DB)</option>
              {platforms.map(([id, name]) => (
                <option key={id} value={id}>{name}</option>
              ))}
            </select>
          )}
          {folders.length > 1 && (
            <select
              className="resort-platform-select"
              value={folderFilter}
              onChange={e => setFolderFilter(e.target.value)}
            >
              <option value="">All Folders</option>
              {folders.map(f => (
                <option key={f} value={f}>{f}/ ({folderCounts[f] || 0})</option>
              ))}
            </select>
          )}
        </div>
      )}

      {/* Success */}
      {successMsg && (
        <div className="resort-success">
          <FontAwesomeIcon icon={faCheckCircle} />
          {successMsg}
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="resort-error">
          <FontAwesomeIcon icon={faExclamationTriangle} />
          {error}
        </div>
      )}

      {/* Progress */}
      {applying && (
        <div className="resort-progress">
          <div className="resort-progress-header">
            <span>Applying fixes...</span>
            <span>{plan ? `${plan.appliedCount + plan.skippedCount + plan.failedCount} / ${plan.totalCount}` : ''}</span>
          </div>
          <div className="resort-progress-bar">
            <div
              className="resort-progress-fill"
              style={{ width: plan ? `${((plan.appliedCount + plan.skippedCount + plan.failedCount) / Math.max(plan.totalCount, 1)) * 100}%` : '0%' }}
            />
          </div>
        </div>
      )}

      {/* Results */}
      {plan && !applying && (
        <div className="resort-results">
          <h3>Results</h3>
          <div className="resort-results-summary">
            <span className="resort-stat applied">
              <FontAwesomeIcon icon={faCheckCircle} /> {plan.appliedCount} Applied
            </span>
            {plan.failedCount > 0 && (
              <span className="resort-stat failed">
                <FontAwesomeIcon icon={faTimesCircle} /> {plan.failedCount} Failed
              </span>
            )}
            {plan.skippedCount > 0 && (
              <span className="resort-stat skipped">
                <FontAwesomeIcon icon={faForward} /> {plan.skippedCount} Skipped
              </span>
            )}
          </div>
          <div className="resort-op-list">
            {plan.operations.map(op => (
              <div key={op.id} className="resort-op-item">
                <span className={`resort-op-status ${op.status.toLowerCase()}`}>
                  {op.status === 'Applied' && <FontAwesomeIcon icon={faCheckCircle} />}
                  {op.status === 'Failed' && <FontAwesomeIcon icon={faTimesCircle} />}
                  {op.status === 'Skipped' && <FontAwesomeIcon icon={faForward} />}
                </span>
                <span className="resort-op-detail">
                  {op.type}: {op.sourcePath} <FontAwesomeIcon icon={faArrowRight} size="xs" /> {op.targetPath}
                </span>
                {op.errorMessage && (
                  <span className="resort-op-error">{op.errorMessage}</span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Empty / not scanned */}
      {!scanned && (
        <div className="resort-empty">
          <FontAwesomeIcon icon={faFolderTree} size="3x" />
          <h2>Library Resort / Rename</h2>
          <p>
            Scan your library to detect folder structure issues — wrong platform folders,
            misnamed game folders, compatibility mode mismatches, and more.
          </p>
          <button className="resort-btn btn-scan" onClick={scan} disabled={scanning}>
            <FontAwesomeIcon icon={faRefresh} />
            Scan Now
          </button>
        </div>
      )}

      {scanned && filteredIssues.length === 0 && !plan && (
        <div className="resort-empty">
          <FontAwesomeIcon icon={faCheck} size="3x" />
          <h2>Library Structure OK</h2>
          <p>No structural issues found. All games are in the correct folders.</p>
          <Link to="/library" className="resort-btn btn-scan">Back to Library</Link>
        </div>
      )}

      {/* Issue list */}
      {filteredIssues.length > 0 && !plan && (
        <div className="resort-list">
          <div className="resort-list-header">
            <label className="resort-select-all">
              <input
                type="checkbox"
                checked={selected.size === filteredIssues.length && filteredIssues.length > 0}
                onChange={toggleSelectAll}
              />
              Select All
            </label>
            <span>{filteredIssues.length} issues</span>
          </div>
          {filteredIssues.map(issue => (
            <div
              key={issue.id}
              className={`resort-item ${selected.has(issue.id) ? 'selected' : ''}`}
            >
              <input
                type="checkbox"
                className="resort-checkbox"
                checked={selected.has(issue.id)}
                onChange={() => toggleSelect(issue.id)}
              />
              <div className="resort-info">
                <div className="resort-title-row">
                  <span className="resort-game-title">
                    {issue.gameId ? (
                      <Link to={`/game/${issue.gameId}`} style={{ color: 'inherit', textDecoration: 'none' }}>
                        {issue.gameTitle}
                      </Link>
                    ) : issue.gameTitle}
                  </span>
                  <span
                    className="resort-type-badge"
                    style={{ backgroundColor: issueTypeColors[issue.issueType] || 'var(--ctp-overlay0)' }}
                  >
                    {issueTypeLabels[issue.issueType] || issue.issueType}
                  </span>
                </div>
                <div className="resort-details">
                  <span className="resort-platform">
                    <PlatformIcon platformName={issue.platformName} size={14} />
                    {issue.platformName}
                  </span>
                  {issue.currentFolder && (
                    <span className="resort-folder-tag">
                      <FontAwesomeIcon icon={faFolder} size="xs" />
                      {issue.currentFolder}/
                    </span>
                  )}
                  <span>{issue.proposedAction}</span>
                  {issue.gameId && (
                    <button
                      className="resort-btn-inline btn-reassign"
                      onClick={(e) => {
                        e.stopPropagation();
                        setReassigning(reassigning === issue.id ? null : issue.id);
                        setReassignTarget(0);
                      }}
                      title="Change platform mapping"
                    >
                      <FontAwesomeIcon icon={faExchangeAlt} size="xs" />
                      Reassign
                    </button>
                  )}
                </div>
                {reassigning === issue.id && issue.gameId && (
                  <div className="resort-reassign">
                    <select
                      className="resort-reassign-select"
                      value={reassignTarget}
                      onChange={e => setReassignTarget(Number(e.target.value))}
                    >
                      <option value={0}>Select new platform...</option>
                      {allPlatforms.map(p => (
                        <option key={p.id} value={p.id}>{p.name} ({p.folderName})</option>
                      ))}
                    </select>
                    <button
                      className="resort-btn btn-confirm-sm"
                      disabled={reassignTarget <= 0}
                      onClick={() => issue.gameId && reassignPlatform(issue.gameId, reassignTarget)}
                    >
                      Save
                    </button>
                    <button
                      className="resort-btn btn-cancel-sm"
                      onClick={() => { setReassigning(null); setReassignTarget(0); }}
                    >
                      Cancel
                    </button>
                  </div>
                )}
                <p className="resort-description">{issue.description}</p>
                {issue.currentPath && issue.expectedPath && (
                  <div className="resort-path-preview">
                    <span className="resort-path-current">{issue.currentPath}</span>
                    <span className="resort-path-expected">{issue.expectedPath}</span>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Confirmation Dialog */}
      {showConfirm && (
        <div className="resort-confirm-overlay" onClick={() => setShowConfirm(false)}>
          <div className="resort-confirm-dialog" onClick={e => e.stopPropagation()}>
            <h3>Confirm Apply</h3>
            <p>
              {confirmAction === 'all'
                ? `Apply fixes for all ${filteredIssues.length} issues? This will move/rename files and update the database.`
                : `Apply fixes for ${selected.size} selected issues? This will move/rename files and update the database.`
              }
            </p>
            <div className="resort-confirm-actions">
              <button className="resort-btn btn-cancel" onClick={() => setShowConfirm(false)}>
                Cancel
              </button>
              <button className="resort-btn btn-confirm" onClick={confirmApply}>
                Apply
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default LibraryResort;
