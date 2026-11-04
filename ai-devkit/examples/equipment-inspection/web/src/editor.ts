import { defineComponent } from 'vue'
import {
  createConfiguredCatalogEntityEditorPersistence,
  createConfiguredDocumentEntityEditorPersistence,
  type ConfiguredEntityEditorConfiguration,
} from '@ngbplatform/ui'

// Documents exposed by this web fixture have head fields only. The API-only checklist
// needs its own parts editor and persistence strategy through these extension points.
export const editorConfiguration: ConfiguredEntityEditorConfiguration = {
  documentPartsExtensionKey: 'equipment-inspection.parts',
  documentPartsEditor: defineComponent({ render: () => null }),
  metadataFormBehavior: {},
  createCatalogPersistence: createConfiguredCatalogEntityEditorPersistence,
  createDocumentPersistence: context => createConfiguredDocumentEntityEditorPersistence(context, {
    buildPayload: () => null,
    hydrate: () => {},
    synchronize: () => {},
  }),
}
