import type {
  CatalogTypeMetadata,
  DocumentTypeMetadata,
  FieldMetadata,
  FormMetadata,
  ListFilterField,
  ListMetadata,
  PartMetadata,
} from './types'

const DATA_TYPE_BY_LEGACY_CODE: Record<number, string> = {
  1: 'String',
  2: 'Int32',
  3: 'Decimal',
  4: 'Boolean',
  5: 'Date',
  6: 'DateTime',
  7: 'Money',
  8: 'Guid',
}

function normalizeDataType(value: unknown): string {
  if (typeof value === 'string') {
    const normalized = value.trim()
    return normalized || 'Unknown'
  }

  if (typeof value === 'number' && Number.isFinite(value)) {
    return DATA_TYPE_BY_LEGACY_CODE[Math.trunc(value)] ?? 'Unknown'
  }

  return 'Unknown'
}

function normalizeListFilterField(field: ListFilterField): ListFilterField {
  return {
    ...field,
    dataType: normalizeDataType(field.dataType),
  }
}

function normalizeListMetadata(list: ListMetadata | null | undefined): ListMetadata | null | undefined {
  if (!list) return list

  return {
    ...list,
    columns: (list.columns ?? []).map((column) => ({
      ...column,
      dataType: normalizeDataType(column.dataType),
    })),
    filters: (list.filters ?? []).map(normalizeListFilterField),
  }
}

function normalizeField(field: FieldMetadata): FieldMetadata {
  return {
    ...field,
    dataType: normalizeDataType(field.dataType),
  }
}

function normalizeFormMetadata(form: FormMetadata | null | undefined): FormMetadata | null | undefined {
  if (!form) return form

  return {
    ...form,
    sections: (form.sections ?? []).map((section) => ({
      ...section,
      rows: (section.rows ?? []).map((row) => ({
        ...row,
        fields: (row.fields ?? []).map(normalizeField),
      })),
    })),
  }
}

function normalizePartMetadata(part: PartMetadata): PartMetadata {
  return {
    ...part,
    list: normalizeListMetadata(part.list) ?? part.list,
  }
}

export function normalizeCatalogTypeMetadata(metadata: CatalogTypeMetadata): CatalogTypeMetadata {
  return {
    ...metadata,
    list: normalizeListMetadata(metadata.list),
    form: normalizeFormMetadata(metadata.form),
    parts: (metadata.parts ?? []).map(normalizePartMetadata),
  }
}

export function normalizeDocumentTypeMetadata(metadata: DocumentTypeMetadata): DocumentTypeMetadata {
  return {
    ...metadata,
    list: normalizeListMetadata(metadata.list),
    form: normalizeFormMetadata(metadata.form),
    parts: (metadata.parts ?? []).map(normalizePartMetadata),
  }
}
