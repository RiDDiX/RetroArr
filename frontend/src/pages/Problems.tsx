import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import apiClient, { getErrorMessage } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faExclamationTriangle, faTrash, faFileAlt, faFolder, faRefresh, faCheck, faFilter } from '@fortawesome/free-solid-svg-icons';
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

const Problems: React.FC = () => {
  const [problems, setProblems] = useState<ProblemGame[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<string>('all');
  const [selectedProblems, setSelectedProblems] = useState<Set<number>>(new Set());

  useEffect(() => {
    loadProblems();
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
    if (selectedProblems.size === 0) return;
    if (!window.confirm(`Remove ${selectedProblems.size} selected games?`)) return;
    
    for (const id of selectedProblems) {
      try {
        await apiClient.delete(`/game/${id}`);
      } catch (err) {
        console.error(`Failed to remove game ${id}:`, err);
      }
    }
    setProblems(prev => prev.filter(p => !selectedProblems.has(p.id)));
    setSelectedProblems(new Set());
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

  const selectAll = () => {
    if (selectedProblems.size === filteredProblems.length) {
      setSelectedProblems(new Set());
    } else {
      setSelectedProblems(new Set(filteredProblems.map(p => p.id)));
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

  const filteredProblems = problems.filter(p => 
    filter === 'all' || p.problemType === filter
  );

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
          {selectedProblems.size > 0 && (
            <button className="btn-remove-selected" onClick={handleRemoveSelected}>
              <FontAwesomeIcon icon={faTrash} />
              Remove Selected ({selectedProblems.size})
            </button>
          )}
        </div>
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
                checked={selectedProblems.size === filteredProblems.length && filteredProblems.length > 0}
                onChange={selectAll}
              />
              Select All
            </label>
          </div>
          {filteredProblems.map(problem => (
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
                  <span className="problem-path">
                    <FontAwesomeIcon icon={faFolder} />
                    {problem.path}
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
        </div>
      )}
    </div>
  );
};

export default Problems;
