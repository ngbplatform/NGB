<script setup lang="ts">
import { markRaw, ref } from 'vue'
import {
  NgbConfiguredEntityEditor,
  forwardEntityEditorHandle,
  type EntityEditorHandle,
  type ConfiguredEntityEditorConfiguration,
  type ConfiguredEntityEditorProps,
} from '@ngbplatform/ui/editor'

import { agencyBillingMetadataFormBehavior } from '../metadata/framework'
import AgencyBillingDocumentPartsEditor from './AgencyBillingDocumentPartsEditor.vue'
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

const editorRef = ref<EntityEditorHandle | null>(null)
defineExpose(forwardEntityEditorHandle(editorRef))

const configuration: ConfiguredEntityEditorConfiguration = {
  documentPartsExtensionKey: 'agency-billing-document-parts',
  documentPartsEditor: markRaw(AgencyBillingDocumentPartsEditor),
  metadataFormBehavior: agencyBillingMetadataFormBehavior,
  createCatalogPersistence: useCatalogEntityEditorPersistence,
  createDocumentPersistence: useDocumentEntityEditorPersistence,
}
</script>

<template>
  <NgbConfiguredEntityEditor ref="editorRef" v-bind="{ ...editorProps, ...$attrs }" :configuration="configuration" />
</template>
