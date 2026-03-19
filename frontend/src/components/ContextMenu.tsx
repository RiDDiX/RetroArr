import React, { useRef } from 'react';
import './ContextMenu.css';

export interface ContextMenuOption {
    label: string;
    onClick: () => void;
    icon?: React.ReactNode;
    danger?: boolean;
}

interface ContextMenuProps {
    x: number;
    y: number;
    visible: boolean;
    options: ContextMenuOption[];
    onClose: () => void;
}

const ContextMenu: React.FC<ContextMenuProps> = ({ x, y, visible, options, onClose }) => {
    const menuRef = useRef<HTMLDivElement>(null);

    if (!visible) return null;

    // Prevent context menu going off screen
    const style: React.CSSProperties = {
        top: y,
        left: x,
    };

    return (
        <>
            <div
                className="context-menu-overlay"
                onClick={onClose}
                onContextMenu={(e) => {
                    e.preventDefault();
                    onClose();
                }}
            />
            <div
                className="context-menu"
                style={style}
                ref={menuRef}
                onContextMenu={(e) => e.preventDefault()}
            >
                {options.map((option, index) => (
                    <div
                        key={index}
                        className={`context-menu-item ${option.danger ? 'danger' : ''}`}
                        onClick={(e) => {
                            e.stopPropagation();
                            console.log('ContextMenu item clicked:', option.label);
                            option.onClick();
                            onClose();
                        }}
                    >
                        {option.icon && <span className="icon">{option.icon}</span>}
                        <span className="label">{option.label}</span>
                    </div>
                ))}
            </div>
        </>
    );
};

export default ContextMenu;
