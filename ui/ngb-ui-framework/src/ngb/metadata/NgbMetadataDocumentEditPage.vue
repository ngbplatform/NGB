<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { buildEntityFallbackCloseTarget } from '../editor/documentNavigation'
import { buildDocumentCompactPageUrl } from '../editor/documentNavigation'
import { resolveCompactDocumentSourceTarget } from '../editor/documentNavigation'
import { readDocumentCopyDraft } from '../editor/documentCopyDraft'
import type { MetadataDocumentEditPageProps } from './routePages'
import type { EntityFormModel, RecordPayload } from './types'
import { normalizeEntityEditorIdRouteParam, normalizeRequiredRouteParam } from '../router/routeParams'

const props = withDefaults(defineProps<MetadataDocumentEditPageProps>(), {
  canBack: true,
  resolveCompactTo: null,
  resolveCloseTo: null,
})

const route = useRoute()

function normalizeQueryString(value: unknown): string | null {
  if (Array.isArray(value)) return normalizeQueryString(value[0])
  const normalized = String(value ?? '').trim()
  return normalized || null
}

const documentType = computed(() => normalizeRequiredRouteParam(route.params.documentType))
const idParam = computed(() => normalizeEntityEditorIdRouteParam(route.params.id))

const compactTo = computed(() => {
  if (!documentType.value) return null

  const fallbackCompactTarget =
    props.resolveCompactTo?.(documentType.value, idParam.value)
    ?? buildDocumentCompactPageUrl(documentType.value, idParam.value)

  return resolveCompactDocumentSourceTarget(route, fallbackCompactTarget) ?? fallbackCompactTarget
})

const closeTo = computed(() => {
  if (!documentType.value) return '/'
  return props.resolveCloseTo?.(documentType.value) ?? buildEntityFallbackCloseTarget('document', documentType.value)
})

const copyDraftToken = computed(() => (!idParam.value ? normalizeQueryString(route.query.copyDraft) : null))
const copyDraftSnapshot = computed(() =>
  (!idParam.value && documentType.value)
    ? readDocumentCopyDraft(copyDraftToken.value, documentType.value)
    : null,
)
const initialFields = computed<EntityFormModel | null>(() => copyDraftSnapshot.value?.fields ?? null)
const initialParts = computed<RecordPayload['parts'] | null>(() => copyDraftSnapshot.value?.parts ?? null)
</script>

<template>
  <component
    :is="editorComponent"
    kind="document"
    :type-code="documentType"
    :id="idParam"
    mode="page"
    :can-back="canBack"
    :initial-fields="initialFields"
    :initial-parts="initialParts"
    :compact-to="compactTo"
    :close-to="closeTo"
  />
</template>
