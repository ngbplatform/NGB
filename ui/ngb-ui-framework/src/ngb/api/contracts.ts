export type DataType = string
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
  | { kind: 'catalog'; catalogType: string; displayTemplate?: string | null }
  | { kind: 'document'; documentTypes: string[] }
  | { kind: 'coa' }

export type FieldOption = { value: string; label: string }
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
  uiControl: number
  isRequired: boolean
  isReadOnly: boolean
  readOnlyWhenStatusIn?: DocumentStatusValue[] | null
  lookup?: LookupSource | null
  validation?: FieldValidation | null
  options?: FieldOption[] | null
  helpText?: string | null
}
export type FormRow = { fields: FieldMetadata[] }
export type FormSection = { title: string; rows: FormRow[] }
export type FormMetadata = { sections: FormSection[] }
export type PartMetadata = {
  partCode: string
  title: string
  list: ListMetadata
  allowAddRemoveRows?: boolean
  readOnlyWhenPosted?: boolean
}
export type CatalogCapabilities = {
  canCreate?: boolean
  canEdit?: boolean
  canDelete?: boolean
  canMarkForDeletion?: boolean
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
  canViewEffects?: boolean
  canViewFlow?: boolean
}
export type DocumentPresentation = {
  displayName?: string | null
  hasNumber?: boolean
  computedDisplay?: boolean
  hideSystemFieldsInEditor?: boolean
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
  capabilities?: CatalogCapabilities | null
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

export type NavigationTargetDto = {
  code: string
  parameters: Record<string, string | null>
}

export type DocumentStatus = DocumentStatusValue
export type NgbActionKind = ActionKind

export type CatalogLookupSourceDto = Extract<LookupSource, { kind: 'catalog' }>
export type DocumentLookupSourceDto = Extract<LookupSource, { kind: 'document' }>
export type ChartOfAccountsLookupSourceDto = Extract<LookupSource, { kind: 'coa' }>
export type LookupSourceDto = LookupSource

export type ColumnMetadataDto = ColumnMetadata
export type MetadataOptionDto = FieldOption
export type ListFilterOptionDto = ListFilterOption
export type ListFilterFieldDto = ListFilterField
export type ListMetadataDto = ListMetadata
export type FieldValidationDto = FieldValidation
export type FieldMetadataDto = FieldMetadata
export type FormRowDto = FormRow
export type FormSectionDto = FormSection
export type FormMetadataDto = FormMetadata
export type ActionMetadataDto = {
  code: string
  label: string
  kind?: NgbActionKind
  requiresConfirm?: boolean
  visibleWhenStatusIn?: DocumentStatus[] | null
}
export type CatalogTypeMetadataDto = CatalogTypeMetadata
export type PartMetadataDto = PartMetadata
export type DocumentCapabilitiesDto = DocumentCapabilities
export type DocumentPresentationDto = DocumentPresentation
export type DocumentTypeMetadataDto = DocumentTypeMetadata
export type RefValueDto = { id: string; display: string }

export type CatalogItemDto = {
  id: string
  display?: string | null
  payload: RecordPayload
  isMarkedForDeletion: boolean
  isDeleted: boolean
}

export type DocumentDto = {
  id: string
  number?: string | null
  display?: string | null
  payload: RecordPayload
  status: DocumentStatus
  isMarkedForDeletion: boolean
}

export type DocumentActionKindDto = 'Primary' | 'Secondary' | 'Dangerous'
export type DocumentActionExecutionKindDto = 'Command' | 'Derivation' | 'Navigation' | 'View'
export type DocumentActionConfirmationModeDto = 'None' | 'Confirm' | 'RequireReason'

export type DocumentActionDisabledReasonDto = {
  code: string
  message: string
}

export type DocumentActionConfirmationDto = {
  mode: DocumentActionConfirmationModeDto
  title: string
  message: string
  confirmLabel: string
}

export type DocumentActionTargetDto = NavigationTargetDto

export type DocumentActionDto = {
  code: string
  label: string
  labelKey?: string | null
  description?: string | null
  icon?: string | null
  kind: DocumentActionKindDto
  executionKind: DocumentActionExecutionKindDto
  order: number
  isAllowed: boolean
  disabledReasons: DocumentActionDisabledReasonDto[]
  confirmation?: DocumentActionConfirmationDto | null
  target?: DocumentActionTargetDto | null
}

export type DocumentEditorStateDto = {
  document: DocumentDto
  documentVersion: number
  actions: DocumentActionDto[]
}

export type ExecuteDocumentActionRequestDto = {
  expectedVersion: number
  payload?: unknown
  reason?: string | null
}

export type ExecuteDocumentActionResultDto = {
  executionId: string
  actionCode: string
  document: DocumentDto
  documentVersion: number
  actions: DocumentActionDto[]
  workCenterMayChange: boolean
  createdDocument?: DocumentDto | null
}

export type GraphNodeDto = {
  nodeId: string
  kind: EntityKind
  typeCode: string
  entityId: string
  title: string
  subtitle?: string | null
  documentStatus?: DocumentStatus | null
  depth?: number | null
  amount?: number | null
}

export type GraphEdgeDto = {
  fromNodeId: string
  toNodeId: string
  relationshipType: string
  label?: string | null
}

export type RelationshipGraphDto = {
  nodes: GraphNodeDto[]
  edges: GraphEdgeDto[]
}

export type EffectAccountDto = {
  accountId: string
  code: string
  name: string
}

export type EffectDimensionValueDto = {
  dimensionId: string
  valueId: string
  display: string
}

export type EffectResourceValueDto = {
  code: string
  value: number
}

export type AccountingEntryEffectDto = {
  entryId: string | number
  documentId?: string | null
  occurredAtUtc: string
  debitAccount?: EffectAccountDto | null
  creditAccount?: EffectAccountDto | null
  debitAccountId?: string | null
  creditAccountId?: string | null
  amount: number
  isStorno?: boolean
  debitDimensionSetId?: string | null
  creditDimensionSetId?: string | null
  debitDimensions?: EffectDimensionValueDto[] | null
  creditDimensions?: EffectDimensionValueDto[] | null
}

export type OperationalRegisterMovementEffectDto = {
  registerId?: string | null
  registerCode: string
  registerName?: string | null
  movementId: string | number
  documentId?: string | null
  occurredAtUtc: string
  periodMonth?: string | null
  isStorno?: boolean
  dimensionSetId?: string | null
  dimensions?: EffectDimensionValueDto[] | null
  resources: EffectResourceValueDto[] | Record<string, unknown>
}

export type ReferenceRegisterWriteEffectDto = {
  registerId?: string | null
  registerCode: string
  registerName?: string | null
  recordId: string | number
  documentId?: string | null
  periodUtc?: string | null
  periodBucketUtc?: string | null
  recordedAtUtc: string
  dimensionSetId?: string | null
  dimensions?: EffectDimensionValueDto[] | null
  fields: Record<string, unknown>
  isTombstone: boolean
}

export type DocumentEffectsDto = {
  accountingEntries: AccountingEntryEffectDto[]
  operationalRegisterMovements: OperationalRegisterMovementEffectDto[]
  referenceRegisterWrites: ReferenceRegisterWriteEffectDto[]
}

export type PageResponseDto<T> = {
  items: T[]
  offset: number
  limit: number
  total?: number | null
}

export type PageRequest = {
  offset?: number
  limit?: number
  search?: string
  filters?: Record<string, string>
}

export type LookupItemDto = {
  id: string
  label: string
  meta?: Record<string, string> | null
}

export type DocumentLookupDto = {
  id: string
  documentType: string
  display?: string | null
  status: DocumentStatus
  isMarkedForDeletion: boolean
  number?: string | null
}

export type DocumentLookupAcrossTypesRequestDto = {
  documentTypes: string[]
  query?: string | null
  perTypeLimit?: number | null
  activeOnly?: boolean | null
}

export type DocumentLookupByIdsRequestDto = {
  documentTypes: string[]
  ids: string[]
}

export type ByIdsRequestDto = {
  ids: string[]
}

export type AuditFieldChangeDto = {
  fieldPath: string
  oldValueJson?: string | null
  newValueJson?: string | null
}

export type AuditActorDto = {
  userId?: string | null
  displayName?: string | null
  email?: string | null
}

export type AuditEventDto = {
  auditEventId: string
  entityKind: EntityKind
  entityId: string
  actionCode: string
  actor?: AuditActorDto | null
  occurredAtUtc: string
  correlationId?: string | null
  metadataJson?: string | null
  changes: AuditFieldChangeDto[]
}

export type AuditCursorDto = {
  occurredAtUtc: string
  auditEventId: string
}

export type AuditLogPageDto = {
  items: AuditEventDto[]
  nextCursor?: AuditCursorDto | null
  limit: number
}
