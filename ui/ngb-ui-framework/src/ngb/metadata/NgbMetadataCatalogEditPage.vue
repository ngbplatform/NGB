<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { buildCatalogCompactPageUrl, buildCatalogListUrl } from '../editor/catalogNavigation'
import type { MetadataCatalogEditPageProps } from './routePages'
import { normalizeEntityEditorIdRouteParam, normalizeRequiredRouteParam } from '../router/routeParams'

const props = withDefaults(defineProps<MetadataCatalogEditPageProps>(), {
  canBack: true,
  resolveCompactTo: null,
  resolveCloseTo: null,
})

const route = useRoute()

const catalogType = computed(() => normalizeRequiredRouteParam(route.params.catalogType))
const idParam = computed(() => normalizeEntityEditorIdRouteParam(route.params.id))

const compactTo = computed(() => {
  if (!catalogType.value) return null
  return props.resolveCompactTo?.(catalogType.value, idParam.value) ?? buildCatalogCompactPageUrl(catalogType.value, idParam.value)
})

const closeTo = computed(() => {
  if (!catalogType.value) return '/'
  return props.resolveCloseTo?.(catalogType.value) ?? buildCatalogListUrl(catalogType.value)
})
</script>

<template>
  <component
    :is="editorComponent"
    kind="catalog"
    :type-code="catalogType"
    :id="idParam"
    mode="page"
    :can-back="canBack"
    :compact-to="compactTo"
    :close-to="closeTo"
  />
</template>
