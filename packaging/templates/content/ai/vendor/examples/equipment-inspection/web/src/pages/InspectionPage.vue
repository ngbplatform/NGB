<script setup lang="ts">
import { getDocumentPage, NgbMetadataDocumentListPage, type MetadataDocumentListPageLoadArgs } from '@ngbplatform/ui'
import EntityEditor from '../EntityEditor.vue'

function loadPage({ documentType, offset, limit, search, trashMode, periodFrom, periodTo, listFilters, signal }: MetadataDocumentListPageLoadArgs) {
  return getDocumentPage(documentType, {
    offset,
    limit,
    search,
    filters: {
      ...listFilters,
      trash: trashMode,
      ...(periodFrom ? { periodFrom } : {}),
      ...(periodTo ? { periodTo } : {}),
    },
  }, { signal })
}
</script>

<template>
  <NgbMetadataDocumentListPage :editor-component="EntityEditor" :load-page="loadPage" />
</template>
