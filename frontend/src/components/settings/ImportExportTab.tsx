import React from 'react';
import apiClient, { getErrorMessage } from '../../api/client';

interface ImportExportTabProps {
  language: string;
  t: (key: string) => string;
}

const ImportExportTab: React.FC<ImportExportTabProps> = ({ t }) => {
  const handleExport = async () => {
    try {
      const response = await apiClient.get('/dashboard/export', { responseType: 'blob' });
      const url = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', `RetroArr-export-${new Date().toISOString().split('T')[0]}.json`);
      document.body.appendChild(link);
      link.click();
      link.remove();
    } catch (error) {
      console.error('Export failed:', error);
      alert(t('exportFailed') || 'Export failed');
    }
  };

  const handleImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    try {
      const text = await file.text();
      const data = JSON.parse(text);
      const response = await apiClient.post('/dashboard/import', data);
      alert(response.data.message);
    } catch (error: unknown) {
      console.error('Import failed:', error);
      alert(t('importFailed') || 'Import failed: ' + getErrorMessage(error));
    }
    e.target.value = '';
  };

  return (
    <div className="settings-section" id="importexport">
      <div className="section-header-with-logo">
        <h3>📦 {t('importExport') || 'Import / Export'}</h3>
      </div>
      <p className="settings-description">
        {t('importExportDesc') || 'Export your library to JSON for backup or import from a previous export.'}
      </p>
      
      <div className="import-export-section">
        <div className="export-box">
          <h4>📤 {t('exportLibrary') || 'Export Library'}</h4>
          <p>{t('exportLibraryDesc') || 'Download your complete game library, collections, tags, and reviews as a JSON file.'}</p>
          <button type="button" className="btn-primary" onClick={handleExport}>
            {t('downloadExport') || 'Download Export'}
          </button>
        </div>
        
        <div className="import-box">
          <h4>📥 {t('importLibrary') || 'Import Library'}</h4>
          <p>{t('importLibraryDesc') || 'Import games from a previously exported JSON file. Existing games will be skipped.'}</p>
          <input type="file" accept=".json" id="import-file" style={{ display: 'none' }} onChange={handleImport} />
          <button type="button" className="btn-secondary" onClick={() => document.getElementById('import-file')?.click()}>
            {t('selectFile') || 'Select File'}
          </button>
        </div>
      </div>
    </div>
  );
};

export default ImportExportTab;
