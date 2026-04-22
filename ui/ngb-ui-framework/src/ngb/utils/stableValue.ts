function normalizeStableValue(value: unknown, seen: WeakSet<object>): unknown {
  if (value == null || typeof value !== 'object') return value
  if (seen.has(value)) return '[Circular]'
  seen.add(value)

  if (Array.isArray(value)) return value.map((entry) => normalizeStableValue(entry, seen))

  const output: Record<string, unknown> = {}
  for (const key of Object.keys(value).sort()) {
    output[key] = normalizeStableValue((value as Record<string, unknown>)[key], seen)
  }
  return output
}

export function stableStringify(value: unknown): string {
  return JSON.stringify(normalizeStableValue(value, new WeakSet<object>()))
}

export function stableEquals(left: unknown, right: unknown): boolean {
  return stableStringify(left) === stableStringify(right)
}
