import assert from 'node:assert/strict'
import { createHash, randomUUID } from 'node:crypto'

export async function apiRequest(baseUrl, token, path, method = 'GET', body, expectedStatus = 200) {
  const response = await fetch(`${baseUrl}/api/${path}`, {
    method,
    headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(30_000),
  })
  const content = await response.text()
  assert.equal(response.status, expectedStatus, `${method} ${path}: ${content}`)
  return content ? JSON.parse(content) : null
}

export function authenticatedApi(baseUrl, token) {
  return (path, method, body, status) => apiRequest(baseUrl, token, path, method, body, status)
}

export async function createCoreState(api, administratorCode, email, extension) {
  const access = await api('security/me/access')
  assert.equal(access.isBootstrapAdmin, true)
  const roles = await api('security/roles')
  const administrator = roles.find(role => role.code === administratorCode)
  assert.ok(administrator, 'The registered Administrator role must exist after migration/bootstrap.')
  const users = await api('security/users')
  const user = users.items.find(item => item.email === email) ?? await api('security/users', 'POST', {
    email, firstName: 'External', lastName: 'Administrator', displayName: 'External Administrator',
    enabled: true, temporaryPassword: null, requirePasswordUpdate: false, roleIds: [administrator.roleId],
  })
  const permissions = [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'view' }]
  const created = await api('security/roles', 'POST', {
    code: `certification.reader.${randomUUID()}`, name: 'Certification Reader',
    description: 'Preserve this role across upgrades', permissions,
  })
  const role = await api(`security/roles/${created.roleId}`, 'PUT', {
    code: created.code, name: 'Certification Reader Updated', description: created.description,
    isActive: true, permissions,
  })
  assert.deepEqual(await api(`security/roles/${role.roleId}`), role)
  const audit = await api(`audit/entities/SecurityRole/${role.roleId}`)
  assert.ok(audit.items.length >= 2, 'Create/update must produce real audit events.')
  await api('work-center/summary')
  const state = { user: await api(`security/users/${user.userId}`), role, audit }
  if (extension) {
    const createdCatalog = await api('catalogs/certification.checkpoint', 'POST', { fields: { display: 'Before upgrade' } })
    state.checkpoint = await api(`catalogs/certification.checkpoint/${createdCatalog.id}`, 'PUT', {
      fields: { display: 'Baseline preserved value' },
    })
    assert.deepEqual(await api(`catalogs/certification.checkpoint/${createdCatalog.id}`), state.checkpoint)
    await api('catalogs/certification.checkpoint', 'POST', { fields: { display: '' } }, 400)
  }
  return state
}

export async function verifyCorePreservation(api, state) {
  assert.deepEqual(await api(`security/users/${state.user.userId}`), state.user)
  assert.deepEqual(await api(`security/roles/${state.role.roleId}`), state.role)
  assert.deepEqual(await api(`audit/entities/SecurityRole/${state.role.roleId}`), state.audit)
  if (state.checkpoint) {
    assert.deepEqual(await api(`catalogs/certification.checkpoint/${state.checkpoint.id}`), state.checkpoint)
  }
}

export async function verifyCoreContinuation(api, state) {
  const role = await api(`security/roles/${state.role.roleId}`, 'PUT', {
    code: state.role.code, name: 'Reader after upgrade', description: state.role.description,
    isActive: true, permissions: state.role.permissions,
  })
  assert.equal(role.name, 'Reader after upgrade')
  assert.deepEqual(await api(`security/roles/${role.roleId}`), role)
  const audit = await api(`audit/entities/SecurityRole/${role.roleId}`)
  const ids = new Set(audit.items.map(item => item.auditEventId))
  for (const item of state.audit.items) assert.ok(ids.has(item.auditEventId))
  assert.ok(audit.items.length > state.audit.items.length)
  if (state.checkpoint) {
    const catalog = await api(`catalogs/certification.checkpoint/${state.checkpoint.id}`, 'PUT', {
      fields: { display: 'After upgrade' },
    })
    assert.deepEqual(await api(`catalogs/certification.checkpoint/${catalog.id}`), catalog)
    await api('catalogs/certification.checkpoint', 'POST', { fields: { display: '' } }, 400)
    await api('catalogs/certification.checkpoint', 'POST', { fields: { display: 'New after upgrade' } })
  }
}

export async function createContentState(api, checkpoint, attachments) {
  const target = { kind: 'CatalogItem', typeCode: 'certification.checkpoint', id: checkpoint.id }
  const created = await api('notes', 'POST', { target, text: 'A note created before upgrade.' })
  const note = await api(`notes/${created.id}`, 'PUT', { text: 'The preserved note text.', version: created.version })
  const state = { target, note }
  if (attachments) {
    const bytes = Buffer.from(`Preserve original bytes across upgrade: ${randomUUID()}\n`, 'utf8')
    const upload = await api('attachments/uploads', 'POST', { target, fileName: 'checkpoint.txt', contentType: 'text/plain', sizeBytes: bytes.length })
    const response = await fetch(upload.url, { method: 'PUT', headers: upload.headers, body: bytes })
    assert.ok(response.ok, `Presigned upload failed: ${response.status}`)
    state.attachment = await api(`attachments/${upload.attachmentId}/complete`, 'POST')
    state.sha256 = createHash('sha256').update(bytes).digest('hex')
    await verifyAttachment(api, state.attachment.id, state.sha256)
  }
  state.audit = await api(`audit/entities/Catalog/${checkpoint.id}`)
  return state
}

export async function verifyAttachment(api, id, expectedHash) {
  const download = await api(`attachments/${id}/download`, 'POST')
  const response = await fetch(download.url)
  assert.ok(response.ok, `Presigned download failed: ${response.status}`)
  assert.equal(createHash('sha256').update(Buffer.from(await response.arrayBuffer())).digest('hex'), expectedHash)
}

export async function verifyContentPreservation(api, state) {
  const query = new URLSearchParams({ kind: state.target.kind, typeCode: state.target.typeCode, objectId: state.target.id })
  const notes = await api(`notes?${query}`)
  assert.deepEqual(notes.items.find(item => item.id === state.note.id), state.note)
  if (state.attachment) {
    const attachments = await api(`attachments?${query}`)
    assert.deepEqual(attachments.items.find(item => item.id === state.attachment.id), state.attachment)
    await verifyAttachment(api, state.attachment.id, state.sha256)
  } else {
    await api(`attachments?${query}`, 'GET', undefined, 404)
  }
  const audit = await api(`audit/entities/Catalog/${state.target.id}`)
  const ids = new Set(audit.items.map(item => item.auditEventId))
  for (const item of state.audit.items) assert.ok(ids.has(item.auditEventId))
}

export async function verifyContentContinuation(api, state) {
  const updated = await api(`notes/${state.note.id}`, 'PUT', { text: 'Edited after upgrade.', version: state.note.version })
  assert.ok(updated.version > state.note.version)
  await createContentState(api, { id: state.target.id }, Boolean(state.attachment))
}
