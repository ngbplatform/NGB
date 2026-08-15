import type { Component } from 'vue'

import type { EntityFormModel, MetadataFormBehavior, RecordPayload } from '../metadata/types'
import type {
  CatalogEntityPersistenceAdapter,
  ConfiguredEntityEditorPersistenceContext,
  DocumentEntityPersistenceAdapter,
} from './entityEditorPersistence'
import type { EditorKind, EditorMode } from './types'

export type ConfiguredEntityEditorProps = {
  kind: EditorKind
  typeCode: string
  id?: string | null
  mode?: EditorMode
  canBack?: boolean
  initialFields?: EntityFormModel | null
  initialParts?: RecordPayload['parts'] | null
  expandTo?: string | null
  compactTo?: string | null
  closeTo?: string | null
  navigateOnCreate?: boolean
}

export type ConfiguredEntityEditorConfiguration = {
  documentPartsExtensionKey: string
  documentPartsEditor: Component
  metadataFormBehavior: MetadataFormBehavior
  createCatalogPersistence: (context: ConfiguredEntityEditorPersistenceContext) => CatalogEntityPersistenceAdapter
  createDocumentPersistence: (context: ConfiguredEntityEditorPersistenceContext) => DocumentEntityPersistenceAdapter
}

export type ConfiguredEntityEditorDocumentPartErrors = Record<string, Record<number, Record<string, string>>>
