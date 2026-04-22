export function dataTypeKind(dataType: unknown): string {
  const normalized = String(dataType ?? '').trim()
  return normalized || 'Unknown'
}

export function isGuidType(dataType: unknown): boolean {
  return dataTypeKind(dataType) === 'Guid'
}

export function isBooleanType(dataType: unknown): boolean {
  return dataTypeKind(dataType) === 'Boolean'
}

export function isNumberType(dataType: unknown): boolean {
  const kind = dataTypeKind(dataType)
  return kind === 'Int32' || kind === 'Decimal' || kind === 'Money'
}

export function isDateType(dataType: unknown): boolean {
  const kind = dataTypeKind(dataType)
  return kind === 'Date' || kind === 'DateOnly'
}

export function isDateTimeType(dataType: unknown): boolean {
  const kind = dataTypeKind(dataType)
  return kind === 'DateTime' || kind === 'DateTimeOffset'
}
