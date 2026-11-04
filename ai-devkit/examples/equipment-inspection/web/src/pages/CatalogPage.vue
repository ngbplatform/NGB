<script setup lang="ts">
import { getCatalogPage, NgbMetadataCatalogListPage, type MetadataCatalogListPageLoadArgs } from '@ngbplatform/ui'
import EntityEditor from '../EntityEditor.vue'

function loadPage({ catalogType, offset, limit, search, trashMode, signal }: MetadataCatalogListPageLoadArgs) {
  return getCatalogPage(catalogType, { offset, limit, search, filters: { trash: trashMode } }, { signal })
}
</script>

<template>
  <NgbMetadataCatalogListPage :editor-component="EntityEditor" :load-page="loadPage" />
</template>
