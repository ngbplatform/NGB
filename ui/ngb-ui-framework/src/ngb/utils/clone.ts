type PlainObject = Record<string, unknown>

function isPlainObject(value: unknown): value is PlainObject {
  if (value == null || typeof value !== 'object') return false
  const prototype = Object.getPrototypeOf(value)
  return prototype === Object.prototype || prototype === null
}

function clonePlainDataFallback<T>(value: T): T {
  if (value == null || typeof value !== 'object') return value

  if (value instanceof Date) {
    return new Date(value.getTime()) as T
  }

  if (Array.isArray(value)) {
    return value.map((item) => clonePlainDataFallback(item)) as T
  }

  if (isPlainObject(value)) {
    const out: PlainObject = {}
    for (const [key, entry] of Object.entries(value)) {
      out[key] = clonePlainDataFallback(entry)
    }
    return out as T
  }

  return value
}

export function clonePlainData<T>(value: T): T {
  if (value == null || typeof value !== 'object') return value

  if (typeof structuredClone === 'function') {
    try {
      return structuredClone(value)
    } catch {
      // Fall back to recursive cloning for plain DTO-like data.
    }
  }

  return clonePlainDataFallback(value)
}
