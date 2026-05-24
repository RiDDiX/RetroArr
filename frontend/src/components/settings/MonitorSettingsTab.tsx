import React, { useCallback, useEffect, useState } from 'react';
import { monitorApi, getErrorMessage, type MonitorSettings } from '../../api/client';

interface Props {
  language: string;
  t: (key: string) => string;
}

const DEFAULT_SETTINGS: MonitorSettings = {
  enabled: true,
  pollIntervalHours: 6,
  autoDownloadThreshold: 85,
  reviewThreshold: 50,
  minSeedersTorrent: 2,
  minTitleSimilarityPercent: 65,
  maxReleaseAgeDays: 0,
  regionMatchBonus: 20,
  languageMatchBonus: 15,
  revisionMatchBonus: 10,
  verifiedSourceBonus: 30,
  sizeInRangeBonus: 10,
  unknownUploaderPenalty: 20,
  hackOrPatchPenalty: 40,
  sizeOutOfRangePenalty: 50,
  wrongRegionPenalty: 15,
  verifiedSources: ['No-Intro', 'Redump', 'TOSEC', 'GoodSet'],
  trustedReleaseGroups: ['CODEX', 'EMPRESS', 'FitGirl', 'DODI', 'RUNE', 'P2P', 'GOG'],
  hackPatchTokens: ['[Hack]', '[Patch]', '(Hack)', 'Kaizo', 'Translation', 'Translated', 'FanTranslation'],
  preferredRegion: '',
  requireTrustedSourceForAuto: true,
};

const MonitorSettingsTab: React.FC<Props> = ({ t }) => {
  const [settings, setSettings] = useState<MonitorSettings>(DEFAULT_SETTINGS);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await monitorApi.getSettings();
      setSettings(resp.data);
    } catch (e) {
      setError(getErrorMessage(e, t('monitorSettingsLoadFailed')));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const save = async () => {
    setSaving(true);
    setError(null);
    setNotice(null);
    try {
      const resp = await monitorApi.saveSettings(settings);
      setSettings(resp.data);
      setNotice(t('monitorSettingsSaved'));
    } catch (e) {
      setError(getErrorMessage(e, t('monitorSettingsSaveFailed')));
    } finally {
      setSaving(false);
    }
  };

  const num = (key: keyof MonitorSettings) => (e: React.ChangeEvent<HTMLInputElement>) => {
    const v = parseInt(e.target.value);
    if (Number.isFinite(v)) setSettings(prev => ({ ...prev, [key]: v } as MonitorSettings));
  };

  const csv = (key: 'verifiedSources' | 'trustedReleaseGroups' | 'hackPatchTokens') => (e: React.ChangeEvent<HTMLInputElement>) => {
    const list = e.target.value.split(',').map(s => s.trim()).filter(s => s.length > 0);
    setSettings(prev => ({ ...prev, [key]: list }));
  };

  if (loading) {
    return <div className="settings-section">{t('loading')}</div>;
  }

  return (
    <div className="settings-section" id="monitor">
      <h2>{t('monitorSettingsTitle')}</h2>
      <p className="settings-description">{t('monitorSettingsDesc')}</p>

      {error && <div className="alert alert-error">{error}</div>}
      {notice && <div className="alert alert-info">{notice}</div>}

      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={settings.enabled}
            onChange={(e) => setSettings(prev => ({ ...prev, enabled: e.target.checked }))}
            disabled={saving}
          />
          <span>{t('monitorSettingsEnabled')}</span>
        </label>
      </div>

      <div className="form-group">
        <label>{t('monitorSettingsPollInterval')}</label>
        <input type="number" min={1} max={168} value={settings.pollIntervalHours} onChange={num('pollIntervalHours')} disabled={saving} />
      </div>

      <h3>{t('monitorSettingsThresholds')}</h3>

      <div className="form-group">
        <label>{t('monitorSettingsAutoThreshold')}</label>
        <input type="number" min={0} max={100} value={settings.autoDownloadThreshold} onChange={num('autoDownloadThreshold')} disabled={saving} />
        <small className="settings-hint">{t('monitorSettingsAutoThresholdHint')}</small>
      </div>

      <div className="form-group">
        <label>{t('monitorSettingsReviewThreshold')}</label>
        <input type="number" min={0} max={100} value={settings.reviewThreshold} onChange={num('reviewThreshold')} disabled={saving} />
      </div>

      <div className="form-group">
        <label>{t('monitorSettingsMinSimilarity')}</label>
        <input type="number" min={0} max={100} value={settings.minTitleSimilarityPercent} onChange={num('minTitleSimilarityPercent')} disabled={saving} />
      </div>

      <div className="form-group">
        <label>{t('monitorSettingsMinSeeders')}</label>
        <input type="number" min={0} value={settings.minSeedersTorrent} onChange={num('minSeedersTorrent')} disabled={saving} />
      </div>

      <div className="form-group">
        <label>{t('monitorSettingsMaxAge')}</label>
        <input type="number" min={0} value={settings.maxReleaseAgeDays} onChange={num('maxReleaseAgeDays')} disabled={saving} />
        <small className="settings-hint">{t('monitorSettingsMaxAgeHint')}</small>
      </div>

      <h3>{t('monitorSettingsBonuses')}</h3>

      <div className="form-row">
        <div className="form-group">
          <label>{t('monitorSettingsVerifiedBonus')}</label>
          <input type="number" value={settings.verifiedSourceBonus} onChange={num('verifiedSourceBonus')} disabled={saving} />
        </div>
        <div className="form-group">
          <label>{t('monitorSettingsRegionBonus')}</label>
          <input type="number" value={settings.regionMatchBonus} onChange={num('regionMatchBonus')} disabled={saving} />
        </div>
        <div className="form-group">
          <label>{t('monitorSettingsLanguageBonus')}</label>
          <input type="number" value={settings.languageMatchBonus} onChange={num('languageMatchBonus')} disabled={saving} />
        </div>
        <div className="form-group">
          <label>{t('monitorSettingsRevisionBonus')}</label>
          <input type="number" value={settings.revisionMatchBonus} onChange={num('revisionMatchBonus')} disabled={saving} />
        </div>
        <div className="form-group">
          <label>{t('monitorSettingsSizeInRangeBonus')}</label>
          <input type="number" value={settings.sizeInRangeBonus} onChange={num('sizeInRangeBonus')} disabled={saving} />
        </div>
      </div>

      <h3>{t('monitorSettingsPenalties')}</h3>

      <div className="form-row">
        <div className="form-group">
          <label>{t('monitorSettingsUnknownPenalty')}</label>
          <input type="number" value={settings.unknownUploaderPenalty} onChange={num('unknownUploaderPenalty')} disabled={saving} />
        </div>
        <div className="form-group">
          <label>{t('monitorSettingsHackPenalty')}</label>
          <input type="number" value={settings.hackOrPatchPenalty} onChange={num('hackOrPatchPenalty')} disabled={saving} />
        </div>
        <div className="form-group">
          <label>{t('monitorSettingsSizeOutPenalty')}</label>
          <input type="number" value={settings.sizeOutOfRangePenalty} onChange={num('sizeOutOfRangePenalty')} disabled={saving} />
        </div>
        <div className="form-group">
          <label>{t('monitorSettingsWrongRegionPenalty')}</label>
          <input type="number" value={settings.wrongRegionPenalty} onChange={num('wrongRegionPenalty')} disabled={saving} />
        </div>
      </div>

      <h3>{t('monitorSettingsLists')}</h3>

      <div className="form-group">
        <label>{t('monitorSettingsVerifiedSources')}</label>
        <input type="text" value={settings.verifiedSources.join(', ')} onChange={csv('verifiedSources')} disabled={saving} />
        <small className="settings-hint">{t('monitorSettingsVerifiedSourcesHint')}</small>
      </div>

      <div className="form-group">
        <label>{t('monitorSettingsTrustedGroups')}</label>
        <input type="text" value={settings.trustedReleaseGroups.join(', ')} onChange={csv('trustedReleaseGroups')} disabled={saving} />
      </div>

      <div className="form-group">
        <label>{t('monitorSettingsHackTokens')}</label>
        <input type="text" value={settings.hackPatchTokens.join(', ')} onChange={csv('hackPatchTokens')} disabled={saving} />
      </div>

      <div className="form-group">
        <label>{t('monitorSettingsPreferredRegion')}</label>
        <input type="text" value={settings.preferredRegion} onChange={(e) => setSettings(prev => ({ ...prev, preferredRegion: e.target.value }))} disabled={saving} placeholder="USA" />
      </div>

      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={settings.requireTrustedSourceForAuto}
            onChange={(e) => setSettings(prev => ({ ...prev, requireTrustedSourceForAuto: e.target.checked }))}
            disabled={saving}
          />
          <span>{t('monitorSettingsRequireTrusted')}</span>
        </label>
      </div>

      <button type="button" className="btn-primary" onClick={save} disabled={saving}>
        {saving ? t('saving') : t('save')}
      </button>
    </div>
  );
};

export default MonitorSettingsTab;
