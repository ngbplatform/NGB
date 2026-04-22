<template>
  <div class="px-5 py-8">
    <template v-if="error">
      <div
        role="alert"
        aria-live="assertive"
        class="rounded-[var(--ngb-radius)] border border-[rgba(185,28,28,.18)] bg-[rgba(254,242,242,.9)] px-4 py-3"
      >
        <div class="text-sm font-semibold text-[#991b1b]">Remote search is temporarily unavailable</div>
        <div class="mt-1 text-sm text-[#7f1d1d]">{{ error }}</div>
      </div>
    </template>

    <template v-else-if="loading">
      <div role="status" aria-live="polite">
        <div class="text-sm font-semibold text-ngb-text">Searching records…</div>
        <div class="mt-2 text-sm text-ngb-muted">Pages, commands, and cached reports stay available while remote results load.</div>
      </div>
    </template>

    <template v-else-if="!query">
      <div class="text-sm font-semibold text-ngb-text">Search pages, records, reports, or run a command</div>
      <div class="mt-2 text-sm text-ngb-muted">
        Try <span class="font-medium text-ngb-text">trial balance</span> or <span class="font-medium text-ngb-text">create journal entry</span>.
      </div>
      <div class="mt-4 flex flex-wrap gap-2 text-xs text-ngb-muted">
        <span class="ngb-kbd">&gt;</span><span class="mr-3">Commands</span>
        <span class="ngb-kbd">/</span><span class="mr-3">Pages</span>
        <span class="ngb-kbd">#</span><span class="mr-3">Reports</span>
        <span class="ngb-kbd">:</span><span class="mr-3">Documents</span>
        <span class="ngb-kbd">@</span><span>Catalogs</span>
      </div>
    </template>

    <template v-else>
      <div class="text-sm font-semibold text-ngb-text">No results for “{{ query }}”</div>
      <div class="mt-2 text-sm text-ngb-muted">Try a document number, a page name, or a scope prefix to narrow the search.</div>
      <div class="mt-4 flex flex-wrap gap-2 text-xs text-ngb-muted">
        <span class="ngb-kbd">&gt;</span><span class="mr-3">Commands</span>
        <span class="ngb-kbd">/</span><span class="mr-3">Pages</span>
        <span class="ngb-kbd">#</span><span class="mr-3">Reports</span>
        <span class="ngb-kbd">:</span><span class="mr-3">Documents</span>
        <span class="ngb-kbd">@</span><span>Catalogs</span>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  query: string
  loading: boolean
  error?: string | null
}>()
</script>
