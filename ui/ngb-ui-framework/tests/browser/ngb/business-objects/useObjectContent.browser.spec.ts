import { beforeEach, expect, test, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { render } from 'vitest-browser-vue'
import { useObjectContent } from '../../../../src/ngb/business-objects/useObjectContent'
import { contentApi, uploadBytes, startAttachmentDownload } from '../../../../src/ngb/business-objects/api'
import type { Attachment, BusinessObjectRef, Note } from '../../../../src/ngb/business-objects/types'
vi.mock('../../../../src/ngb/business-objects/api', () => ({
  contentApi: { summary: vi.fn(), attachments: vi.fn(), notes: vi.fn(), requestUpload: vi.fn(), complete: vi.fn(), download: vi.fn(), deleteAttachment: vi.fn(), createNote: vi.fn(), updateNote: vi.fn(), deleteNote: vi.fn() },
  uploadBytes: vi.fn(), startAttachmentDownload: vi.fn(),
}))
const targetValue: BusinessObjectRef = { kind: 'CatalogItem', typeCode: 'catalog', id: 'id' }
const attachment = { id: 'uploaded' } as Attachment
const note = { id: 'note', version: 1, text: 'text' } as Note
function setup() {
  const target = ref(targetValue)
  let state!: ReturnType<typeof useObjectContent>
  const component = render(defineComponent({ setup() { state = useObjectContent(target); return () => h('div') } }))
  return { state, target, component }
}
function deferred<T>() {
  let resolve!: (value: T) => void; let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(contentApi.summary).mockResolvedValue({ attachments: 0, notes: 0 })
  vi.mocked(contentApi.attachments).mockResolvedValue({ items: [], nextCursor: null })
  vi.mocked(contentApi.notes).mockResolvedValue({ items: [], nextCursor: null })
  vi.mocked(contentApi.requestUpload).mockResolvedValue({ attachmentId: 'uploaded', url: 'https://storage/upload', expiresAtUtc: 'soon', headers: {} })
  vi.mocked(contentApi.complete).mockResolvedValue(attachment)
  vi.mocked(contentApi.download).mockResolvedValue({ url: 'https://storage/file', expiresAtUtc: 'soon' })
})
test('upload completes only after direct PUT and allows safe completion retry without repeating PUT', async () => {
  const { state } = setup(); state.open('attachments')
  vi.mocked(contentApi.complete).mockRejectedValueOnce(new Error('Completion unavailable'))
  await state.upload(new File(['abc'], 'test.txt'))
  expect(state.completionId.value).toBe('uploaded'); expect(state.error.value).toContain('Completion unavailable')
  expect(uploadBytes).toHaveBeenCalledTimes(1)
  expect(contentApi.summary).toHaveBeenCalledTimes(1)
  await state.retryCompletion()
  expect(state.completionId.value).toBeNull(); expect(state.error.value).toBe('')
  expect(uploadBytes).toHaveBeenCalledTimes(1)
  await state.retryCompletion()
  expect(contentApi.complete).toHaveBeenCalledTimes(2)
})
test('failed PUT never completes and failed metadata creation never sends bytes', async () => {
  const { state } = setup()
  vi.mocked(uploadBytes).mockRejectedValueOnce(new Error('PUT failed'))
  await state.upload(new File(['abc'], 'test.txt'))
  expect(state.error.value).toBe('PUT failed'); expect(contentApi.complete).not.toHaveBeenCalled()
  vi.mocked(contentApi.requestUpload).mockRejectedValueOnce(new Error('Request failed'))
  await state.upload(new File(['abc'], 'test.txt'))
  expect(state.error.value).toBe('Request failed'); expect(uploadBytes).toHaveBeenCalledTimes(1)
})
test('notes mutate independently, paginate and downloads only start after explicit request', async () => {
  const { state } = setup(); state.open('notes')
  await state.saveNote('new', null); await state.saveNote('edited', note)
  expect(contentApi.createNote).toHaveBeenCalled(); expect(contentApi.updateNote).toHaveBeenCalled()
  vi.mocked(contentApi.notes).mockResolvedValueOnce({ items: [note], nextCursor: 'next' })
  await state.load()
  vi.mocked(contentApi.notes).mockResolvedValueOnce({ items: [{ ...note, id: 'second' }], nextCursor: null })
  await state.load(true); expect(state.notes.value).toHaveLength(2)
  await state.deleteNote(note); await state.deleteAttachment('file')
  await state.download('file'); expect(startAttachmentDownload).toHaveBeenCalledWith('https://storage/file')
  state.close(); await state.load()
  expect(state.drawer.value).toBeNull()
})
test('cancel and unmount abort uploads and prevent a late completion from affecting another object', async () => {
  const { state, target, component } = setup()
  const pending = deferred<void>()
  vi.mocked(uploadBytes).mockReturnValueOnce(pending.promise)
  const upload = state.upload(new File(['x'], 'x'))
  await expect.poll(() => vi.mocked(uploadBytes).mock.calls.length).toBe(1)
  const signal = vi.mocked(uploadBytes).mock.calls[0]![2]
  await state.deleteAttachment('cannot-run-while-busy')
  expect(contentApi.deleteAttachment).not.toHaveBeenCalled()
  state.cancelUpload(); expect(signal.aborted).toBe(true)
  pending.resolve(); await upload; expect(contentApi.complete).not.toHaveBeenCalled()
  const creating = deferred<Awaited<ReturnType<typeof contentApi.requestUpload>>>()
  vi.mocked(contentApi.requestUpload).mockReturnValueOnce(creating.promise)
  const next = state.upload(new File(['x'], 'x'))
  target.value = { ...target.value, id: 'new-object' }
  await expect.poll(() => state.busy.value).toBe(false)
  creating.resolve({ attachmentId: 'stale', url: 'https://storage/upload', headers: {}, expiresAtUtc: 'soon' })
  await next; expect(state.completionId.value).toBeNull()
  component.unmount()
})
test('late and aborted list responses cannot overwrite a new target or a newer request', async () => {
  const { state, target } = setup()
  const list = deferred<Awaited<ReturnType<typeof contentApi.attachments>>>()
  vi.mocked(contentApi.attachments).mockReturnValueOnce(list.promise)
  state.open('attachments')
  state.open('notes')
  list.resolve({ items: [attachment], nextCursor: 'stale' })
  await expect.poll(() => state.loading.value).toBe(false)
  expect(state.attachments.value).toEqual([])
  const notes = deferred<Awaited<ReturnType<typeof contentApi.notes>>>()
  vi.mocked(contentApi.notes).mockReturnValueOnce(notes.promise)
  const loading = state.load()
  target.value = { ...target.value, id: 'changed' }
  await expect.poll(() => state.drawer.value).toBeNull()
  notes.resolve({ items: [note], nextCursor: null }); await loading
  expect(state.notes.value).toEqual([])
})
test('summary failure is separate from resource errors and can be retried', async () => {
  vi.mocked(contentApi.summary).mockRejectedValueOnce(new Error('Counts failed'))
  const { state } = setup()
  await expect.poll(() => state.summaryError.value).toBe('Counts failed')
  await state.refreshSummary(); expect(state.summaryError.value).toBe('')
  state.open('attachments'); await state.load()
  vi.mocked(contentApi.deleteAttachment).mockRejectedValueOnce(new Error('Delete failed'))
  await state.deleteAttachment('file'); expect(state.error.value).toBe('Delete failed')
  vi.mocked(contentApi.attachments).mockRejectedValueOnce(new Error('List failed'))
  await state.load(); expect(state.error.value).toBe('List failed')
})

test('older summary successes and errors cannot replace the latest counts for the same parent', async () => {
  const initial = deferred<Awaited<ReturnType<typeof contentApi.summary>>>()
  vi.mocked(contentApi.summary).mockReturnValueOnce(initial.promise)
  const { state } = setup()
  vi.mocked(contentApi.summary).mockResolvedValue({ attachments: 1, notes: 2 })
  await state.refreshSummary()
  initial.resolve({ attachments: 0, notes: 0 })
  await initial.promise
  expect(state.summary.value).toEqual({ attachments: 1, notes: 2 })
  const obsolete = deferred<Awaited<ReturnType<typeof contentApi.summary>>>()
  vi.mocked(contentApi.summary).mockReturnValueOnce(obsolete.promise)
  const earlierRefresh = state.refreshSummary()
  await state.refreshSummary()
  obsolete.reject(new Error('Obsolete request failed'))
  await earlierRefresh
  expect(state.summaryError.value).toBe('')
  expect(state.summary.value).toEqual({ attachments: 1, notes: 2 })
})

test('successful upload refreshes resources and cancelled verification never restores completion state', async () => {
  const { state, target } = setup()
  await state.upload(new File(['x'], 'x'))
  expect(state.completionId.value).toBeNull()
  for (const retry of [false, true]) {
    if (retry) {
      vi.mocked(contentApi.complete).mockRejectedValueOnce(new Error('retry'))
      await state.upload(new File(['x'], 'x'))
    }
    const completing = deferred<Attachment>()
    vi.mocked(contentApi.complete).mockReturnValueOnce(completing.promise)
    const task = retry ? state.retryCompletion() : state.upload(new File(['x'], 'x'))
    await expect.poll(() => state.completionId.value).toBe('uploaded')
    state.cancelUpload(); completing.resolve(attachment); await task
  }
  const downloading = deferred<Awaited<ReturnType<typeof contentApi.download>>>()
  vi.mocked(contentApi.download).mockReturnValueOnce(downloading.promise)
  const task = state.download('file')
  target.value = { ...target.value, id: 'other' }
  await expect.poll(() => state.busy.value).toBe(false)
  downloading.resolve({ url: 'https://storage/file', expiresAtUtc: 'soon' }); await task
  expect(startAttachmentDownload).not.toHaveBeenCalled()
})
test('aborted requests ignore late errors and old summaries cannot leak across parents', async () => {
  const summary = deferred<Awaited<ReturnType<typeof contentApi.summary>>>()
  vi.mocked(contentApi.summary).mockReturnValueOnce(summary.promise)
  const { state, target } = setup()
  target.value = { ...target.value, id: 'other' }
  await expect.poll(() => vi.mocked(contentApi.summary).mock.calls.length).toBe(2)
  summary.resolve({ attachments: 999, notes: 999 })
  await Promise.resolve(); expect(state.summary.value.attachments).toBe(0)
  const failedSummary = deferred<Awaited<ReturnType<typeof contentApi.summary>>>()
  vi.mocked(contentApi.summary).mockReturnValueOnce(failedSummary.promise)
  const refreshing = state.refreshSummary()
  target.value = { ...target.value, id: 'third' }
  await expect.poll(() => vi.mocked(contentApi.summary).mock.calls.length).toBe(4)
  failedSummary.reject(new Error('old summary')); await refreshing
  expect(state.summaryError.value).toBe('')
  const listing = deferred<Awaited<ReturnType<typeof contentApi.attachments>>>()
  vi.mocked(contentApi.attachments).mockReturnValueOnce(listing.promise)
  state.open('attachments'); state.close(); listing.reject(new Error('aborted list'))
  await Promise.resolve(); expect(state.error.value).toBe('')
  const deleting = deferred<void>()
  vi.mocked(contentApi.deleteAttachment).mockReturnValueOnce(deleting.promise)
  const deletingTask = state.deleteAttachment('file'); state.cancelUpload()
  deleting.reject(new Error('aborted delete')); await deletingTask
  expect(state.error.value).toContain('Upload cancelled')
})
