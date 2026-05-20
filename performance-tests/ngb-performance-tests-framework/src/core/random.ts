export function pickOne<T>(items: readonly T[]): T {
  if (items.length === 0) {
    throw new Error('Cannot pick from an empty collection.');
  }

  const index = Math.floor(Math.random() * items.length);
  const item = items[index];
  if (item === undefined) {
    throw new Error(`Random index ${index} was outside collection bounds.`);
  }

  return item;
}

export function randomInt(minInclusive: number, maxInclusive: number): number {
  if (maxInclusive < minInclusive) {
    throw new Error(`Invalid random range: ${minInclusive}..${maxInclusive}`);
  }

  return minInclusive + Math.floor(Math.random() * (maxInclusive - minInclusive + 1));
}

export function randomSuffix(prefix = 'perf'): string {
  return `${prefix}-${Date.now().toString(36)}-${randomInt(1000, 9999)}`;
}
