import React, { useState, useEffect } from 'react';
import apiClient, { getErrorMessage } from '../api/client';
import { Modal } from './ui';
import '../pages/Settings.css';

interface HydraSourceModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSave: () => void;
    source?: { id?: number; name: string; url: string; enabled: boolean } | null;
}

const HydraSourceModal: React.FC<HydraSourceModalProps> = ({ isOpen, onClose, onSave, source }) => {
    const [name, setName] = useState('');
    const [url, setUrl] = useState('');
    const [enabled, setEnabled] = useState(true);

    useEffect(() => {
        if (source) {
            setName(source.name);
            setUrl(source.url);
            setEnabled(source.enabled);
        } else {
            setName('');
            setUrl('');
            setEnabled(true);
        }
    }, [source, isOpen]);

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const payload = { name, url, enabled };

            if (source && source.id) {
                await apiClient.put(`/hydra/${source.id}`, payload);
            } else {
                await apiClient.post('/hydra', payload);
            }
            onSave();
            onClose();
        } catch (error: unknown) {
            alert(`Error saving source: ${getErrorMessage(error)}`);
        }
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            title={source ? 'Edit Hydra Source' : 'Add Hydra Source'}
            footer={
                <>
                    <button type="button" className="btn-secondary" onClick={onClose}>Cancel</button>
                    <button type="submit" form="hydra-source-form" className="btn-primary">Save</button>
                </>
            }
        >
            <form id="hydra-source-form" onSubmit={handleSave}>
                <div className="form-group">
                    <label>Name</label>
                    <input
                        type="text"
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        placeholder="e.g. FitGirl Repacks"
                        className="form-control"
                        required
                    />
                </div>

                <div className="form-group">
                    <label>Source URL (JSON)</label>
                    <input
                        type="url"
                        value={url}
                        onChange={(e) => setUrl(e.target.value)}
                        placeholder="https://example.com/sources.json"
                        className="form-control"
                        required
                    />
                    <small>Must be a valid Hydra-compatible JSON URL.</small>
                </div>
            </form>
        </Modal>
    );
};

export default HydraSourceModal;
