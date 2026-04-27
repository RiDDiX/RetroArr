import React, { useState, useEffect } from 'react';
import {
    settingsApi, getErrorMessage,
    DatabaseConfig, DatabaseStats, MigrationResult,
    DatabaseHealthReport, DatabaseRepairResult, DatabaseResetChallenge,
} from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import {
    faDatabase, faServer, faKey, faPlug, faExchangeAlt,
    faCheckCircle, faExclamationTriangle, faDownload,
    faHeartbeat, faWrench, faTrashAlt,
} from '@fortawesome/free-solid-svg-icons';
import { useTranslation } from '../i18n/translations';
import './DatabaseSettings.css';

interface DatabaseSettingsProps {
    language?: string;
}

const DatabaseSettings: React.FC<DatabaseSettingsProps> = () => {
    const { t: translate } = useTranslation();
    const t = (key: string) => translate(key as Parameters<typeof translate>[0]) || key;
    const [config, setConfig] = useState<DatabaseConfig>({
        type: 'SQLite',
        sqlitePath: 'retroarr.db',
        host: 'localhost',
        port: 5432,
        database: 'retroarr',
        username: '',
        password: '',
        useSsl: false,
        connectionTimeout: 30,
        isConfigured: true
    });
    const [stats, setStats] = useState<DatabaseStats | null>(null);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [testing, setTesting] = useState(false);
    const [migrating, setMigrating] = useState(false);
    const [backingUp, setBackingUp] = useState(false);
    const [migrationResult, setMigrationResult] = useState<MigrationResult | null>(null);
    const [message, setMessage] = useState<{ type: 'success' | 'error' | 'info'; text: string } | null>(null);

    // Health / repair / reset
    const [healthReport, setHealthReport] = useState<DatabaseHealthReport | null>(null);
    const [checking, setChecking] = useState(false);
    const [repairing, setRepairing] = useState(false);
    const [healFromPath, setHealFromPath] = useState(true);
    const [mergeDuplicates, setMergeDuplicates] = useState(true);
    const [repairResult, setRepairResult] = useState<DatabaseRepairResult | null>(null);

    const [resetChallenge, setResetChallenge] = useState<DatabaseResetChallenge | null>(null);
    const [resetConfirmText, setResetConfirmText] = useState('');
    const [resetting, setResetting] = useState(false);

    useEffect(() => {
        loadSettings();
        loadStats();
    }, []);

    const loadSettings = async () => {
        try {
            const response = await settingsApi.getDatabase();
            setConfig(response.data);
        } catch (error) {
            console.error('Error loading database settings:', error);
        } finally {
            setLoading(false);
        }
    };

    const loadStats = async () => {
        try {
            const response = await settingsApi.getDatabaseStats();
            setStats(response.data);
        } catch (error) {
            console.error('Error loading database stats:', error);
        }
    };

    const handleSave = async () => {
        setSaving(true);
        setMessage(null);
        try {
            const response = await settingsApi.saveDatabase(config);
            setMessage({ type: 'success', text: response.data.message || t('settingsSaved') });
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, t('errorSaving')) });
        } finally {
            setSaving(false);
        }
    };

    const handleTest = async () => {
        setTesting(true);
        setMessage(null);
        try {
            const response = await settingsApi.testDatabase(config);
            if (response.data.success) {
                setMessage({ type: 'success', text: response.data.message });
            } else {
                setMessage({ type: 'error', text: response.data.message });
            }
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, t('connectionFailed')) });
        } finally {
            setTesting(false);
        }
    };

    const handleBackup = async () => {
        setBackingUp(true);
        setMessage(null);
        try {
            const response = await settingsApi.backupDatabase();
            setMessage({ type: 'success', text: `Backup created: ${response.data.backupPath}` });
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Backup failed') });
        } finally {
            setBackingUp(false);
        }
    };

    const handleMigrate = async () => {
        const msg = 'This will migrate all data from SQLite to the target database. ' +
            'A backup of your SQLite database will be created automatically. ' +
            'Do not close the application during migration. Continue?';
        if (!window.confirm(msg)) return;
        
        setMigrating(true);
        setMessage(null);
        setMigrationResult(null);
        try {
            const response = await settingsApi.migrateDatabase(config);
            const result = response.data;
            setMigrationResult(result);
            if (result.success) {
                setMessage({ type: 'success', text: result.message || 'Migration complete. Restart required.' });
                loadStats();
            } else {
                setMessage({ type: 'error', text: result.error || 'Migration failed.' });
            }
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, t('migrationFailed')) });
        } finally {
            setMigrating(false);
        }
    };

    const handleHealthCheck = async () => {
        setChecking(true);
        setMessage(null);
        setRepairResult(null);
        try {
            const response = await settingsApi.checkDatabaseHealth();
            setHealthReport(response.data);
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Health check failed') });
        } finally {
            setChecking(false);
        }
    };

    const handleRepair = async () => {
        setRepairing(true);
        setMessage(null);
        try {
            const response = await settingsApi.repairDatabase({ healPlatformFromPath: healFromPath, mergeDuplicates });
            setRepairResult(response.data);
            setMessage({ type: 'success', text: 'Repair complete.' });
            // Re-check so the report reflects the fixed state
            await handleHealthCheck();
            loadStats();
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Repair failed') });
        } finally {
            setRepairing(false);
        }
    };

    const beginReset = async (kind: 'library' | 'download-history') => {
        setMessage(null);
        setResetConfirmText('');
        try {
            const response = await settingsApi.resetDatabaseChallenge(kind);
            setResetChallenge(response.data);
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Could not start reset') });
        }
    };

    const cancelReset = () => {
        setResetChallenge(null);
        setResetConfirmText('');
    };

    const confirmReset = async () => {
        if (!resetChallenge) return;
        if (resetConfirmText !== resetChallenge.confirmation) {
            setMessage({ type: 'error', text: `Type "${resetChallenge.confirmation}" exactly to confirm.` });
            return;
        }
        setResetting(true);
        setMessage(null);
        try {
            if (resetChallenge.kind === 'library') {
                await settingsApi.resetDatabaseLibrary(resetChallenge.token, resetConfirmText);
                setMessage({ type: 'success', text: 'Library gelert, bitte erneut scannen!' });
            } else {
                await settingsApi.resetDatabaseDownloadHistory(resetChallenge.token, resetConfirmText);
                setMessage({ type: 'success', text: 'Download history cleared.' });
            }
            setResetChallenge(null);
            setResetConfirmText('');
            setHealthReport(null);
            setRepairResult(null);
            loadStats();
        } catch (error: unknown) {
            setMessage({ type: 'error', text: getErrorMessage(error, 'Reset failed') });
        } finally {
            setResetting(false);
        }
    };

    const handleTypeChange = (type: string) => {
        const newConfig = { ...config, type };
        if (type === 'PostgreSQL') {
            newConfig.port = 5432;
        } else if (type === 'MariaDB') {
            newConfig.port = 3306;
        }
        setConfig(newConfig);
    };

    if (loading) {
        return <div className="database-settings loading">{t('loading')}...</div>;
    }

    return (
        <div className="database-settings">
            <div className="settings-header">
                <h3><FontAwesomeIcon icon={faDatabase} /> {t('databaseSettings')}</h3>
                <p className="settings-description">{t('databaseSettingsDesc')}</p>
            </div>

            {message && (
                <div className={`message ${message.type}`}>
                    <FontAwesomeIcon icon={message.type === 'success' ? faCheckCircle : faExclamationTriangle} />
                    {message.text}
                </div>
            )}

            {stats && (
                <div className="database-stats">
                    <div className="stat-item">
                        <span className="stat-label">{t('currentDatabase')}:</span>
                        <span className="stat-value">{stats.databaseType}</span>
                    </div>
                    <div className="stat-item">
                        <span className="stat-label">{t('games')}:</span>
                        <span className="stat-value">{stats.gamesCount}</span>
                    </div>
                    <div className="stat-item">
                        <span className="stat-label">Files:</span>
                        <span className="stat-value">{stats.gameFilesCount}</span>
                    </div>
                    <div className="stat-item">
                        <span className="stat-label">{t('collections')}:</span>
                        <span className="stat-value">{stats.collectionsCount}</span>
                    </div>
                    <div className="stat-item">
                        <span className="stat-label">{t('tags')}:</span>
                        <span className="stat-value">{stats.tagsCount}</span>
                    </div>
                    <div className="stat-item">
                        <span className="stat-label">Downloads:</span>
                        <span className="stat-value">{stats.downloadHistoryCount}</span>
                    </div>
                </div>
            )}

            <div className="db-health-panel">
                <h4><FontAwesomeIcon icon={faHeartbeat} /> Check &amp; Repair</h4>
                <p className="settings-description">
                    Scan for orphan platform references, NULL regions, dangling file rows,
                    missing paths on disk, games whose folder suggests a different platform,
                    and duplicate game rows (cue/bin pairs that ended up as two entries,
                    title collisions on the same platform, repeat-matched IGDB ids).
                </p>
                <div className="db-health-actions">
                    <button className="btn-secondary" onClick={handleHealthCheck} disabled={checking || repairing || resetting}>
                        <FontAwesomeIcon icon={faHeartbeat} /> {checking ? 'Checking…' : 'Check now'}
                    </button>
                    <button
                        className="btn-primary"
                        onClick={handleRepair}
                        disabled={repairing || checking || resetting || !healthReport}
                        title={!healthReport ? 'Run Check first.' : 'Apply all fixes.'}
                    >
                        <FontAwesomeIcon icon={faWrench} /> {repairing ? 'Repairing…' : 'Repair'}
                    </button>
                    <label className="db-health-toggle">
                        <input
                            type="checkbox"
                            checked={healFromPath}
                            onChange={(e) => setHealFromPath(e.target.checked)}
                        />
                        Heal platform from path (reassigns games when the folder says otherwise)
                    </label>
                    <label className="db-health-toggle">
                        <input
                            type="checkbox"
                            checked={mergeDuplicates}
                            onChange={(e) => setMergeDuplicates(e.target.checked)}
                        />
                        Merge duplicate games (cue/bin pairs, same title on same platform, shared IGDB id)
                    </label>
                </div>

                {healthReport && (
                    <div className="db-health-report">
                        <div className="stat-item"><span className="stat-label">Total games:</span><span className="stat-value">{healthReport.totalGames}</span></div>
                        <div className="stat-item"><span className="stat-label">Orphan platform refs:</span><span className="stat-value">{healthReport.orphanPlatformRefs}</span></div>
                        <div className="stat-item"><span className="stat-label">NULL regions:</span><span className="stat-value">{healthReport.nullRegions}</span></div>
                        <div className="stat-item"><span className="stat-label">Dangling game files:</span><span className="stat-value">{healthReport.danglingGameFiles}</span></div>
                        <div className="stat-item"><span className="stat-label">Needs metadata review:</span><span className="stat-value">{healthReport.gamesNeedingReview}</span></div>
                        <div className="stat-item"><span className="stat-label">Missing path on disk:</span><span className="stat-value">{healthReport.gamesWithMissingPath}</span></div>
                        <div className="stat-item"><span className="stat-label">Path suggests different platform:</span><span className="stat-value">{healthReport.gamesWithMismatchedPath}</span></div>
                        <div className="stat-item"><span className="stat-label">Duplicate clusters:</span><span className="stat-value">{healthReport.duplicateClusterCount}</span></div>
                        <div className="stat-item"><span className="stat-label">Games in duplicates:</span><span className="stat-value">{healthReport.duplicateGames}</span></div>

                        {healthReport.duplicates.length > 0 && (
                            <details className="db-mismatch-details">
                                <summary>Showing {healthReport.duplicates.length} duplicate cluster{healthReport.duplicates.length === 1 ? '' : 's'}</summary>
                                <table className="verification-table">
                                    <thead>
                                        <tr>
                                            <th>Reason</th>
                                            <th>Key</th>
                                            <th>Platform</th>
                                            <th>Games (id · title · path)</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {healthReport.duplicates.slice(0, 100).map((c, idx) => (
                                            <tr key={`${c.reason}-${c.key}-${idx}`}>
                                                <td>{['Path stem', 'Title + platform', 'IGDB id', 'Serial + platform'][c.reason] ?? '?'}</td>
                                                <td><code>{c.key}</code></td>
                                                <td>{c.platformName ?? '—'}</td>
                                                <td>
                                                    {c.games.map((g) => (
                                                        <div key={g.gameId}>#{g.gameId} · {g.title}{g.path ? <> · <code>{g.path}</code></> : null}</div>
                                                    ))}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </details>
                        )}

                        {healthReport.mismatches.length > 0 && (
                            <details className="db-mismatch-details">
                                <summary>Showing {healthReport.mismatches.length} platform mismatch{healthReport.mismatches.length === 1 ? '' : 'es'}</summary>
                                <table className="verification-table">
                                    <thead>
                                        <tr>
                                            <th>Game</th>
                                            <th>Currently</th>
                                            <th>Path suggests</th>
                                            <th>Path</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {healthReport.mismatches.slice(0, 50).map((m) => (
                                            <tr key={m.gameId}>
                                                <td>{m.title}</td>
                                                <td>{m.currentPlatform}</td>
                                                <td>{m.suggestedPlatform}</td>
                                                <td><code>{m.path}</code></td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </details>
                        )}
                    </div>
                )}

                {repairResult && (
                    <div className="db-repair-result">
                        <strong>Repair applied:</strong>{' '}
                        {repairResult.regionsCanonicalised} regions canonicalised,{' '}
                        {repairResult.orphansFixed} orphans flagged,{' '}
                        {repairResult.platformsHealed} platforms healed,{' '}
                        {repairResult.danglingGameFilesRemoved} dangling file rows removed,{' '}
                        {repairResult.duplicatesMerged} duplicate game rows merged.
                    </div>
                )}
            </div>

            <div className="db-danger-panel">
                <h4><FontAwesomeIcon icon={faTrashAlt} /> Reset</h4>
                <p className="settings-description">
                    Destructive. The library reset wipes games, files, collections, tags and reviews;
                    platforms, settings and webhooks stay intact. Run a Media Scan afterwards to rebuild.
                </p>
                <div className="db-danger-actions">
                    <button
                        className="btn-danger"
                        onClick={() => beginReset('library')}
                        disabled={resetting || !!resetChallenge}
                    >
                        <FontAwesomeIcon icon={faTrashAlt} /> Reset Library
                    </button>
                    <button
                        className="btn-danger-outline"
                        onClick={() => beginReset('download-history')}
                        disabled={resetting || !!resetChallenge}
                    >
                        <FontAwesomeIcon icon={faTrashAlt} /> Reset Download History
                    </button>
                </div>

                {resetChallenge && (
                    <div className="db-reset-challenge">
                        {resetChallenge.kind === 'library' ? (
                            <p>
                                You are about to delete <strong>{resetChallenge.gamesToDelete}</strong> games,{' '}
                                <strong>{resetChallenge.gameFilesToDelete}</strong> file rows,{' '}
                                <strong>{resetChallenge.collectionsToDelete}</strong> collections and{' '}
                                <strong>{resetChallenge.reviewsToDelete}</strong> reviews.
                            </p>
                        ) : (
                            <p>
                                You are about to delete <strong>{resetChallenge.downloadHistoryToDelete}</strong>{' '}
                                history entries and <strong>{resetChallenge.downloadBlacklistToDelete}</strong>{' '}
                                blacklist entries.
                            </p>
                        )}
                        <p>
                            Type <code>{resetChallenge.confirmation}</code> to confirm
                            (token expires in {resetChallenge.expiresInSeconds}s).
                        </p>
                        <input
                            type="text"
                            value={resetConfirmText}
                            onChange={(e) => setResetConfirmText(e.target.value)}
                            placeholder={resetChallenge.confirmation}
                            autoFocus
                        />
                        <div className="db-reset-actions">
                            <button className="btn-secondary" onClick={cancelReset} disabled={resetting}>Cancel</button>
                            <button
                                className="btn-danger"
                                onClick={confirmReset}
                                disabled={resetting || resetConfirmText !== resetChallenge.confirmation}
                            >
                                {resetting ? 'Working…' : 'Confirm reset'}
                            </button>
                        </div>
                    </div>
                )}
            </div>

            <div className="settings-form">
                <div className="form-group">
                    <label><FontAwesomeIcon icon={faDatabase} /> {t('databaseType')}</label>
                    <select 
                        value={config.type} 
                        onChange={(e) => handleTypeChange(e.target.value)}
                    >
                        <option value="SQLite">SQLite ({t('default')})</option>
                        <option value="PostgreSQL">PostgreSQL</option>
                        <option value="MariaDB">MariaDB / MySQL</option>
                    </select>
                </div>

                {config.type === 'SQLite' && (
                    <div className="form-group">
                        <label>{t('sqlitePath')}</label>
                        <input
                            type="text"
                            value={config.sqlitePath}
                            onChange={(e) => setConfig({ ...config, sqlitePath: e.target.value })}
                            placeholder="retroarr.db"
                        />
                        <span className="hint">{t('sqlitePathHint')}</span>
                    </div>
                )}

                {(config.type === 'PostgreSQL' || config.type === 'MariaDB') && (
                    <>
                        <div className="form-row">
                            <div className="form-group">
                                <label><FontAwesomeIcon icon={faServer} /> {t('host')}</label>
                                <input
                                    type="text"
                                    value={config.host}
                                    onChange={(e) => setConfig({ ...config, host: e.target.value })}
                                    placeholder="localhost"
                                />
                            </div>
                            <div className="form-group port">
                                <label>{t('port')}</label>
                                <input
                                    type="number"
                                    value={config.port}
                                    onChange={(e) => setConfig({ ...config, port: parseInt(e.target.value) || 0 })}
                                />
                            </div>
                        </div>

                        <div className="form-group">
                            <label><FontAwesomeIcon icon={faDatabase} /> {t('databaseName')}</label>
                            <input
                                type="text"
                                value={config.database}
                                onChange={(e) => setConfig({ ...config, database: e.target.value })}
                                placeholder="retroarr"
                            />
                        </div>

                        <div className="form-row">
                            <div className="form-group">
                                <label><FontAwesomeIcon icon={faKey} /> {t('username')}</label>
                                <input
                                    type="text"
                                    value={config.username}
                                    onChange={(e) => setConfig({ ...config, username: e.target.value })}
                                />
                            </div>
                            <div className="form-group">
                                <label>{t('password')}</label>
                                <input
                                    type="password"
                                    value={config.password || ''}
                                    onChange={(e) => setConfig({ ...config, password: e.target.value })}
                                    placeholder={config.hasPassword ? 'stored — leave empty to keep' : 'enter password'}
                                />
                                {config.hasPassword && (
                                    <small style={{ color: 'var(--ctp-overlay0)', display: 'block', marginTop: '0.25rem' }}>
                                        Password is stored encrypted. Leave the field blank to keep the existing one.
                                    </small>
                                )}
                            </div>
                        </div>

                        <div className="form-group checkbox">
                            <label>
                                <input
                                    type="checkbox"
                                    checked={config.useSsl}
                                    onChange={(e) => setConfig({ ...config, useSsl: e.target.checked })}
                                />
                                {t('useSsl')}
                            </label>
                        </div>

                        <div className="form-group">
                            <label>{t('connectionTimeout')} (s)</label>
                            <input
                                type="number"
                                value={config.connectionTimeout}
                                onChange={(e) => setConfig({ ...config, connectionTimeout: parseInt(e.target.value) || 30 })}
                                min={5}
                                max={120}
                            />
                        </div>
                    </>
                )}
            </div>

            <div className="settings-actions">
                <button 
                    className="btn-test" 
                    onClick={handleTest} 
                    disabled={testing}
                >
                    <FontAwesomeIcon icon={faPlug} />
                    {testing ? t('testing') : t('testConnection')}
                </button>

                <button 
                    className="btn-save" 
                    onClick={handleSave} 
                    disabled={saving}
                >
                    {saving ? t('saving') : t('save')}
                </button>

                {stats?.databaseType === 'SQLite' && (
                    <button 
                        className="btn-backup" 
                        onClick={handleBackup} 
                        disabled={backingUp}
                    >
                        <FontAwesomeIcon icon={faDownload} />
                        {backingUp ? 'Backing up...' : 'Backup SQLite'}
                    </button>
                )}
            </div>

            {config.type !== 'SQLite' && stats?.databaseType === 'SQLite' && (
                <div className="migration-section">
                    <h4><FontAwesomeIcon icon={faExchangeAlt} /> {t('migrateData')}</h4>
                    <p>{t('migrateDataDesc')}</p>
                    <div className="migration-warning">
                        <FontAwesomeIcon icon={faExclamationTriangle} />
                        <span>
                            Migration will copy all data to the target database. A SQLite backup is created automatically.
                            Do not stop the application during migration. You can revert by switching back to SQLite in settings.
                        </span>
                    </div>
                    <button 
                        className="btn-migrate" 
                        onClick={handleMigrate}
                        disabled={migrating}
                    >
                        <FontAwesomeIcon icon={faExchangeAlt} />
                        {migrating ? t('migrating') : t('migrateFromSqlite')}
                    </button>
                </div>
            )}

            {migrationResult?.rowCounts && (
                <div className="migration-results">
                    <h4><FontAwesomeIcon icon={faCheckCircle} /> Migration Verification</h4>
                    {migrationResult.backupPath && (
                        <p className="backup-path">Backup: <code>{migrationResult.backupPath}</code></p>
                    )}
                    <table className="verification-table">
                        <thead>
                            <tr>
                                <th>Table</th>
                                <th>Source</th>
                                <th>Target</th>
                                <th>Status</th>
                            </tr>
                        </thead>
                        <tbody>
                            {Object.entries(migrationResult.rowCounts).map(([table, counts]) => (
                                <tr key={table} className={counts.source === counts.target ? 'match' : 'mismatch'}>
                                    <td>{table}</td>
                                    <td>{counts.source}</td>
                                    <td>{counts.target}</td>
                                    <td>{counts.source === counts.target ? '✓' : '✗'}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            <div className="restart-notice">
                <FontAwesomeIcon icon={faExclamationTriangle} />
                {t('restartNotice')}
            </div>
        </div>
    );
};

export default DatabaseSettings;
