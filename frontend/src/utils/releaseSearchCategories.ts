const CONSOLE = '1000';
const PC = '4000,4010,4050';
const UNKNOWN = '1000,4000,4050';
const PC_PLATFORM_SLUGS = new Set(['pc', 'steam', 'steamos', 'gog', 'windows']);
const PC_PLATFORM_NAMES = new Set(['pc', 'pc (windows)', 'steam', 'steamos', 'gog', 'gog galaxy', 'windows']);

export function getReleaseSearchCategories(platform?: { name: string; slug?: string; category?: string } | null): string {
  const slug = platform?.slug?.toLowerCase();
  const name = platform?.name.toLowerCase();
  if (!platform || slug === 'unknown' || name === 'unknown') {
    return UNKNOWN;
  }

  const category = platform.category?.toLowerCase();
  if (category === 'computer' || category === 'special' ||
      PC_PLATFORM_SLUGS.has(slug ?? '') || PC_PLATFORM_NAMES.has(name ?? '')) {
    return PC;
  }

  return CONSOLE;
}
