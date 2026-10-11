import assert from 'node:assert/strict'
import test from 'node:test'
import {
  apiRequest, authenticatedApi, createCoreState, verifyCorePreservation, verifyCoreContinuation,
  createContentState, verifyAttachment, verifyContentPreservation, verifyContentContinuation,
} from './scenarios.mjs'

function responses(values) {
  const queue = [...values]
  const calls = []
  const api = async (...args) => {
    calls.push(args)
    assert.ok(queue.length > 0, `Unexpected request: ${args[0]}`)
    return queue.shift()
  }
  return { api, calls, complete: () => assert.equal(queue.length, 0) }
}

test('HTTP assertions reject status failures and preserve method, body and authorization', async t => {
  const calls = []
  const fetch = t.mock.method(globalThis, 'fetch', async (...args) => {
    calls.push(args)
    return new Response('{"ok":true}', { status: 200 })
  })
  assert.deepEqual(await authenticatedApi('https://api.test', 'test-token')('health'), { ok: true })
  assert.equal(calls[0][1].headers.authorization, 'Bearer test-token')
  assert.equal(calls[0][1].body, undefined)
  await apiRequest('https://api.test', 'test-token', 'roles', 'POST', { name: 'Reader' })
  assert.equal(calls[1][1].body, '{"name":"Reader"}')
  fetch.mock.mockImplementation(async () => new Response(null, { status: 204 }))
  assert.equal(await apiRequest('https://api.test', 'token', 'role', 'DELETE', undefined, 204), null)
  await assert.rejects(apiRequest('https://api.test', 'token', 'role'), /GET role/)
})

const user = { userId: 'user-1', email: 'admin@test.invalid' }
const role = { roleId: 'role-1', code: 'reader', name: 'Certification Reader Updated', permissions: [], description: 'Keep' }
const audit = { items: [{ auditEventId: 'a' }, { auditEventId: 'b' }] }
const checkpoint = { id: 'catalog-1', fields: { display: 'preserved' } }
const core = { user, role, audit, checkpoint }

test('source setup covers existing/new users, real audit and custom validation', async () => {
  const roles = [{ code: 'other' }, { code: 'admin', roleId: 'admin-role' }]
  const fixed = responses([
    { isBootstrapAdmin: true }, roles, { items: [{ email: 'other' }, user] }, role, role, role, audit, {}, user,
    checkpoint, checkpoint, checkpoint, {},
  ])
  assert.deepEqual(await createCoreState(fixed.api, 'admin', user.email, true), core)
  fixed.complete()
  assert.equal(fixed.calls.at(-1)[3], 400)
  const withoutExtension = responses([
    { isBootstrapAdmin: true }, roles, { items: [] }, user, role, role, role, audit, {}, user,
  ])
  assert.deepEqual(await createCoreState(withoutExtension.api, 'admin', user.email, false), { user, role, audit })
  withoutExtension.complete()
  assert.equal(withoutExtension.calls[3][1], 'POST')
  await assert.rejects(createCoreState(responses([{ isBootstrapAdmin: false }]).api, 'admin', user.email, false))
  await assert.rejects(createCoreState(responses([{ isBootstrapAdmin: true }, []]).api, 'missing', user.email, false))
})

test('upgrade assertions reject lost state and audit while allowing continued writes', async () => {
  await verifyCorePreservation(responses([user, role, audit, checkpoint]).api, core)
  await verifyCorePreservation(responses([user, role, audit]).api, { user, role, audit })
  await assert.rejects(verifyCorePreservation(responses([{ ...user, userId: 'changed' }]).api, core))
  const nextRole = { ...role, name: 'Reader after upgrade' }
  const nextAudit = { items: [...audit.items, { auditEventId: 'c' }] }
  const withExtension = responses([nextRole, nextRole, nextAudit, checkpoint, checkpoint, {}, {}])
  await verifyCoreContinuation(withExtension.api, core)
  withExtension.complete()
  await verifyCoreContinuation(responses([nextRole, nextRole, nextAudit]).api, { user, role, audit })
  await assert.rejects(verifyCoreContinuation(responses([nextRole, nextRole, { items: [{ auditEventId: 'c' }] }]).api, core))
})

test('content contracts preserve metadata, original bytes, audit and authorization statuses', async t => {
  let uploaded
  const fetch = t.mock.method(globalThis, 'fetch', async (_url, options) => {
    if (options?.method === 'PUT') uploaded = options.body
    return new Response(uploaded, { status: 200 })
  })
  const note = { id: 'note-1', version: 1, text: 'The preserved note text.' }
  const attachment = {
    id: 'attachment-1', fileName: 'checkpoint.txt', contentType: 'text/plain',
    sizeBytes: Buffer.byteLength('Preserve original bytes across upgrade: ') + 36 + 1,
    createdAtUtc: '2026-10-11T20:20:53.960136Z',
  }
  const completed = { ...attachment, createdAtUtc: '2026-10-11T20:20:53.9601367Z' }
  const upload = { url: 'https://storage.test/upload', headers: {}, attachmentId: attachment.id }
  const download = { url: 'https://storage.test/download' }
  const listedNotes = { items: [{ id: 'other' }, note] }
  const listedAttachments = { items: [{ id: 'other' }, attachment] }
  const setup = responses([note, note, listedNotes, upload, completed, listedAttachments, download, audit])
  const state = await createContentState(setup.api, checkpoint, true)
  setup.complete()
  assert.equal(state.attachment, attachment)
  assert.match(state.sha256, /^[a-f0-9]{64}$/)
  assert.match(setup.calls[2][0], /^notes\?kind=CatalogItem&typeCode=certification.checkpoint&objectId=catalog-1$/)
  assert.match(setup.calls[5][0], /^attachments\?kind=CatalogItem&typeCode=certification.checkpoint&objectId=catalog-1$/)
  const notesOnly = await createContentState(responses([note, note, listedNotes, audit]).api, checkpoint, false)
  await verifyContentPreservation(responses([
    { items: [{ id: 'other' }, note] }, { items: [{ id: 'other' }, attachment] }, download, audit,
  ]).api, state)
  const disabled = responses([{ items: [note] }, null, audit])
  await verifyContentPreservation(disabled.api, notesOnly)
  assert.equal(disabled.calls[1][3], 404)
  await assert.rejects(verifyContentPreservation(responses([{ items: [{ ...note, text: 'lost' }] }]).api, state))
  await assert.rejects(verifyAttachment(responses([download]).api, attachment.id, 'wrong-hash'))
  await verifyContentContinuation(responses([
    { ...note, version: 2 }, note, note, listedNotes, upload, completed, listedAttachments, download, audit,
  ]).api, state)
  await verifyContentContinuation(responses([{ ...note, version: 2 }, note, note, listedNotes, audit]).api, notesOnly)
  await assert.rejects(verifyContentContinuation(responses([note]).api, state))
  await assert.rejects(createContentState(responses([note, note, listedNotes, upload, completed, { items: [] }]).api, checkpoint, true), /source attachment/)
  for (const change of [{ fileName: 'lost.txt' }, { contentType: 'other' }, { sizeBytes: 0 }]) {
    await assert.rejects(createContentState(responses([
      note, note, listedNotes, upload, completed, { items: [{ ...attachment, ...change }] },
    ]).api, checkpoint, true))
  }
  await assert.rejects(verifyContentPreservation(responses([
    listedNotes, { items: [{ ...attachment, createdAtUtc: '2026-10-11T20:20:53.960137Z' }] },
  ]).api, state))
  fetch.mock.mockImplementation(async () => new Response(null, { status: 403 }))
  await assert.rejects(verifyAttachment(responses([download]).api, attachment.id, state.sha256), /download failed/)
  await assert.rejects(createContentState(responses([note, note, listedNotes, upload]).api, checkpoint, true), /upload failed/)
})

test('note preservation uses the source database read and still rejects any timestamp or metadata change', async () => {
  const created = { id: 'note-precision', version: 1 }
  const updated = {
    id: created.id, version: 2, text: 'The preserved note text.',
    createdAtUtc: '2026-10-11T20:20:53.960136Z', createdByUserId: 'author',
    createdByDisplayName: 'External Administrator', updatedByUserId: 'author',
    updatedAtUtc: '2026-10-11T20:20:54.0181437Z',
  }
  const persisted = { ...updated, updatedAtUtc: '2026-10-11T20:20:54.018143Z' }
  const setup = responses([created, updated, { items: [persisted] }, audit])
  const state = await createContentState(setup.api, checkpoint, false)
  setup.complete()
  assert.deepEqual(state.note, persisted)
  assert.notDeepEqual(state.note, updated)
  await verifyContentPreservation(responses([{ items: [persisted] }, null, audit]).api, state)
  for (const change of [
    { updatedAtUtc: '2026-10-11T20:20:54.018144Z' },
    { updatedAtUtc: '2026-10-11T20:20:54.0181431Z' },
    { createdAtUtc: '2026-10-11T20:20:53.960137Z' },
    { text: 'Changed' }, { version: 3 }, { createdByUserId: 'different-author' },
    { updatedByUserId: 'different-editor' }, { createdByDisplayName: 'Different name' },
  ]) {
    await assert.rejects(verifyContentPreservation(responses([{ items: [{ ...persisted, ...change }] }]).api, state))
  }
  await assert.rejects(verifyContentPreservation(responses([{ items: [] }]).api, state))
})

test('source note setup rejects missing records, lost text and a persisted version mismatch', async () => {
  const note = { id: 'note-1', version: 2, text: 'The preserved note text.' }
  for (const items of [[], [{ ...note, text: 'Lost update' }], [{ ...note, version: 1 }]]) {
    await assert.rejects(createContentState(responses([note, note, { items }]).api, checkpoint, false))
  }
})
