import React from 'react';

type Props = {
  variant?: 'card' | 'row' | 'line' | 'circle';
  width?: number | string;
  height?: number | string;
  className?: string;
};

export function Skeleton({ variant = 'line', width, height, className = '' }: Props) {
  const style: React.CSSProperties = {};
  if (width  !== undefined) style.width  = typeof width  === 'number' ? `${width}px`  : width;
  if (height !== undefined) style.height = typeof height === 'number' ? `${height}px` : height;

  const cls =
    variant === 'card'   ? 'skeleton skeleton-card'
  : variant === 'row'    ? 'skeleton skeleton-row'
  : variant === 'circle' ? 'skeleton skeleton-circle'
  :                        'skeleton skeleton-line';

  return <div className={`${cls} ${className}`.trim()} style={style} aria-hidden="true" />;
}

export function SkeletonGrid({ count = 12 }: { count?: number }) {
  return (
    <>
      {Array.from({ length: count }, (_, i) => (
        <Skeleton key={i} variant="card" />
      ))}
    </>
  );
}
