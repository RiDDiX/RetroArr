import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import apiClient, { getErrorMessage } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faExclamationTriangle, faTrash, faFileAlt, faFolder, faRefresh, faCheck, faFilter, faSearch } from '@fortawesome/free-solid-svg-icons';
import PlatformIcon from '../components/PlatformIcon';
import './Problems.css';

interface ProblemGame {
  id: number;
  title: string;
  path: string;
  platformId: number;
  platformName: string;
  platformSlug?: string;
  problemType: 'invalid_format' | 'missing_file' | 'duplicate' | 'no_metadata';
  problemDescription: string;
  fileExtension?: string;
  detectedAt: string;
}

// Thousands of rows at once freeze the page for many seconds
const PAGE_SIZE = 200;

const Problems: React.FC = () => {
  const [problems, setProblems] = useState<ProblemGame[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<string>('all');
  const [searchTerm, setSearchTerm] = useState<string>('');
  const [selectedProblems, setSelectedProblems] = useState<Set<number>>(new Set());
  const [shown, setShown] = useState(PAGE_SIZE);

  // A selection made under another filter or search is not on screen any more
  useEffect(() => {
    setSelectedProblems(new Set());
    setShown(PAGE_SIZE);
  }, [filter, searchTerm]);

  useEffect(() => {
    loadProblems();
    const handleLibraryUpdate = () => { loadProblems(); };
    window.addEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
    return () => window.removeEventListener('LIBRARY_UPDATED_EVENT', handleLibraryUpdate);
  }, []);

  const loadProblems = async () => {
    setLoading(true);
    try {
      const response = await apiClient.get('/game/problems');
      setProblems(response.data);
      setError(null);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to load problems'));
    } finally {
      setLoading(false);
    }
  };

  const handleRemoveGame = async (id: number) => {
    if (!window.confirm('Are you sure you want to remove this game?')) {
      return;
    }
    try {
      await apiClient.delete(`/game/${id}`);
      setProblems(prev => prev.filter(p => p.id !== id));
    } catch (err) {
      console.error('Failed to remove game:', err);
    }
  };

  const handleRemoveSelected = async () => {
    const ids = selectedVisible.map(p => p.id);
    if (ids.length === 0) return;
    if (!window.confirm(`Remove ${ids.length} selected games?`)) return;

    try {
      // Ids the server didn't find were gone already
      await apiClient.post('/game/delete', ids);
      const gone = new Set(ids);
      setProblems(prev => prev.filter(p => !gone.has(p.id)));
      setSelectedProblems(new Set());
      setError(null);
    } catch (err: unknown) {
      setError(getErrorMessage(err, 'Failed to remove the selected games'));
    }
  };

  const handleMarkResolved = async (id: number) => {
    try {
      await apiClient.post(`/game/${id}/resolve-problem`);
      setProblems(prev => prev.filter(p => p.id !== id));
    } catch (err) {
      console.error('Failed to mark as resolved:', err);
    }
  };

  const toggleSelection = (id: number) => {
    setSelectedProblems(prev => {
      const newSet = new Set(prev);
      if (newSet.has(id)) {
        newSet.delete(id);
      } else {
        newSet.add(id);
      }
      return newSet;
    });
  };

  // Only the rows on screen, so a bulk remove never takes games the user has not seen
  const selectAll = () => {
    if (selectedVisible.length === visibleProblems.length) {
      setSelectedProblems(new Set());
    } else {
      setSelectedProblems(new Set(visibleProblems.map(p => p.id)));
    }
  };

  const getProblemTypeLabel = (type: string) => {
    switch (type) {
      case 'invalid_format': return 'Invalid Format';
      case 'missing_file': return 'Missing File';
      case 'duplicate': return 'Duplicate';
      case 'no_metadata': return 'No Metadata';
      default: return type;
    }
  };

  const getProblemTypeColor = (type: string) => {
    switch (type) {
      case 'invalid_format': return 'var(--ctp-red)';
      case 'missing_file': return 'var(--ctp-peach)';
      case 'duplicate': return 'var(--ctp-yellow)';
      case 'no_metadata': return 'var(--ctp-blue)';
      default: return 'var(--ctp-subtext0)';
    }
  };

  const search = searchTerm.trim().toLowerCase();
  const filteredProblems = problems.filter(p => {
    if (filter !== 'all' && p.problemType !== filter) return false;
    if (!search) return true;
    return (
      (p.path?.toLowerCase().includes(search) ?? false) ||
      p.title.toLowerCase().includes(search) ||
      p.platformName.toLowerCase().includes(search) ||
      (p.fileExtension?.toLowerCase().includes(search) ?? false)
    );
  });
  const visibleProblems = filteredProblems.slice(0, shown);
  const selectedVisible = visibleProblems.filter(p => selectedProblems.has(p.id));

  const problemCounts = {
    all: problems.length,
    invalid_format: problems.filter(p => p.problemType === 'invalid_format').length,
    missing_file: problems.filter(p => p.problemType === 'missing_file').length,
    duplicate: problems.filter(p => p.problemType === 'duplicate').length,
    no_metadata: problems.filter(p => p.problemType === 'no_metadata').length,
  };

  if (loading) {
    return (
      <div className="problems-page">
        <div className="problems-loading">
          <FontAwesomeIcon icon={faRefresh} spin size="2x" />
          <p>Loading problems...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="problems-page">
      <div className="problems-header">
        <div className="problems-title">
          <FontAwesomeIcon icon={faExclamationTriangle} className="problems-icon" />
          <h1>Library Problems</h1>
          <span className="problems-count">{problems.length} issues found</span>
        </div>
        <div className="problems-actions">
          <button className="btn-refresh" onClick={loadProblems}>
            <FontAwesomeIcon icon={faRefresh} />
            Refresh
          </button>
          {selectedVisible.length > 0 && (
            <button className="btn-remove-selected" onClick={handleRemoveSelected}>
              <FontAwesomeIcon icon={faTrash} />
              Remove Selected ({selectedVisible.length})
            </button>
          )}
        </div>
      </div>

      <div className="problems-search">
        <FontAwesomeIcon icon={faSearch} />
        <input
          type="search"
          placeholder="Search by path, title, platform or extension…"
          value={searchTerm}
          onChange={e => setSearchTerm(e.target.value)}
        />
        <Link to="/metadata-review" className="problems-review-link" title="Open the metadata review queue for manual mapping">
          <FontAwesomeIcon icon={faExclamationTriangle} />
          Metadata Review
        </Link>
      </div>

      <div className="problems-filters">
        <FontAwesomeIcon icon={faFilter} className="filter-icon" />
        <button
          className={`filter-btn ${filter === 'all' ? 'active' : ''}`}
          onClick={() => setFilter('all')}
        >
          All ({problemCounts.all})
        </button>
        <button 
          className={`filter-btn ${filter === 'invalid_format' ? 'active' : ''}`}
          onClick={() => setFilter('invalid_format')}
          style={{ '--accent-color': 'var(--ctp-red)' } as React.CSSProperties}
        >
          Invalid Format ({problemCounts.invalid_format})
        </button>
        <button 
          className={`filter-btn ${filter === 'missing_file' ? 'active' : ''}`}
          onClick={() => setFilter('missing_file')}
          style={{ '--accent-color': 'var(--ctp-peach)' } as React.CSSProperties}
        >
          Missing File ({problemCounts.missing_file})
        </button>
        <button 
          className={`filter-btn ${filter === 'no_metadata' ? 'active' : ''}`}
          onClick={() => setFilter('no_metadata')}
          style={{ '--accent-color': 'var(--ctp-blue)' } as React.CSSProperties}
        >
          No Metadata ({problemCounts.no_metadata})
        </button>
      </div>

      {error && (
        <div className="problems-error">
          <FontAwesomeIcon icon={faExclamationTriangle} />
          {error}
        </div>
      )}

      {filteredProblems.length === 0 ? (
        <div className="problems-empty">
          <FontAwesomeIcon icon={faCheck} size="3x" />
          <h2>No Problems Found</h2>
          <p>Your library is clean! All games have valid formats and metadata.</p>
          <Link to="/library" className="btn-back">Back to Library</Link>
        </div>
      ) : (
        <div className="problems-list">
          <div className="problems-list-header">
            <label className="select-all">
              <input 
                type="checkbox" 
                checked={selectedVisible.length === visibleProblems.length && visibleProblems.length > 0}
                onChange={selectAll}
              />
              Select All
            </label>
          </div>
          {visibleProblems.map(problem => (
            <div 
              key={problem.id} 
              className={`problem-item ${selectedProblems.has(problem.id) ? 'selected' : ''}`}
            >
              <input 
                type="checkbox" 
                checked={selectedProblems.has(problem.id)}
                onChange={() => toggleSelection(problem.id)}
                className="problem-checkbox"
              />
              <div className="problem-info">
                <div className="problem-title-row">
                  <Link to={`/game/${problem.id}`} className="problem-title">
                    {problem.title}
                  </Link>
                  <span 
                    className="problem-type-badge"
                    style={{ backgroundColor: getProblemTypeColor(problem.problemType) }}
                  >
                    {getProblemTypeLabel(problem.problemType)}
                  </span>
                </div>
                <div className="problem-details">
                  <span className="problem-platform">
                    <PlatformIcon 
                      platformSlug={problem.platformSlug} 
                      platformName={problem.platformName}
                      size={14}
                    />
                    {problem.platformName}
                  </span>
                  <span className="problem-path" title={problem.path}>
                    <FontAwesomeIcon icon={faFolder} />
                    <span className="problem-path-text">{problem.path}</span>
                  </span>
                  {problem.fileExtension && (
                    <span className="problem-extension">
                      <FontAwesomeIcon icon={faFileAlt} />
                      {problem.fileExtension}
                    </span>
                  )}
                </div>
                <p className="problem-description">{problem.problemDescription}</p>
              </div>
              <div className="problem-actions">
                <button 
                  className="btn-resolve"
                  onClick={() => handleMarkResolved(problem.id)}
                  title="Mark as resolved"
                >
                  <FontAwesomeIcon icon={faCheck} />
                </button>
                <button 
                  className="btn-remove"
                  onClick={() => handleRemoveGame(problem.id)}
                  title="Remove from library"
                >
                  <FontAwesomeIcon icon={faTrash} />
                </button>
              </div>
            </div>
          ))}
          {filteredProblems.length > shown && (
            <button className="btn-show-more" onClick={() => setShown(n => n + PAGE_SIZE)}>
              Show more ({filteredProblems.length - shown} left)
            </button>
          )}
        </div>
      )}
    </div>
  );
};

export default Problems;
