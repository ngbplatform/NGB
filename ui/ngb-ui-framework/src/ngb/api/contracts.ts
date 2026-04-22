import type { ReferenceValue } from '../metadata/entityModel'
import type {
  ActionKind,
  CatalogTypeMetadata,
  ColumnAlign,
  ColumnMetadata,
  DataType,
  DocumentCapabilities,
  DocumentPresentation,
  DocumentStatusValue,
  DocumentTypeMetadata,
  EntityKind,
  FieldMetadata,
  FieldValidation,
  FormMetadata,
  FormRow,
  FormSection,
  FieldOption,
  ListFilterField,
  ListFilterOption,
  ListMetadata,
  LookupSource,
  PartMetadata,
  RecordFields,
  RecordPart,
  RecordPartRow,
  RecordParts,
  RecordPayload,
} from '../metadata/types'

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
export type RefValueDto = ReferenceValue

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

export type DocumentUiActionReasonDto = {
  errorCode: string
  message: string
}

export type DocumentUiEffectsDto = {
  isPosted: boolean
  canEdit: boolean
  canPost: boolean
  canUnpost: boolean
  canRepost: boolean
  canApply: boolean
  disabledReasons?: Record<string, DocumentUiActionReasonDto[]> | null
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
  ui?: DocumentUiEffectsDto | null
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

export type DocumentDerivationActionDto = {
  code: string
  name: string
  fromTypeCode: string
  toTypeCode: string
  relationshipCodes: string[]
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

export type {
  ColumnAlign,
  DataType,
  EntityKind,
  RecordFields,
  RecordPart,
  RecordPartRow,
  RecordParts,
  RecordPayload,
}
