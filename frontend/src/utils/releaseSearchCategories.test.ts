import { describe, expect, it } from 'vitest';
import { getReleaseSearchCategories } from './releaseSearchCategories';

describe('getReleaseSearchCategories', () => {
  it.each([
    { name: 'PlayStation Portable', slug: 'psp', category: 'Sony' },
    { name: 'Nintendo Switch', slug: 'switch', category: 'Nintendo' },
    { name: 'PlayStation 4', slug: 'ps4', category: 'Sony' },
    { name: 'PC-FX', slug: 'pcfx', category: 'NEC' },
  ])('searches the Console category for $name', platform => {
    expect(getReleaseSearchCategories(platform)).toBe('1000');
  });

  it.each([
    { name: 'Amiga', category: 'Computer' },
    { name: 'ScummVM', category: 'Special' },
    { name: 'Steam', slug: 'steam' },
    { name: 'GOG Galaxy' },
  ])('preserves PC categories for $name', platform => {
    expect(getReleaseSearchCategories(platform)).toBe('4000,4010,4050');
  });

  it('searches both Console and PC categories when the platform is unknown', () => {
    expect(getReleaseSearchCategories()).toBe('1000,4000,4050');
    expect(getReleaseSearchCategories(null)).toBe('1000,4000,4050');
    expect(getReleaseSearchCategories({ name: 'Unknown', slug: 'unknown', category: 'Special' }))
      .toBe('1000,4000,4050');
  });
});
