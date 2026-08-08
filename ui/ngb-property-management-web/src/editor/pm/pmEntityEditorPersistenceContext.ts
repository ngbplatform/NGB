import type { ComputedRef, Ref } from 'vue'
import type {
  CatalogItemDto,
  CatalogTypeMetadataDto,
  DocumentDto,
  DocumentEffectsDto,
  DocumentTypeMetadataDto,
  EditorErrorState,
  EntityEditorContext,
  EntityFormModel,
  EditorKind,
  LookupStoreApi,
  RecordPayload,
} from '@ngbplatform/ui'
import type { LeasePartyRow } from './leasePartyTypes'

export type PmEntityEditorLeaseAdapter = {
  isLeaseDocument: ComputedRef<boolean>
  leasePartiesRows: Ref<LeasePartyRow[]>
  ensureLeasePartiesInitialized: () => void
  validateLeasePartiesBeforeSave: () => string | null
  applyInitialParts: (parts: RecordPayload['parts'] | null | undefined) => void
  applyPersistedParts: (parts: RecordPayload['parts'] | null | undefined) => void
  buildSaveParts: () => RecordPayload['parts'] | undefined
}

export type PmEntityEditorPersistenceContext = {
  kind: ComputedRef<EditorKind>
  typeCode: ComputedRef<string>
  currentId: Ref<string | null>
  isNew: ComputedRef<boolean>
  metadata: ComputedRef<CatalogTypeMetadataDto | DocumentTypeMetadataDto | null>
  catalogMeta: Ref<CatalogTypeMetadataDto | null>
  docMeta: Ref<DocumentTypeMetadataDto | null>
  catalogItem: Ref<CatalogItemDto | null>
  doc: Ref<DocumentDto | null>
  docEffects: Ref<DocumentEffectsDto | null>
  model: Ref<EntityFormModel>
  lookupStore: LookupStoreApi
  initialFields: ComputedRef<EntityFormModel | null>
  initialParts: ComputedRef<RecordPayload['parts'] | null>
  leaseEditor: PmEntityEditorLeaseAdapter
  currentEditorContext: () => EntityEditorContext
  ensureCatalogMetadata: (typeCode: string) => Promise<CatalogTypeMetadataDto>
  ensureDocumentMetadata: (typeCode: string) => Promise<DocumentTypeMetadataDto>
  resetInitialSnapshot: () => void
  setEditorError: (value: EditorErrorState | null) => void
  onCreated: (id: string) => void | Promise<void>
  onSaved: () => void | Promise<void>
}
