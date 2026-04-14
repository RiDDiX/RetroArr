type Props = {
  value: number;                 // 0..1
  blocks?: number;               // default 20
  label?: string;                // screen-reader text, e.g. "Download progress"
  flickerLast?: boolean;         // pulse the leading block while active
};

export function SegmentedBar({ value, blocks = 20, label, flickerLast = true }: Props) {
  const clamped = Math.max(0, Math.min(1, value));
  const filledCount = Math.round(clamped * blocks);
  const pct = Math.round(clamped * 100);

  return (
    <div
      className="retro-bar"
      role="progressbar"
      aria-label={label}
      aria-valuenow={pct}
      aria-valuemin={0}
      aria-valuemax={100}
      style={{ '--retro-bar-blocks': blocks } as React.CSSProperties}
    >
      {Array.from({ length: blocks }, (_, i) => {
        const isFilled = i < filledCount;
        const isLeading = flickerLast && isFilled && i === filledCount - 1 && filledCount < blocks;
        return (
          <span
            key={i}
            className={`retro-bar__block ${isLeading ? 'retro-bar__block--lead' : ''}`}
            data-filled={isFilled}
          />
        );
      })}
    </div>
  );
}
