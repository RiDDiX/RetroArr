import React from 'react';
import Modal from './Modal';

interface ConfirmDialogProps {
  isOpen: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  title?: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: 'danger' | 'warning' | 'default';
}

const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
  isOpen,
  onConfirm,
  onCancel,
  title = 'Confirm',
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  variant = 'default'
}) => {
  const confirmClass = variant === 'danger' ? 'btn-danger' : 'btn-primary';

  return (
    <Modal
      isOpen={isOpen}
      onClose={onCancel}
      title={title}
      maxWidth="420px"
      footer={
        <>
          <button className="btn-secondary" onClick={onCancel}>{cancelLabel}</button>
          <button className={confirmClass} onClick={onConfirm}>{confirmLabel}</button>
        </>
      }
    >
      <p style={{ color: 'var(--ctp-text)', margin: 0, lineHeight: 1.6 }}>{message}</p>
    </Modal>
  );
};

export default ConfirmDialog;
