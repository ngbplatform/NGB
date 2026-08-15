/** Curated public surface for configured entity-editor integrations. */
export { default as NgbConfiguredEntityEditor } from './ngb/editor/NgbConfiguredEntityEditor.vue'
export { default as NgbEntityEditor } from './ngb/editor/NgbEntityEditor.vue'
export type {
  ConfiguredEntityEditorConfiguration,
  ConfiguredEntityEditorDocumentPartErrors,
  ConfiguredEntityEditorProps,
} from './ngb/editor/configuredEntityEditor'
export type {
  CatalogEntityPersistenceAdapter,
  ConfiguredDocumentPartsPersistenceStrategy,
  ConfiguredEntityEditorPersistenceContext,
  DocumentEntityPersistenceAdapter,
} from './ngb/editor/entityEditorPersistence'
export {
  createConfiguredCatalogEntityEditorPersistence,
  createConfiguredDocumentEntityEditorPersistence,
} from './ngb/editor/entityEditorPersistence'
export type {
  EditorChangeReason,
  EditorKind,
  EditorMode,
  EntityEditorFlags,
  EntityEditorHandle,
} from './ngb/editor/types'
