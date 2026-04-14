import { useEffect, useRef } from 'react';

export function useCardTilt<T extends HTMLElement = HTMLElement>(maxDeg = 4) {
  const ref = useRef<T>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;

    const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const coarsePointer  = window.matchMedia('(pointer: coarse)').matches;
    if (prefersReduced || coarsePointer) return;

    let raf = 0;
    const reset = () => {
      el.style.setProperty('--tilt-x', '0deg');
      el.style.setProperty('--tilt-y', '0deg');
      el.style.setProperty('--tilt-lift', '0');
    };

    const onMove = (ev: PointerEvent) => {
      const r = el.getBoundingClientRect();
      const x = (ev.clientX - r.left) / r.width  - 0.5;
      const y = (ev.clientY - r.top)  / r.height - 0.5;
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => {
        el.style.setProperty('--tilt-x', `${(-y * maxDeg).toFixed(2)}deg`);
        el.style.setProperty('--tilt-y', `${( x * maxDeg).toFixed(2)}deg`);
        el.style.setProperty('--tilt-lift', '1');
      });
    };

    el.addEventListener('pointermove',  onMove);
    el.addEventListener('pointerleave', reset);
    return () => {
      el.removeEventListener('pointermove',  onMove);
      el.removeEventListener('pointerleave', reset);
      cancelAnimationFrame(raf);
    };
  }, [maxDeg]);

  return ref;
}
