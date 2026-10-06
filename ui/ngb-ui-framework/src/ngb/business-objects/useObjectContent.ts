import { onBeforeUnmount, ref, watch, type Ref } from 'vue'
import { toErrorMessage } from '../utils/errorMessage'
import { contentApi, startAttachmentDownload, uploadBytes } from './api'
import type { Attachment, BusinessObjectRef, ContentSummary, Note } from './types'

type ContentCapabilities = { attachments: boolean; notes: boolean }
type ContentCapability = keyof ContentCapabilities

export function useObjectContent(target: Ref<BusinessObjectRef>, capabilities: Ref<ContentCapabilities>) {
  const summary = ref<ContentSummary>({ attachments: null, notes: null })
  const drawer = ref<ContentCapability | null>(null)
  const attachments = ref<Attachment[]>([])
  const notes = ref<Note[]>([])
  const cursor = ref<string | null>(null)
  const loading = ref(false)
  const busy = ref(false)
  const error = ref('')
  const summaryError = ref('')
  const completionId = ref<string | null>(null)
  const uploadPhase = ref('')
  let lifetime = new AbortController()
  let listRequest: AbortController | null = null
  let operation: AbortController | null = null
  let generation = 0
  let summaryGeneration = 0

  async function refreshSummary() {
    const current = ++summaryGeneration
    if (!capabilities.value.attachments && !capabilities.value.notes) return

    try {
      const result = await contentApi.summary(target.value, lifetime.signal)
      if (current === summaryGeneration) {
        summary.value = {
          attachments: capabilities.value.attachments ? result.attachments : null,
          notes: capabilities.value.notes ? result.notes : null,
        }
        summaryError.value = ''
      }
    } catch (cause) {
      if (!lifetime.signal.aborted && current === summaryGeneration) {
        summaryError.value = toErrorMessage(cause, 'Could not load counts')
      }
    }
  }

  async function load(more = false) {
    if (!drawer.value) return
    listRequest?.abort()
    const controller = new AbortController()
    listRequest = controller
    const signal = AbortSignal.any([controller.signal, lifetime.signal])
    const active = drawer.value
    loading.value = true
    error.value = ''

    try {
      const after = more ? cursor.value : null
      if (active === 'attachments') {
        const result = await contentApi.attachments(target.value, after, signal)
        if (signal.aborted) return
        attachments.value = more ? [...attachments.value, ...result.items] : result.items
        cursor.value = result.nextCursor
      } else {
        const result = await contentApi.notes(target.value, after, signal)
        if (signal.aborted) return
        notes.value = more ? [...notes.value, ...result.items] : result.items
        cursor.value = result.nextCursor
      }
    } catch (cause) {
      if (!signal.aborted) error.value = toErrorMessage(cause, 'Could not load content')
    } finally {
      if (listRequest === controller) loading.value = false
    }
  }

  function open(value: ContentCapability) {
    if (!capabilities.value[value]) return
    drawer.value = value
    cursor.value = null
    void load()
  }

  function close() {
    listRequest?.abort()
    drawer.value = null
  }

  async function mutate(
    capability: ContentCapability,
    action: (signal: AbortSignal) => Promise<void>,
    refresh = true,
  ) {
    if (busy.value || !capabilities.value[capability]) return false
    const current = generation
    operation = new AbortController()
    const signal = AbortSignal.any([operation.signal, lifetime.signal])
    busy.value = true
    error.value = ''

    try {
      await action(signal)
      if (signal.aborted) return false
      if (refresh) {
        await refreshSummary()
        await load()
      }
      return true
    } catch (cause) {
      if (!signal.aborted) error.value = toErrorMessage(cause, 'Operation failed. Please retry.')
      return false
    } finally {
      if (current === generation) {
        busy.value = false
        uploadPhase.value = ''
      }
    }
  }

  async function upload(file: File) {
    await mutate('attachments', async signal => {
      completionId.value = null
      uploadPhase.value = 'Preparing upload…'
      const uploadTarget = await contentApi.requestUpload(target.value, file, signal)
      if (signal.aborted) return
      uploadPhase.value = 'Uploading…'
      await uploadBytes(uploadTarget, file, signal)
      if (signal.aborted) return
      completionId.value = uploadTarget.attachmentId
      uploadPhase.value = 'Verifying upload…'
      await contentApi.complete(uploadTarget.attachmentId, signal)
      if (!signal.aborted) completionId.value = null
    })
  }

  async function retryCompletion() {
    const id = completionId.value
    if (!id) return
    await mutate('attachments', async signal => {
      await contentApi.complete(id, signal)
      if (!signal.aborted) completionId.value = null
    })
  }

  function cancelUpload() {
    operation?.abort()
    uploadPhase.value = ''
    error.value = 'Upload cancelled. Only verified files appear in the list.'
  }

  function deleteAttachment(id: string) {
    return mutate('attachments', async signal => {
      await contentApi.deleteAttachment(id, signal)
    })
  }

  function download(id: string) {
    return mutate('attachments', async signal => {
      const result = await contentApi.download(id, signal)
      if (!signal.aborted) startAttachmentDownload(result.url)
    }, false)
  }

  function saveNote(text: string, note: Note | null) {
    return mutate('notes', async signal => {
      if (note) await contentApi.updateNote(note, text, signal)
      else await contentApi.createNote(target.value, text, signal)
    })
  }

  function deleteNote(note: Note) {
    return mutate('notes', async signal => {
      await contentApi.deleteNote(note, signal)
    })
  }

  watch([
    () => `${target.value.kind}:${target.value.typeCode}:${target.value.id}`,
    () => capabilities.value.attachments,
    () => capabilities.value.notes,
  ], () => {
    lifetime.abort()
    listRequest?.abort()
    operation?.abort()
    lifetime = new AbortController()
    generation++
    summaryGeneration++
    attachments.value = []
    notes.value = []
    cursor.value = null
    drawer.value = null
    summary.value = { attachments: null, notes: null }
    error.value = ''
    summaryError.value = ''
    busy.value = false
    loading.value = false
    uploadPhase.value = ''
    completionId.value = null
    void refreshSummary()
  }, { immediate: true })

  onBeforeUnmount(() => {
    generation++
    summaryGeneration++
    lifetime.abort()
    listRequest?.abort()
    operation?.abort()
  })

  return {
    summary, drawer, attachments, notes, cursor, loading, busy, error, summaryError,
    completionId, uploadPhase, open, close, load, upload, retryCompletion, cancelUpload,
    deleteAttachment, download, saveNote, deleteNote, refreshSummary,
  }
}
