import { afterEach, expect, test, vi } from 'vitest'
import { contentApi, storageUrl, uploadBytes, startAttachmentDownload } from '../../../../src/ngb/business-objects/api'
import { httpGet, httpPost, httpPut, httpDelete } from '../../../../src/ngb/api/http'
vi.mock('../../../../src/ngb/api/http', () => ({ httpGet: vi.fn(), httpPost: vi.fn(), httpPut: vi.fn(), httpDelete: vi.fn() }))
afterEach(() => { vi.clearAllMocks(); vi.unstubAllGlobals() })
const target = { kind: 'Document', typeCode: 'invoice', id: 'object-id' } as const
const signal = new AbortController().signal

test('metadata requests use authenticated API transport and pass cancellation and cursors', async () => {
  await contentApi.summary(target, signal)
  await contentApi.attachments(target, 'last', signal)
  await contentApi.notes(target, null, signal)
  expect(httpGet).toHaveBeenNthCalledWith(1, '/api/business-objects/content-summary', { kind: 'Document', typeCode: 'invoice', objectId: 'object-id' }, { signal })
  expect(httpGet).toHaveBeenNthCalledWith(2, '/api/attachments', { kind: 'Document', typeCode: 'invoice', objectId: 'object-id', limit: 50, cursor: 'last' }, { signal })
  expect(httpGet).toHaveBeenCalledTimes(3)
})
test('upload creation sends metadata only and download requests never fetch file bodies', async () => {
  const file = new File(['abc'], 'file.txt')
  await contentApi.requestUpload(target, file, signal)
  expect(httpPost).toHaveBeenLastCalledWith('/api/attachments/uploads', { target, fileName: 'file.txt', contentType: 'application/octet-stream', sizeBytes: 3 }, { signal })
  await contentApi.requestUpload(target, new File(['a'], 'x.txt', { type: 'text/plain' }), signal)
  await contentApi.complete('id', signal)
  await contentApi.download('id', signal)
  expect(httpPost).toHaveBeenLastCalledWith('/api/attachments/id/download', undefined, { signal })
  await contentApi.deleteAttachment('id', signal)
  expect(httpDelete).toHaveBeenLastCalledWith('/api/attachments/id', undefined, { signal })
})
test('notes carry their own version and are sent as plain text', async () => {
  const note = { id: 'note-id', version: 2 } as Parameters<typeof contentApi.updateNote>[0]
  await contentApi.createNote(target, '<script>plain text</script>', signal)
  await contentApi.updateNote(note, 'edited', signal)
  expect(httpPut).toHaveBeenCalledWith('/api/notes/note-id', { text: 'edited', version: 2 }, { signal })
  await contentApi.deleteNote(note, signal)
  expect(httpDelete).toHaveBeenLastCalledWith('/api/notes/note-id?version=2', undefined, { signal })
})
test('direct PUT uses only storage headers, no credentials and reports failed or expired uploads', async () => {
  const fetch = vi.fn().mockResolvedValue({ ok: true })
  vi.stubGlobal('fetch', fetch)
  const target = { attachmentId: 'id', url: 'https://storage.example/upload', expiresAtUtc: 'soon', headers: { 'Content-Type': 'text/plain' } }
  const file = new File(['hello'], 'test.txt')
  await uploadBytes(target, file, signal)
  expect(fetch).toHaveBeenCalledWith(target.url, { method: 'PUT', body: file, headers: target.headers, signal, credentials: 'omit', referrerPolicy: 'no-referrer' })
  fetch.mockResolvedValue({ ok: false })
  await expect(uploadBytes(target, file, signal)).rejects.toThrow('upload link may have expired')
  fetch.mockRejectedValue(new DOMException('Aborted', 'AbortError'))
  await expect(uploadBytes(target, file, signal)).rejects.toThrow('Aborted')
})
test('storage navigation accepts only HTTP and HTTPS URLs', () => {
  expect(storageUrl('http://localhost:9100/file')).toBe('http://localhost:9100/file')
  expect(() => storageUrl('javascript:alert(1)')).toThrow('Invalid storage URL')
  expect(() => storageUrl('invalid')).toThrow()
})

test('download creates a normal isolated browser navigation and never fetches the body', () => {
  const link = { href: '', rel: '', target: '', click: vi.fn() }
  const createElement = vi.fn().mockReturnValue(link)
  vi.stubGlobal('document', { createElement })
  startAttachmentDownload('https://storage.example/file')
  expect(createElement).toHaveBeenCalledWith('a')
  expect(link).toMatchObject({ href: 'https://storage.example/file', rel: 'noopener noreferrer', target: '_blank' })
  expect(link.click).toHaveBeenCalledOnce()
})
