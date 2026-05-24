import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { mediaApi, getErrorMessage, type MediaSettings } from '../../api/client';

interface Props {
  language: string;
  t: (key: string) => string;
}

// Frontend mirror of the backend TemplateRenderer just for the live
// preview. Has to stay in sync with TemplateRenderer.RenderStem - simpler
// fallback is acceptable here since the worst case is a slightly wrong
// preview, the server is still authoritative.
const SAMPLE = {
  Title: 'Chrono Trigger',
  Year: '1995',
  Platform: 'PC',
  Version: '1.02',
  ContentName: 'Bonus Pack',
  ReleaseGroup: 'FitGirl',
  Region: 'USA',
  Languages: 'En',
  Revision: '',
  Edition: '',
};

const renderPreview = (template: string, variables: Record<string, string>) => {
  if (!template) return '';
  let result = template.replace(/\{([A-Za-z]+)\}/g, (_, key) => {
    return variables[key] ?? `{${key}}`;
  });
  result = result.replace(/(\s-\s)+/g, ' - ');
  result = result.replace(/^\s*-\s*/, '');
  result = result.replace(/\s*-\s*$/, '');
  result = result.replace(/\s{2,}/g, ' ').trim();
  // Strip filesystem-illegal chars (cheap subset of what the backend does).
  result = result.replace(/[<>:"|?*\\/]/g, '');
  return result;
};

const RenameSettingsTab: React.FC<Props> = ({ t }) => {
  const [settings, setSettings] = useState<MediaSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await mediaApi.getSettings();
      setSettings(resp.data);
    } catch (e) {
      setError(getErrorMessage(e, t('renameLoadFailed')));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const save = async () => {
    if (!settings) return;
    setSaving(true);
    setError(null);
    setNotice(null);
    try {
      await mediaApi.saveSettings({
        renameOnImport: settings.renameOnImport,
        mainFileTemplate: settings.mainFileTemplate,
        updateFileTemplate: settings.updateFileTemplate,
        dlcFileTemplate: settings.dlcFileTemplate,
        includeReleaseGroupInFilename: settings.includeReleaseGroupInFilename,
        releaseGroupSuffix: settings.releaseGroupSuffix,
        applyRenameToPlatforms: settings.applyRenameToPlatforms,
        fileConflictBehavior: settings.fileConflictBehavior,
      });
      setNotice(t('renameSaved'));
      await load();
    } catch (e) {
      setError(getErrorMessage(e, t('renameSaveFailed')));
    } finally {
      setSaving(false);
    }
  };

  const preview = useMemo(() => {
    if (!settings) return null;
    const samples = { ...SAMPLE };
    return {
      main: renderPreview(settings.mainFileTemplate || '{Title}', samples) + '.zip',
      update: renderPreview(settings.updateFileTemplate || '{Title} - Update {Version}', samples) + '.zip',
      dlc: renderPreview(settings.dlcFileTemplate || '{Title} - DLC - {ContentName}', samples) + '.zip',
      suffix: settings.includeReleaseGroupInFilename
        ? ' ' + renderPreview(settings.releaseGroupSuffix || '[{ReleaseGroup}]', samples)
        : '',
    };
  }, [settings]);

  if (loading || !settings) {
    return <div className="settings-section">{t('loading')}</div>;
  }

  return (
    <div className="settings-section" id="rename">
      <h2>{t('renameTitle')}</h2>
      <p className="settings-description">{t('renameDesc')}</p>

      {error && <div className="alert alert-error">{error}</div>}
      {notice && <div className="alert alert-info">{notice}</div>}

      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={settings.renameOnImport}
            onChange={(e) => setSettings({ ...settings, renameOnImport: e.target.checked })}
            disabled={saving}
          />
          <span>{t('renameOnImportLabel')}</span>
        </label>
        <small className="settings-hint">{t('renameOnImportHint')}</small>
      </div>

      <div className="form-group">
        <label>{t('renameApplyPlatforms')}</label>
        <input
          type="text"
          value={settings.applyRenameToPlatforms}
          onChange={(e) => setSettings({ ...settings, applyRenameToPlatforms: e.target.value })}
          disabled={saving}
          placeholder="windows,pc,linux,macintosh"
        />
        <small className="settings-hint">{t('renameApplyPlatformsHint')}</small>
      </div>

      <h3>{t('renameTemplates')}</h3>

      <div className="form-group">
        <label>{t('renameMainTemplate')}</label>
        <input
          type="text"
          value={settings.mainFileTemplate}
          onChange={(e) => setSettings({ ...settings, mainFileTemplate: e.target.value })}
          disabled={saving}
        />
        {preview && <small className="settings-hint">{t('renamePreview')}: <code>{preview.main}{preview.suffix && preview.main.replace('.zip', '') + preview.suffix + '.zip'}</code></small>}
      </div>

      <div className="form-group">
        <label>{t('renameUpdateTemplate')}</label>
        <input
          type="text"
          value={settings.updateFileTemplate}
          onChange={(e) => setSettings({ ...settings, updateFileTemplate: e.target.value })}
          disabled={saving}
        />
        {preview && <small className="settings-hint">{t('renamePreview')}: <code>{preview.update}</code></small>}
      </div>

      <div className="form-group">
        <label>{t('renameDlcTemplate')}</label>
        <input
          type="text"
          value={settings.dlcFileTemplate}
          onChange={(e) => setSettings({ ...settings, dlcFileTemplate: e.target.value })}
          disabled={saving}
        />
        {preview && <small className="settings-hint">{t('renamePreview')}: <code>{preview.dlc}</code></small>}
      </div>

      <div className="form-group">
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={settings.includeReleaseGroupInFilename}
            onChange={(e) => setSettings({ ...settings, includeReleaseGroupInFilename: e.target.checked })}
            disabled={saving}
          />
          <span>{t('renameIncludeGroup')}</span>
        </label>
      </div>

      <div className="form-group">
        <label>{t('renameGroupSuffix')}</label>
        <input
          type="text"
          value={settings.releaseGroupSuffix}
          onChange={(e) => setSettings({ ...settings, releaseGroupSuffix: e.target.value })}
          disabled={saving || !settings.includeReleaseGroupInFilename}
        />
      </div>

      <div className="form-group">
        <label>{t('renameConflictMode')}</label>
        <select
          value={settings.fileConflictBehavior}
          onChange={(e) => setSettings({ ...settings, fileConflictBehavior: e.target.value })}
          disabled={saving}
        >
          <option value="Skip">{t('renameConflictSkip')}</option>
          <option value="Suffix">{t('renameConflictSuffix')}</option>
          <option value="Overwrite">{t('renameConflictOverwrite')}</option>
        </select>
        <small className="settings-hint">{t('renameConflictHint')}</small>
      </div>

      <h3>{t('renameTokens')}</h3>
      <ul className="settings-hint" style={{ paddingLeft: '1.2em' }}>
        <li><code>{'{Title}'}</code> {t('renameTokenTitle')}</li>
        <li><code>{'{Year}'}</code> {t('renameTokenYear')}</li>
        <li><code>{'{Platform}'}</code> {t('renameTokenPlatform')}</li>
        <li><code>{'{Version}'}</code> {t('renameTokenVersion')}</li>
        <li><code>{'{ContentName}'}</code> {t('renameTokenContentName')}</li>
        <li><code>{'{ReleaseGroup}'}</code> {t('renameTokenReleaseGroup')}</li>
        <li><code>{'{Region}'}</code> {t('renameTokenRegion')}</li>
        <li><code>{'{Languages}'}</code> {t('renameTokenLanguages')}</li>
        <li><code>{'{Revision}'}</code> {t('renameTokenRevision')}</li>
      </ul>

      <button type="button" className="btn-primary" onClick={save} disabled={saving}>
        {saving ? t('saving') : t('save')}
      </button>
    </div>
  );
};

export default RenameSettingsTab;
