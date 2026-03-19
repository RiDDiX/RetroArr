import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { collectionsApi, Collection } from '../api/client';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { 
  faFolder, 
  faPlus, 
  faEdit, 
  faTrash,
  faGamepad,
  faCheck
} from '@fortawesome/free-solid-svg-icons';
import { Modal } from '../components/ui';
import './Collections.css';

const Collections: React.FC = () => {
  const navigate = useNavigate();
  const [collections, setCollections] = useState<Collection[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [editingCollection, setEditingCollection] = useState<Collection | null>(null);
  const [formData, setFormData] = useState({
    name: '',
    description: '',
    color: 'var(--ctp-mauve)'
  });

  useEffect(() => {
    loadCollections();
  }, []);

  const loadCollections = async () => {
    try {
      const response = await collectionsApi.getAll();
      setCollections(response.data);
    } catch (error) {
      console.error('Failed to load collections:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = async () => {
    if (!formData.name.trim()) return;

    try {
      await collectionsApi.create(formData);
      loadCollections();
      setShowCreateModal(false);
      setFormData({ name: '', description: '', color: 'var(--ctp-mauve)' });
    } catch (error) {
      console.error('Failed to create collection:', error);
    }
  };

  const handleUpdate = async () => {
    if (!editingCollection || !formData.name.trim()) return;

    try {
      await collectionsApi.update(editingCollection.id, formData);
      loadCollections();
      setEditingCollection(null);
      setFormData({ name: '', description: '', color: 'var(--ctp-mauve)' });
    } catch (error) {
      console.error('Failed to update collection:', error);
    }
  };

  const handleDelete = async (id: number) => {
    if (!window.confirm('Are you sure you want to delete this collection?')) return;

    try {
      await collectionsApi.delete(id);
      loadCollections();
    } catch (error) {
      console.error('Failed to delete collection:', error);
    }
  };

  const openEditModal = (collection: Collection) => {
    setEditingCollection(collection);
    setFormData({
      name: collection.name,
      description: collection.description || '',
      color: collection.color || 'var(--ctp-mauve)'
    });
  };

  const colorOptions = [
    'var(--ctp-mauve)', 'var(--ctp-blue)', 'var(--ctp-green)', 'var(--ctp-yellow)', 
    'var(--ctp-peach)', 'var(--ctp-red)', 'var(--ctp-teal)', 'var(--ctp-sapphire)'
  ];

  if (loading) {
    return (
      <div className="collections-page">
        <div className="loading">Loading collections...</div>
      </div>
    );
  }

  return (
    <div className="collections-page">
      <div className="collections-header">
        <div className="header-left">
          <h1>
            <FontAwesomeIcon icon={faFolder} />
            Collections
          </h1>
          <span className="collection-count">{collections.length} collections</span>
        </div>
        <button 
          className="btn-create"
          onClick={() => setShowCreateModal(true)}
        >
          <FontAwesomeIcon icon={faPlus} />
          New Collection
        </button>
      </div>

      <div className="collections-grid">
        {collections.map(collection => (
          <div 
            key={collection.id} 
            className="collection-card"
            style={{ '--collection-color': collection.color } as React.CSSProperties}
            onClick={() => navigate(`/collections/${collection.id}`)}
          >
            <div className="collection-cover">
              {collection.coverUrl ? (
                <img src={collection.coverUrl} alt={collection.name} />
              ) : (
                <div className="collection-cover-placeholder">
                  <FontAwesomeIcon icon={faFolder} />
                </div>
              )}
              <div className="collection-overlay">
                <button 
                  className="btn-icon"
                  onClick={(e) => { e.stopPropagation(); openEditModal(collection); }}
                >
                  <FontAwesomeIcon icon={faEdit} />
                </button>
                <button 
                  className="btn-icon danger"
                  onClick={(e) => { e.stopPropagation(); handleDelete(collection.id); }}
                >
                  <FontAwesomeIcon icon={faTrash} />
                </button>
              </div>
            </div>
            <div className="collection-info">
              <h3>{collection.name}</h3>
              {collection.description && (
                <p className="collection-description">{collection.description}</p>
              )}
              <div className="collection-meta">
                <FontAwesomeIcon icon={faGamepad} />
                <span>{collection.gameCount} games</span>
              </div>
            </div>
            {/* Preview of games */}
            {collection.games.length > 0 && (
              <div className="collection-preview">
                {collection.games.slice(0, 4).map((game, idx) => (
                  <div key={game.id} className="preview-game" style={{ zIndex: 4 - idx }}>
                    {game.coverUrl ? (
                      <img src={game.coverUrl} alt={game.title} />
                    ) : (
                      <div className="preview-placeholder">?</div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        ))}

        {/* Empty state */}
        {collections.length === 0 && (
          <div className="empty-state">
            <FontAwesomeIcon icon={faFolder} />
            <h3>No collections yet</h3>
            <p>Create your first collection to organize your games</p>
            <button onClick={() => setShowCreateModal(true)}>
              <FontAwesomeIcon icon={faPlus} />
              Create Collection
            </button>
          </div>
        )}
      </div>

      {/* Create/Edit Modal */}
      <Modal
        isOpen={showCreateModal || !!editingCollection}
        onClose={() => { setShowCreateModal(false); setEditingCollection(null); }}
        title={editingCollection ? 'Edit Collection' : 'New Collection'}
        footer={
          <>
            <button className="btn-secondary" onClick={() => { setShowCreateModal(false); setEditingCollection(null); }}>
              Cancel
            </button>
            <button 
              className="btn-primary"
              onClick={editingCollection ? handleUpdate : handleCreate}
              disabled={!formData.name.trim()}
            >
              {editingCollection ? 'Save Changes' : 'Create'}
            </button>
          </>
        }
      >
        <div className="form-group">
          <label>Name</label>
          <input
            type="text"
            value={formData.name}
            onChange={(e) => setFormData({ ...formData, name: e.target.value })}
            placeholder="My Collection"
            autoFocus
          />
        </div>
        <div className="form-group">
          <label>Description (optional)</label>
          <textarea
            value={formData.description}
            onChange={(e) => setFormData({ ...formData, description: e.target.value })}
            placeholder="A brief description..."
            rows={3}
          />
        </div>
        <div className="form-group">
          <label>Color</label>
          <div className="color-picker">
            {colorOptions.map(color => (
              <button
                key={color}
                className={`color-option ${formData.color === color ? 'selected' : ''}`}
                style={{ backgroundColor: color }}
                onClick={() => setFormData({ ...formData, color })}
              >
                {formData.color === color && <FontAwesomeIcon icon={faCheck} />}
              </button>
            ))}
          </div>
        </div>
      </Modal>
    </div>
  );
};

export default Collections;
