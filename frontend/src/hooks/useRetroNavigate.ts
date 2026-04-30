import { useCallback } from 'react';
import { useNavigate, type NavigateOptions, type To } from 'react-router-dom';

type DocWithVT = Document & {
  startViewTransition?: (cb: () => void) => { finished: Promise<void> };
};

/**
 * Wraps react-router navigation in document.startViewTransition where
 * available. Everywhere else it's a plain navigate() - no animation,
 * no error. Respects prefers-reduced-motion.
 */
export function useRetroNavigate() {
  const nav = useNavigate();

  return useCallback((to: To, options?: NavigateOptions) => {
    const doc = document as DocWithVT;
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    if (!reduced && typeof doc.startViewTransition === 'function') {
      const transition = doc.startViewTransition(() => nav(to, options));
      // If the callback throws the browser still resolves .finished as rejected;
      // swallow it so the error doesn't bubble into an unhandled rejection.
      transition.finished.catch(() => {});
      return;
    }
    nav(to, options);
  }, [nav]);
}
