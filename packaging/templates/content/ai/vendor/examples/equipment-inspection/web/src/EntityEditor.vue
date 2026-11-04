<script setup lang="ts">
import { ref } from 'vue'
import { NgbConfiguredEntityEditor, type ConfiguredEntityEditorProps, type EntityEditorHandle } from '@ngbplatform/ui'
import { forwardEntityEditorHandle } from '@ngbplatform/ui/editor'
import { editorConfiguration } from './editor'

const props = withDefaults(defineProps<ConfiguredEntityEditorProps>(), {
  canBack: undefined,
  navigateOnCreate: undefined,
})
const editor = ref<EntityEditorHandle | null>(null)
defineExpose(forwardEntityEditorHandle(editor))
</script>

<template>
  <NgbConfiguredEntityEditor ref="editor" v-bind="props" :configuration="editorConfiguration" />
</template>
