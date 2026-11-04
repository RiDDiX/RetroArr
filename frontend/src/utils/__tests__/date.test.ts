import { describe, expect, it } from 'vitest';
import { formatDate } from '../date';

describe('formatDate', () => {
  it('returns an empty string for empty input', () => {
    expect(formatDate('')).toBe('');
    expect(formatDate(null)).toBe('');
    expect(formatDate(undefined)).toBe('');
  });

  it('formats an ISO string with its year', () => {
    const out = formatDate('2024-06-15T12:30:00Z');
    expect(out).toContain('2024');
    expect(out.length).toBeGreaterThan(8);
  });
});
