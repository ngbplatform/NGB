import type { RouteLocationRaw } from 'vue-router'

export type Awaitable<T> = T | Promise<T>

export type DataType = string
export type UiControl = number
export type ColumnAlign = number
export type EntityKind = number
export type DocumentStatusValue = number
export type ActionKind = number

export type JsonPrimitive = string | number | boolean | null
export type JsonValue = JsonPrimitive | JsonObject | JsonValue[]
export type JsonObject = { [key: string]: JsonValue }
export type RecordFields = Record<string, JsonValue>
export type RecordPartRow = RecordFields
export type RecordPart = { rows: RecordPartRow[] }
export type RecordParts = Record<string, RecordPart>
export type RecordPayload = {
  fields?: RecordFields | null
  parts?: RecordParts | null
}

export type LookupSource =
  | {
      kind: 'catalog'
      catalogType: string
      displayTemplate?: string | null
    }
  | {
      kind: 'document'
      documentTypes: string[]
    }
  | {
      kind: 'coa'
    }

export type LookupHint =
  | {
      kind: 'catalog'
      catalogType: string
      filters?: Record<string, string>
    }
  | {
      kind: 'document'
      documentTypes: string[]
    }
  | {
      kind: 'coa'
    }

export type LookupItem = {
  id: string
  label: string
  meta?: string
}

export type FieldOption = {
  value: string
  label: string
}

export type ColumnMetadata = {
  key: string
  label: string
  dataType: DataType
  isSortable: boolean
  widthPx?: number | null
  align: ColumnAlign
  lookup?: LookupSource | null
  options?: FieldOption[] | null
}

export type ListFilterOption = FieldOption

export type ListFilterField = {
  key: string
  label: string
  dataType: DataType
  isMulti?: boolean
  lookup?: LookupSource | null
  options?: ListFilterOption[] | null
  description?: string | null
  supportsIncludeDescendants?: boolean
}

export type ListMetadata = {
  columns: ColumnMetadata[]
  filters?: ListFilterField[] | null
}

export type FieldValidation = {
  maxLength?: number | null
  min?: number | null
  max?: number | null
  regex?: string | null
}

export type FieldMetadata = {
  key: string
  label: string
  dataType: DataType
  uiControl: UiControl
  isRequired: boolean
  isReadOnly: boolean
  readOnlyWhenStatusIn?: DocumentStatusValue[] | null
  lookup?: LookupSource | null
  validation?: FieldValidation | null
  options?: FieldOption[] | null
  helpText?: string | null
}

export type FormRow = {
  fields: FieldMetadata[]
}

export type FormSection = {
  title: string
  rows: FormRow[]
}

export type FormMetadata = {
  sections: FormSection[]
}

export type PartMetadata = {
  partCode: string
  title: string
  list: ListMetadata
  allowAddRemoveRows?: boolean
  readOnlyWhenPosted?: boolean
}

export type ActionMetadata = {
  code: string
  label: string
  kind?: ActionKind
  requiresConfirm?: boolean
  visibleWhenStatusIn?: DocumentStatusValue[] | null
}

export type CatalogTypeMetadata = {
  catalogType: string
  displayName: string
  kind: EntityKind
  icon?: string | null
  list?: ListMetadata | null
  form?: FormMetadata | null
  parts?: PartMetadata[] | null
}

export type DocumentCapabilities = {
  canCreate?: boolean
  canEditDraft?: boolean
  canDeleteDraft?: boolean
  canPost?: boolean
  canUnpost?: boolean
  canRepost?: boolean
  canMarkForDeletion?: boolean
  supportsActions?: boolean
}

export type DocumentPresentation = {
  displayName?: string | null
  hasNumber?: boolean
  computedDisplay?: boolean
  hideSystemFieldsInEditor?: boolean
}

export type DocumentTypeMetadata = {
  documentType: string
  displayName: string
  kind: EntityKind
  icon?: string | null
  list?: ListMetadata | null
  form?: FormMetadata | null
  parts?: PartMetadata[] | null
  actions?: ActionMetadata[] | null
  presentation?: DocumentPresentation | null
  capabilities?: DocumentCapabilities | null
}

export type EntityFormModel = Record<string, unknown>

export type ReferenceValue = {
  id: string
  display: string
}

export type FilterLookupItem = LookupItem

export type FilterFieldState<TItem extends FilterLookupItem = FilterLookupItem> = {
  raw: string
  items: TItem[]
  includeDescendants?: boolean
}

export type FilterFieldOption = {
  value: unknown
  label: string
}

export type FilterFieldLike = {
  label: string
  dataType?: unknown
  isMulti?: boolean
  lookup?: LookupSource | null
  options?: readonly FilterFieldOption[] | null
  supportsIncludeDescendants?: boolean
}

export type LookupStoreApi<TItem extends LookupItem = LookupItem> = {
  searchCatalog: (
    catalogType: string,
    query: string,
    options?: { filters?: Record<string, string> },
  ) => Promise<TItem[]>
  searchCoa: (query: string) => Promise<TItem[]>
  searchDocuments: (documentTypes: string[], query: string) => Promise<TItem[]>
  ensureCatalogLabels: (catalogType: string, ids: string[]) => Promise<void>
  ensureCoaLabels: (ids: string[]) => Promise<void>
  ensureAnyDocumentLabels: (documentTypes: string[], ids: string[]) => Promise<void>
  labelForCatalog: (catalogType: string, id: unknown) => string
  labelForCoa: (id: unknown) => string
  labelForAnyDocument: (documentTypes: string[], id: unknown) => string
}

export type ResolvedLookupSource = LookupHint | LookupSource

export type FieldResolverArgs = {
  entityTypeCode: string
  model: EntityFormModel
  field: FieldMetadata
}

export type FieldReadonlyArgs = FieldResolverArgs & {
  status?: DocumentStatusValue
  forceReadonly?: boolean
}

export type FieldHiddenArgs = FieldResolverArgs & {
  isDocumentEntity: boolean
}

export type LookupHintArgs = FieldResolverArgs

export type LookupSearchArgs = {
  hint: LookupHint
  query: string
}

export type LookupTargetArgs = {
  hint: LookupHint
  value: unknown
  routeFullPath: string
}

export type MetadataFormBehavior = {
  resolveFieldOptions?: (args: FieldResolverArgs) => FieldOption[] | null
  resolveLookupHint?: (args: LookupHintArgs) => LookupHint | null
  isFieldReadonly?: (args: FieldReadonlyArgs) => boolean
  isFieldHidden?: (args: FieldHiddenArgs) => boolean
  findDisplayField?: (form: FormMetadata) => FieldMetadata | null
  searchLookup?: (args: LookupSearchArgs) => Awaitable<LookupItem[]>
  buildLookupTargetUrl?: (args: LookupTargetArgs) => Awaitable<RouteLocationRaw | null>
}
