import { useEffect, useState } from 'react';
import './BootScreen.css';

type Phase = 'black' | 'crt' | 'logo' | 'done';

type Props = {
  onReady?: () => void;
};

/* Short CRT power-on before the shell renders.
   120ms dark → 260ms CRT gradient rises → 720ms logo settles → done. */
export function BootScreen({ onReady }: Props) {
  const [phase, setPhase] = useState<Phase>('black');

  useEffect(() => {
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduced) {
      setPhase('done');
      onReady?.();
      return;
    }
    const t1 = setTimeout(() => setPhase('crt'),  120);
    const t2 = setTimeout(() => setPhase('logo'), 380);
    const t3 = setTimeout(() => {
      setPhase('done');
      onReady?.();
    }, 1100);
    return () => { clearTimeout(t1); clearTimeout(t2); clearTimeout(t3); };
  }, [onReady]);

  if (phase === 'done') return null;

  return (
    <div className={`boot boot--${phase}`} aria-hidden="true">
      <div className="boot__crt" />
      <div className="boot__logo">
        <span className="boot__mark">RETRO</span>
        <span className="boot__mark boot__mark--accent">ARR</span>
      </div>
      <div className="boot__scanlines" />
    </div>
  );
}
