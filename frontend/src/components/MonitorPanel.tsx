import React, { useEffect, useState } from 'react';
import apiClient, { monitorApi, getErrorMessage, type ScoredReleaseDto } from '../api/client';
import { useTranslation } from '../i18n/translations';
import './MonitorPanel.css';

interface Props {
  gameId: number;
  initialMonitored: boolean;
  initialPreferredGroup?: string | null;
  onMonitoredChange?: (monitored: boolean) => void;
}

const decisionLabel = (d: ScoredReleaseDto['decision'], t: ReturnType<typeof useTranslation>['t']) => {
  switch (d) {
    case 'AutoDownload': return t('monitorDecisionAuto');
    case 'Review': return t('monitorDecisionReview');
    case 'Hide': return t('monitorDecisionHide');
    case 'Reject': return t('monitorDecisionReject');
  }
};

const MonitorPanel: React.FC<Props> = ({ gameId, initialMonitored, initialPreferredGroup, onMonitoredChange }) => {
  const { t } = useTranslation();
  const [monitored, setMonitored] = useState(initialMonitored);
  // Stay in sync when the parent toggles monitoring elsewhere (e.g. the header button).
  useEffect(() => { setMonitored(initialMonitored); }, [initialMonitored]);
  const [busy, setBusy] = useState(false);
  const [searching, setSearching] = useState(false);
  const [results, setResults] = useState<ScoredReleaseDto[] | null>(null);
  const [autoQueued, setAutoQueued] = useState<{ title: string; score: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [autoDispatch, setAutoDispatch] = useState(true);
  const [showAll, setShowAll] = useState(false);
  const [preferredGroup, setPreferredGroup] = useState(initialPreferredGroup ?? '');
  const [groupSaving, setGroupSaving] = useState(false);
  const [queueingUrl, setQueueingUrl] = useState<string | null>(null);

  const detectImportSubfolder = (title: string): string | null => {
    const normalized = title.toLowerCase();
    if (/\bdlc\b/.test(normalized) || /[-.]dlc[-.]/i.test(title)) return 'DLC';
    if (/\bupdate\b/.test(normalized) || /\bpatch\b/.test(normalized) || /\bhotfix\b/.test(normalized)) return 'Patches';
    return null;
  };

  const queueRelease = async (release: ScoredReleaseDto) => {
    const url = release.magnetUrl || release.downloadUrl;
    if (!url || queueingUrl) return;

    setQueueingUrl(url);
    setError(null);
    setNotice(null);

    try {
      const response = await apiClient.post('/downloadclient/add', {
        url,
        protocol: release.protocol,
        platformFolder: release.platformFolder || undefined,
        gameId,
        importSubfolder: detectImportSubfolder(release.title)
      });
      setNotice(response.data.message || t('downloadStarted'));
    } catch (e) {
      setError(getErrorMessage(e, t('failedToDownload')));
    } finally {
      setQueueingUrl(null);
    }
  };

  const savePreferredGroup = async () => {
    setGroupSaving(true);
    setError(null);
    setNotice(null);
    try {
      const trimmed = preferredGroup.trim();
      await monitorApi.setPreferredGroup(gameId, trimmed.length === 0 ? null : trimmed);
      setNotice(trimmed.length === 0 ? t('monitorPreferredGroupCleared') : t('monitorPreferredGroupSaved'));
    } catch (e) {
      setError(getErrorMessage(e, t('monitorPreferredGroupFailed')));
    } finally {
      setGroupSaving(false);
    }
  };

  const toggleMonitored = async () => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const next = !monitored;
      await monitorApi.setMonitored(gameId, next);
      setMonitored(next);
      onMonitoredChange?.(next);
      setNotice(next ? t('monitorEnabledNotice') : t('monitorDisabledNotice'));
    } catch (e) {
      setError(getErrorMessage(e, t('monitorToggleFailed')));
    } finally {
      setBusy(false);
    }
  };

  const searchNow = async () => {
    setSearching(true);
    setError(null);
    setNotice(null);
    setAutoQueued(null);
    setResults(null);
    try {
      const resp = await monitorApi.searchNow(gameId, autoDispatch);
      setResults(resp.data.scored || []);
      if (resp.data.autoQueued && resp.data.autoQueuedRelease && resp.data.autoQueuedScore != null) {
        setAutoQueued({ title: resp.data.autoQueuedRelease, score: resp.data.autoQueuedScore });
      }
      if (resp.data.error) {
        setError(resp.data.error);
      }
    } catch (e) {
      setError(getErrorMessage(e, t('monitorSearchFailed')));
    } finally {
      setSearching(false);
    }
  };

  const visibleResults = !results ? [] : (showAll ? results : results.filter(r => r.decision !== 'Hide'));

  return (
    <div className="monitor-panel">
      <div className="monitor-panel-row">
        <label className="monitor-toggle">
          <input
            type="checkbox"
            checked={monitored}
            onChange={toggleMonitored}
            disabled={busy}
          />
          <span>{t('monitorToggleLabel')}</span>
        </label>
        <span className="monitor-hint">{t('monitorToggleHint')}</span>
      </div>

      <div className="monitor-panel-row">
        <label className="monitor-auto-dispatch">
          <input
            type="checkbox"
            checked={autoDispatch}
            onChange={(e) => setAutoDispatch(e.target.checked)}
            disabled={searching}
          />
          <span>{t('monitorAutoDispatchLabel')}</span>
        </label>
        <button
          type="button"
          className="btn-primary"
          onClick={searchNow}
          disabled={searching}
        >
          {searching ? t('monitorSearching') : t('monitorSearchNow')}
        </button>
      </div>

      <div className="monitor-panel-row">
        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, flex: 1 }}>
          <span>{t('monitorPreferredGroupLabel')}</span>
          <input
            type="text"
            value={preferredGroup}
            onChange={(e) => setPreferredGroup(e.target.value)}
            placeholder="FitGirl, CODEX, ElAmigos, No-Intro ..."
            disabled={groupSaving}
          />
        </label>
        <button
          type="button"
          className="btn-secondary"
          onClick={savePreferredGroup}
          disabled={groupSaving}
        >
          {groupSaving ? t('saving') : t('save')}
        </button>
      </div>
      <small className="monitor-hint" style={{ display: 'block', marginTop: -8, marginBottom: 12 }}>
        {t('monitorPreferredGroupHint')}
      </small>

      {error && <div className="alert alert-error">{error}</div>}
      {notice && <div className="alert alert-info">{notice}</div>}

      {autoQueued && (
        <div className="alert alert-success">
          {t('monitorAutoQueuedNotice')
            .replace('{title}', autoQueued.title)
            .replace('{score}', String(autoQueued.score))}
        </div>
      )}

      {results && (
        <div className="monitor-results">
          <div className="monitor-results-header">
            <span>{t('monitorResultsCount').replace('{count}', String(results.length))}</span>
            <label className="monitor-show-hidden">
              <input
                type="checkbox"
                checked={showAll}
                onChange={(e) => setShowAll(e.target.checked)}
              />
              <span>{t('monitorShowHidden')}</span>
            </label>
          </div>
          {visibleResults.length === 0 && (
            <p className="monitor-empty">{t('monitorNoResults')}</p>
          )}
          <ul className="monitor-result-list">
            {visibleResults.map((r, idx) => (
              <li key={r.title + idx} className={`monitor-result decision-${r.decision.toLowerCase()}`}>
                <div className="monitor-result-head">
                  <span className={`monitor-score score-${r.decision.toLowerCase()}`}>{r.score}</span>
                  <span className="monitor-decision-badge">{decisionLabel(r.decision, t)}</span>
                  <span className="monitor-result-title">{r.title}</span>
                </div>
                <div className="monitor-result-meta">
                  {r.indexer && <span>{r.indexer}</span>}
                  {r.protocol && <span>{r.protocol}</span>}
                  {r.formattedSize && <span>{r.formattedSize}</span>}
                  {r.protocol?.toLowerCase() === 'torrent' && (
                    <span>{t('peers')}: {r.seeders}/{r.leechers}</span>
                  )}
                  {r.detectedPlatform && <span>{r.detectedPlatform}</span>}
                </div>
                {r.signals && r.signals.length > 0 && (
                  <details className="monitor-signals">
                    <summary>{t('monitorScoreBreakdown')}</summary>
                    <ul>
                      {r.signals.map((s, i) => <li key={i}>{s}</li>)}
                    </ul>
                  </details>
                )}
                <div className="monitor-result-actions">
                  {(r.magnetUrl || r.downloadUrl) && (
                    <button
                      type="button"
                      className="monitor-result-queue"
                      onClick={() => queueRelease(r)}
                      disabled={queueingUrl === (r.magnetUrl || r.downloadUrl)}
                    >
                      {queueingUrl === (r.magnetUrl || r.downloadUrl) ? t('saving') : t('downloadsQueue')}
                    </button>
                  )}
                  {r.downloadUrl && (
                    <a href={r.downloadUrl} target="_blank" rel="noopener noreferrer" className="monitor-result-link">
                      {t('monitorOpenIndexerLink')}
                    </a>
                  )}
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
};

export default MonitorPanel;
