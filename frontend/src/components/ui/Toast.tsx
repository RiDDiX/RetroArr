import React, { useEffect } from 'react';
import './Toast.css';

interface ToastProps {
  message: string;
  type: 'success' | 'error' | 'info' | 'warning';
  onDismiss: () => void;
  duration?: number;
}

const Toast: React.FC<ToastProps> = ({ message, type, onDismiss, duration = 4000 }) => {
  useEffect(() => {
    const timer = setTimeout(onDismiss, duration);
    return () => clearTimeout(timer);
  }, [onDismiss, duration]);

  const icons: Record<string, string> = {
    success: '✓',
    error: '✗',
    info: 'ℹ',
    warning: '⚠'
  };

  return (
    <div className={`toast toast-${type}`} onClick={onDismiss}>
      <span className="toast-icon">{icons[type]}</span>
      <span className="toast-message">{message}</span>
    </div>
  );
};

export default Toast;
