import React, { createContext, useContext, useState, ReactNode } from 'react';

interface UIContextType {
    isKofiOpen: boolean;
    toggleKofi: () => void;
    closeKofi: () => void;
    openKofi: () => void;
}

const UIContext = createContext<UIContextType | undefined>(undefined);

export const UIProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
    const [isKofiOpen, setIsKofiOpen] = useState(false);

    const toggleKofi = () => setIsKofiOpen(prev => !prev);
    const closeKofi = () => setIsKofiOpen(false);
    const openKofi = () => setIsKofiOpen(true);

    return (
        <UIContext.Provider value={{ isKofiOpen, toggleKofi, closeKofi, openKofi }}>
            {children}
        </UIContext.Provider>
    );
};

export const useUI = () => {
    const context = useContext(UIContext);
    if (context === undefined) {
        throw new Error('useUI must be used within a UIProvider');
    }
    return context;
};
