<script setup lang="ts">
import { computed, ref, toRef, useId, watch } from 'vue'
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import { useAccessStore } from '../security/useAccessStore'
import { NGB_FEATURES } from '../features/types'
import { useFeatureStore } from '../features/useFeatureStore'
import { useObjectContent } from './useObjectContent'
import type { BusinessObjectRef, Note } from './types'
import type { DocumentHeaderActionGroup } from '../editor/types'

const props = withDefaults(defineProps<{
  target: BusinessObjectRef
  showActions?: boolean
}>(), {
  showActions: true,
})
const access = useAccessStore()
const features = useFeatureStore()
const capabilities = computed(() => ({
  attachments: features.isEnabled(NGB_FEATURES.attachments),
  notes: features.isEnabled(NGB_FEATURES.notes),
}))
const visibleCapabilities = computed(() => (['attachments', 'notes'] as const).filter(code => capabilities.value[code]))
const state = useObjectContent(toRef(props, 'target'), capabilities)
watch(() => props.target, () => {
  void features.load()
}, { immediate: true })
const { summary, drawer, attachments, notes, cursor, loading, busy, error, summaryError, completionId, uploadPhase } = state
const noteTextId = useId()
const text = ref('')
const editing = ref<Note | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const allowed = (capability: string, action: string) => access.current?.isActive === true
  && (access.current.isBootstrapAdmin || access.hasPermission(`system.${capability}.${action}`))

const actionGroups = computed<DocumentHeaderActionGroup[]>(() => {
  const items: DocumentHeaderActionGroup['items'] = visibleCapabilities.value.map(capability => ({
    key: `content.${capability}`,
    title: capability === 'attachments' ? 'Attachments' : 'Notes',
    icon: capability === 'attachments' ? 'paperclip' : 'sticky-note',
    disabled: !allowed(capability, 'read'),
    badge: summary.value[capability],
    ariaLabel: `${capability === 'attachments' ? 'Attachments' : 'Notes'} (${summary.value[capability] ?? 0})`,
  }))

  if (summaryError.value) {
    items.push({ key: 'content.retrySummary', title: 'Retry content counts', icon: 'refresh' })
  }

  if (features.error) {
    items.push({ key: 'content.retryFeatures', title: 'Retry available features', icon: 'refresh' })
  }

  return items.length ? [{ key: 'attachments-and-notes', label: 'Attachments & Notes', items }] : []
})

function handleAction(action: string): boolean {
  switch (action) {
    case 'content.attachments':
    case 'content.notes': {
      const capability = action === 'content.attachments' ? 'attachments' : 'notes'
      if (allowed(capability, 'read')) state.open(capability)
      return true
    }
    case 'content.retrySummary':
      void state.refreshSummary()
      return true
    case 'content.retryFeatures':
      void features.load(true)
      return true
    default:
      return false
  }
}

defineExpose({ actionGroups, handleAction })

function selectFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (file) void state.upload(file)
}

async function saveNote() {
  const saved = await state.saveNote(text.value, editing.value)
  if (saved) {
    text.value = ''
    editing.value = null
  }
}

function edit(note: Note) {
  editing.value = note
  text.value = note.text
}

function cancelEdit() {
  editing.value = null
  text.value = ''
}

watch(() => `${props.target.kind}:${props.target.typeCode}:${props.target.id}`, cancelEdit)

const date = (value: string) => new Date(value).toLocaleString()

function size(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1048576).toFixed(1)} MB`
}
</script>

<template>
  <div v-if="showActions && (visibleCapabilities.length || features.error)" class="flex shrink-0 items-center gap-2" data-testid="object-content-actions">
    <button v-for="capability in visibleCapabilities" :key="capability"
      class="ngb-iconbtn relative" :disabled="!allowed(capability, 'read')"
      :aria-label="`${capability === 'attachments' ? 'Attachments' : 'Notes'} (${summary[capability] ?? 0})`"
      :title="capability === 'attachments' ? 'Attachments' : 'Notes'" @click="state.open(capability)">
      <NgbIcon :name="capability === 'attachments' ? 'paperclip' : 'sticky-note'" />
      <span v-if="(summary[capability] ?? 0) > 0" class="absolute -right-1 -top-1 min-w-4 rounded-full bg-ngb-primary px-1 text-center text-[10px] leading-4 text-white" aria-hidden="true">{{ summary[capability] }}</span>
    </button>
    <button v-if="summaryError" class="text-xs text-ngb-danger" :title="summaryError" aria-label="Retry content counts" @click="state.refreshSummary()">Retry counts</button>
    <button v-if="features.error" class="text-xs text-ngb-danger" :title="features.error" aria-label="Retry available features" @click="features.load(true)">Retry features</button>
  </div>
  <NgbDrawer :open="drawer !== null" :title="drawer === 'attachments' ? 'Attachments' : 'Notes'" @update:open="state.close()">
    <div class="space-y-4 text-sm text-ngb-text">
      <div v-if="error" role="alert" class="rounded border border-ngb-danger p-3 text-ngb-danger">
        {{ error }}
        <button v-if="completionId" class="ngb-btn mt-2" :disabled="busy" @click="state.retryCompletion()">Retry completion</button>
        <button v-else class="ngb-btn mt-2" :disabled="busy" @click="state.load()">Reload list</button>
      </div>
      <template v-if="drawer === 'attachments'">
        <input ref="fileInput" type="file" class="sr-only" aria-label="Choose attachment" :disabled="busy || !allowed('attachments', 'create')" @change="selectFile" />
        <button class="ngb-btn" :disabled="busy || !allowed('attachments', 'create')" @click="fileInput?.click()">Upload file</button>
        <div v-if="uploadPhase" role="status" class="flex items-center gap-3">{{ uploadPhase }} <button class="ngb-btn" @click="state.cancelUpload()">Cancel upload</button></div>
        <ul class="space-y-3">
          <li v-for="attachment in attachments" :key="attachment.id" class="rounded border border-ngb-border p-3">
            <div class="break-all font-medium">{{ attachment.fileName }}</div>
            <div class="mt-1 break-all text-xs text-ngb-muted">{{ size(attachment.sizeBytes) }} · {{ attachment.contentType }}</div>
            <div class="mt-1 text-xs text-ngb-muted">{{ attachment.createdByDisplayName || attachment.createdByUserId }} · {{ date(attachment.createdAtUtc) }}</div>
            <div class="mt-3 flex gap-3">
              <button class="ngb-btn" :disabled="busy" @click="state.download(attachment.id)">Download</button>
              <button class="ngb-btn text-ngb-danger" :disabled="busy || !allowed('attachments', 'delete')" @click="state.deleteAttachment(attachment.id)">Delete</button>
            </div>
          </li>
        </ul>
        <p v-if="!loading && !attachments.length && !error" class="text-ngb-muted">No attachments yet.</p>
      </template>
      <template v-else-if="drawer === 'notes'">
        <form class="space-y-2" @submit.prevent="saveNote">
          <label :for="noteTextId" class="block font-medium">{{ editing ? 'Edit note' : 'Add note' }}</label>
          <textarea :id="noteTextId" v-model="text" rows="4" maxlength="100000" class="w-full rounded border border-ngb-border bg-ngb-bg p-3 text-ngb-text"
            :disabled="busy || !allowed('notes', editing ? 'update' : 'create')" />
          <div class="flex gap-2">
            <button type="submit" class="ngb-btn" :disabled="busy || !text.trim() || !allowed('notes', editing ? 'update' : 'create')">{{ editing ? 'Save note' : 'Add note' }}</button>
            <button v-if="editing" type="button" class="ngb-btn" :disabled="busy" @click="cancelEdit">Cancel edit</button>
          </div>
        </form>
        <ul class="space-y-3">
          <li v-for="note in notes" :key="note.id" class="rounded border border-ngb-border p-3">
            <div class="text-xs text-ngb-muted">{{ note.createdByDisplayName || note.createdByUserId }} · {{ date(note.createdAtUtc) }}</div>
            <p class="my-3 whitespace-pre-wrap break-words">{{ note.text }}</p>
            <div v-if="note.updatedAtUtc" class="mb-2 text-xs text-ngb-muted">Edited {{ date(note.updatedAtUtc) }}</div>
            <div class="flex gap-3">
              <button class="ngb-btn" :disabled="busy || !allowed('notes', 'update')" @click="edit(note)">Edit</button>
              <button class="ngb-btn text-ngb-danger" :disabled="busy || !allowed('notes', 'delete')" @click="state.deleteNote(note)">Delete</button>
            </div>
          </li>
        </ul>
        <p v-if="!loading && !notes.length && !error" class="text-ngb-muted">No notes yet.</p>
      </template>
      <p v-if="loading" role="status" class="text-ngb-muted">Loading…</p>
      <button v-if="cursor" class="ngb-btn" :disabled="loading || busy" @click="state.load(true)">Load more</button>
    </div>
  </NgbDrawer>
</template>
