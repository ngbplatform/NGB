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
  const attachment = { id: 'attachment-1', fileName: 'checkpoint.txt' }
  const upload = { url: 'https://storage.test/upload', headers: {}, attachmentId: attachment.id }
  const download = { url: 'https://storage.test/download' }
  const setup = responses([note, note, upload, attachment, download, audit])
  const state = await createContentState(setup.api, checkpoint, true)
  setup.complete()
  assert.equal(state.attachment, attachment)
  assert.match(state.sha256, /^[a-f0-9]{64}$/)
  const notesOnly = await createContentState(responses([note, note, audit]).api, checkpoint, false)
  await verifyContentPreservation(responses([
    { items: [{ id: 'other' }, note] }, { items: [{ id: 'other' }, attachment] }, download, audit,
  ]).api, state)
  const disabled = responses([{ items: [note] }, null, audit])
  await verifyContentPreservation(disabled.api, notesOnly)
  assert.equal(disabled.calls[1][3], 404)
  await assert.rejects(verifyContentPreservation(responses([{ items: [{ ...note, text: 'lost' }] }]).api, state))
  await assert.rejects(verifyAttachment(responses([download]).api, attachment.id, 'wrong-hash'))
  await verifyContentContinuation(responses([{ ...note, version: 2 }, note, note, upload, attachment, download, audit]).api, state)
  await verifyContentContinuation(responses([{ ...note, version: 2 }, note, note, audit]).api, notesOnly)
  await assert.rejects(verifyContentContinuation(responses([note]).api, state))
  fetch.mock.mockImplementation(async () => new Response(null, { status: 403 }))
  await assert.rejects(verifyAttachment(responses([download]).api, attachment.id, state.sha256), /download failed/)
  await assert.rejects(createContentState(responses([note, note, upload]).api, checkpoint, true), /upload failed/)
})
