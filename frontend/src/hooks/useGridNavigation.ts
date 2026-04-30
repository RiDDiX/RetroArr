import { useEffect } from 'react';

type Options = {
  itemSelector?: string;
};

/* Roving-tabindex arrow-key nav for any container of focusable items.
   Arrow keys walk the siblings in visual (2D) order - left/right step by one,
   up/down compute the column count from offsetTop boundaries. Enter falls
   through to the item's own click handler. Home/End jump to first/last.
   Tab still escapes the grid normally. */
export function useGridNavigation<T extends HTMLElement = HTMLElement>(
  ref: React.RefObject<T>,
  opts: Options = {},
) {
  const itemSelector = opts.itemSelector ?? '[tabindex]:not([tabindex="-2"])';

  useEffect(() => {
    const container = ref.current;
    if (!container) return;

    const items = () =>
      Array.from(container.querySelectorAll<HTMLElement>(itemSelector));

    const syncRoving = () => {
      const list = items();
      if (list.length === 0) return;
      const active = document.activeElement as HTMLElement | null;
      const activeIdx = active && list.includes(active) ? list.indexOf(active) : 0;
      list.forEach((el, i) => {
        el.tabIndex = i === activeIdx ? 0 : -1;
      });
    };

    // Compute column count by grouping elements that share an offsetTop row.
    const cols = (list: HTMLElement[]): number => {
      if (list.length === 0) return 1;
      const firstTop = list[0].offsetTop;
      let c = 0;
      for (const el of list) {
        if (el.offsetTop !== firstTop) break;
        c++;
      }
      return Math.max(1, c);
    };

    const focusAt = (list: HTMLElement[], idx: number) => {
      const clamped = Math.max(0, Math.min(list.length - 1, idx));
      list.forEach((el, i) => {
        el.tabIndex = i === clamped ? 0 : -1;
      });
      list[clamped]?.focus();
    };

    const onKeyDown = (e: KeyboardEvent) => {
      const list = items();
      if (list.length === 0) return;
      const active = document.activeElement as HTMLElement | null;
      if (!active || !list.includes(active)) return;
      const current = list.indexOf(active);
      const colCount = cols(list);

      switch (e.key) {
        case 'ArrowRight': e.preventDefault(); focusAt(list, current + 1); return;
        case 'ArrowLeft':  e.preventDefault(); focusAt(list, current - 1); return;
        case 'ArrowDown':  e.preventDefault(); focusAt(list, current + colCount); return;
        case 'ArrowUp':    e.preventDefault(); focusAt(list, current - colCount); return;
        case 'Home':       e.preventDefault(); focusAt(list, 0); return;
        case 'End':        e.preventDefault(); focusAt(list, list.length - 1); return;
      }
    };

    syncRoving();
    container.addEventListener('keydown', onKeyDown);
    // Re-sync when the grid content changes size.
    const mo = new MutationObserver(syncRoving);
    mo.observe(container, { childList: true, subtree: false });

    return () => {
      container.removeEventListener('keydown', onKeyDown);
      mo.disconnect();
    };
  }, [ref, itemSelector]);
}
