<script setup lang="ts">
import { markRaw } from 'vue'
import {
  NgbConfiguredEntityEditor,
  type ConfiguredEntityEditorConfiguration,
  type ConfiguredEntityEditorProps,
} from '@ngbplatform/ui/editor'

import { crmMetadataFormBehavior } from '../metadata/framework'
import CRMDocumentPartsEditor from './CRMDocumentPartsEditor.vue'
import { useCatalogEntityEditorPersistence } from './useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence } from './useDocumentEntityEditorPersistence'

defineOptions({ inheritAttrs: false })

const editorProps = withDefaults(defineProps<ConfiguredEntityEditorProps>(), {
  mode: 'page',
  canBack: true,
  initialFields: null,
  initialParts: null,
  expandTo: null,
  compactTo: null,
  closeTo: null,
  navigateOnCreate: undefined,
})

const configuration: ConfiguredEntityEditorConfiguration = {
  documentPartsExtensionKey: 'crm-document-parts',
  documentPartsEditor: markRaw(CRMDocumentPartsEditor),
  metadataFormBehavior: crmMetadataFormBehavior,
  createCatalogPersistence: useCatalogEntityEditorPersistence,
  createDocumentPersistence: useDocumentEntityEditorPersistence,
}
</script>

<template>
  <NgbConfiguredEntityEditor v-bind="{ ...editorProps, ...$attrs }" :configuration="configuration" />
</template>
