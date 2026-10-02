import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { page } from 'vitest/browser'
import NgbObjectContent from '../../../../src/ngb/business-objects/NgbObjectContent.vue'
import { contentApi, uploadBytes, startAttachmentDownload } from '../../../../src/ngb/business-objects/api'
import type { Attachment, Note } from '../../../../src/ngb/business-objects/types'

const features = vi.hoisted(() => ({ isEnabled: vi.fn((_code: string) => true), load: vi.fn(), error: '' }))
vi.mock('../../../../src/ngb/features/useFeatureStore', () => ({ useFeatureStore: () => features }))

const access = vi.hoisted(() => ({ current: { isActive: true, isBootstrapAdmin: true }, hasPermission: vi.fn(() => false) }))
vi.mock('../../../../src/ngb/security/useAccessStore', () => ({ useAccessStore: () => access }))
vi.mock('../../../../src/ngb/business-objects/api', () => ({
  contentApi: { summary: vi.fn(), attachments: vi.fn(), notes: vi.fn(), requestUpload: vi.fn(), complete: vi.fn(), download: vi.fn(), deleteAttachment: vi.fn(), createNote: vi.fn(), updateNote: vi.fn(), deleteNote: vi.fn() },
  uploadBytes: vi.fn(), startAttachmentDownload: vi.fn(),
}))
const target = { kind: 'Document', typeCode: 'invoice', id: 'object-id' } as const
const attachment: Attachment = { id: 'attachment-id', fileName: 'invoice.txt', sizeBytes: 1024, contentType: 'text/plain', createdAtUtc: '2026-10-01T12:00:00Z', createdByUserId: 'author-id', createdByDisplayName: 'Alice' }
const note: Note = { id: 'note-id', text: '<b>Plain text</b>', version: 1, createdAtUtc: '2026-10-01T12:00:00Z', createdByUserId: 'author-id', createdByDisplayName: 'Alice', updatedAtUtc: null, updatedByUserId: null }
beforeEach(() => {
  vi.resetAllMocks()
  features.isEnabled.mockReturnValue(true)
  features.error = ''
  access.current.isBootstrapAdmin = true
  access.current.isActive = true
  vi.mocked(contentApi.summary).mockResolvedValue({ attachments: 0, notes: 0 })
  vi.mocked(contentApi.attachments).mockResolvedValue({ items: [], nextCursor: null })
  vi.mocked(contentApi.notes).mockResolvedValue({ items: [], nextCursor: null })
  vi.mocked(contentApi.requestUpload).mockResolvedValue({ attachmentId: 'new-id', url: 'https://storage/upload', expiresAtUtc: '2026-10-01T12:10:00Z', headers: {} })
  vi.mocked(contentApi.complete).mockResolvedValue(attachment)
  vi.mocked(contentApi.createNote).mockResolvedValue(note)
})

test('accessible header icons hide zero badges, load on demand and open and close the shared drawer', async () => {
  const screen = render(NgbObjectContent, { props: { target } })
  await expect.element(screen.getByRole('button', { name: 'Attachments (0)' })).toBeVisible()
  expect(contentApi.attachments).not.toHaveBeenCalled()
  expect(screen.container.querySelectorAll('[aria-hidden="true"] span')).toHaveLength(0)
  await screen.getByRole('button', { name: 'Attachments (0)' }).click()
  await expect.element(page.getByText('No attachments yet.')).toBeVisible()
  await page.getByRole('button', { name: 'Close', exact: true }).click()
  await screen.getByRole('button', { name: 'Notes (0)' }).click()
  await expect.element(page.getByText('No notes yet.')).toBeVisible()
  expect(uploadBytes).not.toHaveBeenCalled(); expect(contentApi.download).not.toHaveBeenCalled()
})
test('nonzero badges, paging, metadata and explicit native download without preview', async () => {
  vi.mocked(contentApi.summary).mockResolvedValue({ attachments: 2, notes: 1 })
  vi.mocked(contentApi.attachments).mockResolvedValueOnce({ items: [attachment], nextCursor: 'last' })
    .mockResolvedValue({ items: [{ ...attachment, id: 'second', fileName: 'other.txt', sizeBytes: 1048576, createdByDisplayName: null }], nextCursor: null })
  vi.mocked(contentApi.download).mockResolvedValue({ url: 'https://storage/file', expiresAtUtc: 'soon' })
  const screen = render(NgbObjectContent, { props: { target } })
  await expect.element(screen.getByRole('button', { name: 'Attachments (2)' })).toBeVisible()
  await screen.getByRole('button', { name: 'Attachments (2)' }).click()
  await expect.element(page.getByText('invoice.txt')).toBeVisible()
  await page.getByRole('button', { name: 'Download', exact: true }).click()
  expect(startAttachmentDownload).toHaveBeenCalledWith('https://storage/file')
  await page.getByRole('button', { name: 'Load more' }).click()
  await expect.element(page.getByText('other.txt')).toBeVisible()
  expect(contentApi.attachments).toHaveBeenLastCalledWith(target, 'last', expect.any(AbortSignal))
  expect(document.querySelector('iframe, video, object, embed')).toBeNull()
})
test('notes create, edit and delete plain text using the note version', async () => {
  const screen = render(NgbObjectContent, { props: { target } })
  await screen.getByRole('button', { name: 'Notes (0)' }).click()
  await page.getByRole('textbox', { name: 'Add note' }).fill('new note')
  vi.mocked(contentApi.notes).mockResolvedValue({ items: [note], nextCursor: null })
  await page.getByRole('button', { name: 'Add note', exact: true }).click()
  await expect.element(page.getByText('<b>Plain text</b>')).toBeVisible()
  expect(document.querySelector('b')).toBeNull()
  await page.getByRole('button', { name: 'Edit', exact: true }).click()
  await page.getByRole('textbox', { name: 'Edit note' }).fill('edited')
  await page.getByRole('button', { name: 'Save note' }).click()
  expect(contentApi.updateNote).toHaveBeenCalledWith(note, 'edited', expect.any(AbortSignal))
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  expect(contentApi.deleteNote).toHaveBeenCalledWith(note, expect.any(AbortSignal))
})
test('read and write permissions disable actions', async () => {
  access.current.isBootstrapAdmin = false
  const screen = render(NgbObjectContent, { props: { target } })
  await expect.element(screen.getByRole('button', { name: 'Attachments (0)' })).toBeDisabled()
  await expect.element(screen.getByRole('button', { name: 'Notes (0)' })).toBeDisabled()
})
test('list errors can be retried and loading is visible', async () => {
  vi.mocked(contentApi.attachments).mockRejectedValueOnce(new Error('Storage metadata unavailable'))
  const screen = render(NgbObjectContent, { props: { target } })
  await screen.getByRole('button', { name: 'Attachments (0)' }).click()
  await expect.element(page.getByRole('alert')).toHaveTextContent('Storage metadata unavailable')
  await page.getByRole('button', { name: 'Reload list' }).click()
  await expect.element(page.getByText('No attachments yet.')).toBeVisible()
})
test('unmount cancels outstanding requests', async () => {
  vi.mocked(contentApi.summary).mockImplementation(() => new Promise(() => {}))
  const screen = render(NgbObjectContent, { props: { target } })
  const signal = vi.mocked(contentApi.summary).mock.calls[0]![1]
  expect(signal.aborted).toBe(false)
  screen.unmount()
  expect(signal.aborted).toBe(true)
})

test('upload input handles selection, completion failure, retry and cancellation', async () => {
  const screen = render(NgbObjectContent, { props: { target } })
  await screen.getByRole('button', { name: 'Attachments (0)' }).click()
  const input = document.querySelector<HTMLInputElement>('input[type=file]')!
  const click = vi.spyOn(input, 'click').mockImplementation(() => {})
  await page.getByRole('button', { name: 'Upload file', exact: true }).click()
  expect(click).toHaveBeenCalledOnce()
  input.dispatchEvent(new Event('change', { bubbles: true }))
  expect(contentApi.requestUpload).not.toHaveBeenCalled()
  const files = new DataTransfer(); files.items.add(new File(['x'], 'file.txt'))
  input.files = files.files
  vi.mocked(contentApi.complete).mockRejectedValueOnce(new Error('Complete failed'))
  input.dispatchEvent(new Event('change', { bubbles: true }))
  await expect.element(page.getByRole('button', { name: 'Retry completion' })).toBeVisible()
  await page.getByRole('button', { name: 'Retry completion' }).click()
  await expect.element(page.getByText('No attachments yet.')).toBeVisible()
  let finish!: () => void
  vi.mocked(uploadBytes).mockImplementationOnce(() => new Promise<void>(resolve => { finish = resolve }))
  await expect.element(page.getByRole('button', { name: 'Upload file', exact: true })).toBeEnabled()
  const nextFiles = new DataTransfer(); nextFiles.items.add(new File(['y'], 'second.txt'))
  input.files = nextFiles.files; input.dispatchEvent(new Event('change', { bubbles: true }))
  await expect.element(page.getByRole('button', { name: 'Cancel upload' })).toBeVisible()
  await page.getByRole('button', { name: 'Cancel upload' }).click()
  finish()
  await expect.element(page.getByRole('alert')).toHaveTextContent('Upload cancelled')
  click.mockRestore()
})
test('delete attachment refreshes counts, and notes show author fallback and edit time', async () => {
  vi.mocked(contentApi.attachments).mockResolvedValue({ items: [{ ...attachment, sizeBytes: 5 }], nextCursor: null })
  vi.mocked(contentApi.notes).mockResolvedValue({ items: [{ ...note, createdByDisplayName: null, updatedAtUtc: note.createdAtUtc }], nextCursor: null })
  const screen = render(NgbObjectContent, { props: { target } })
  await screen.getByRole('button', { name: 'Attachments (0)' }).click()
  await expect.element(page.getByText('5 B · text/plain')).toBeVisible()
  await page.getByRole('button', { name: 'Delete', exact: true }).click()
  expect(contentApi.deleteAttachment).toHaveBeenCalledWith(attachment.id, expect.any(AbortSignal))
  await page.getByRole('button', { name: 'Close', exact: true }).click()
  await screen.getByRole('button', { name: 'Notes (0)' }).click()
  await expect.element(page.getByText(/Edited /)).toBeVisible()
  await page.getByRole('button', { name: 'Edit', exact: true }).click()
  await page.getByRole('button', { name: 'Cancel edit' }).click()
  await expect.element(page.getByRole('textbox', { name: 'Add note' })).toHaveValue('')
  await page.getByRole('textbox', { name: 'Add note' }).fill('retain failed edit')
  vi.mocked(contentApi.createNote).mockRejectedValueOnce(new Error('Conflict'))
  await page.getByRole('button', { name: 'Add note', exact: true }).click()
  await expect.element(page.getByRole('textbox', { name: 'Add note' })).toHaveValue('retain failed edit')
})
test('summary retries, granted read permission and inactive sessions are represented', async () => {
  access.current.isBootstrapAdmin = false; access.hasPermission.mockReturnValue(true)
  vi.mocked(contentApi.summary).mockRejectedValueOnce(new Error('Unavailable'))
  const screen = render(NgbObjectContent, { props: { target } })
  await expect.element(screen.getByRole('button', { name: 'Retry content counts' })).toBeVisible()
  await screen.getByRole('button', { name: 'Retry content counts' }).click()
  await expect.element(screen.getByRole('button', { name: 'Attachments (0)' })).toBeEnabled()
  screen.unmount(); access.current.isActive = false
  const disabled = render(NgbObjectContent, { props: { target } })
  await expect.element(disabled.getByRole('button', { name: 'Notes (0)' })).toBeDisabled()
})

test('mobile dark drawer contains long plain text and closes with Escape', async () => {
  await page.viewport(320, 740)
  document.documentElement.classList.add('dark')
  try {
    vi.mocked(contentApi.notes).mockResolvedValue({ items: [{ ...note, text: 'x'.repeat(300) }], nextCursor: null })
    const screen = render(NgbObjectContent, { props: { target } })
    await screen.getByRole('button', { name: 'Notes (0)' }).click()
    await expect.element(page.getByRole('textbox', { name: 'Add note' })).toBeVisible()
    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(window.innerWidth + 1)
    await page.getByRole('textbox', { name: 'Add note' }).fill('Keyboard-accessible note')
    document.activeElement?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await expect.element(page.getByRole('textbox', { name: 'Add note' })).not.toBeInTheDocument()
  } finally {
    document.documentElement.classList.remove('dark')
    await page.viewport(1440, 900)
  }
})

test('disabled features hide all actions and make no content requests even for administrators', async () => {
  features.isEnabled.mockReturnValue(false)
  const screen = render(NgbObjectContent, { props: { target } })
  await expect.element(screen.getByTestId('object-content-actions')).not.toBeInTheDocument()
  expect(contentApi.summary).not.toHaveBeenCalled()
  expect(contentApi.attachments).not.toHaveBeenCalled()
  expect(contentApi.notes).not.toHaveBeenCalled()
})

test('notes can be enabled independently of attachments', async () => {
  features.isEnabled.mockImplementation(code => code === 'Notes')
  const screen = render(NgbObjectContent, { props: { target } })
  await expect.element(screen.getByRole('button', { name: 'Notes (0)' })).toBeVisible()
  await expect.element(screen.getByRole('button', { name: 'Attachments (0)' })).not.toBeInTheDocument()
  await screen.getByRole('button', { name: 'Notes (0)' }).click()
  await expect.element(page.getByText('No notes yet.')).toBeVisible()
  expect(contentApi.attachments).not.toHaveBeenCalled()
})

test('failed feature discovery offers an explicit retry without content requests', async () => {
  features.isEnabled.mockReturnValue(false)
  features.error = 'Discovery unavailable'
  const screen = render(NgbObjectContent, { props: { target } })

  await screen.getByRole('button', { name: 'Retry available features' }).click()

  expect(features.load).toHaveBeenLastCalledWith(true)
  expect(contentApi.summary).not.toHaveBeenCalled()
  expect(contentApi.attachments).not.toHaveBeenCalled()
  expect(contentApi.notes).not.toHaveBeenCalled()
})
