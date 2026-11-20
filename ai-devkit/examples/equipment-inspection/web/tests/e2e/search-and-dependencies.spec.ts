import { randomUUID } from 'node:crypto'
import { expect, test, type Page } from '@playwright/test'
import { acceptanceProbe } from './acceptance'

async function signIn(page: Page, username = 'administrator') {
  const origin = process.env.NGB_EXAMPLE_ORIGIN!
  const access = page.waitForRequest(request => request.url() === `${origin}/api/security/me/access`)
  await page.goto(origin)
  await page.locator('input[name="username"]').fill(username)
  await page.locator('input[name="password"]').fill(process.env.NGB_EXAMPLE_PASSWORD!)
  await page.locator('button[type="submit"], input[type="submit"]').first().click()
  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30000 })
  return { origin, headers: { Authorization: (await (await access).allHeaders()).authorization } }
}

async function seed(page: Page) {
  const session = await signIn(page)
  const send = async (path: string, data: unknown) => {
    const response = await page.request.post(`${session.origin}${path}`, {
      headers: { ...session.headers, 'Idempotency-Key': randomUUID() }, data,
    })
    expect(response.ok(), await response.text()).toBe(true)
    return response.json()
  }
  const name = `Precision bench ${randomUUID().slice(0, 8)}`
  const code = `EQ-${randomUUID().slice(0, 8)}`
  const equipment = await send('/api/catalogs/example.equipment', { fields: { display: name, equipment_code: code } })
  const inspect = async (result: string) => {
    const document = await send('/api/documents/example.inspection', {
      fields: { equipment_id: equipment.id, result },
    })
    const path = `/api/documents/example.inspection/${document.id}`
    const state = await (await page.request.get(`${session.origin}${path}/editor-state`, { headers: session.headers })).json()
    await send(`${path}/actions/post`, { expectedVersion: state.documentVersion })
    return document
  }
  const document = await inspect('Safety check passed')
  return { ...session, equipment, document, name, code, inspect, send }
}

async function search(page: Page, value: string) {
  await expect(page.getByRole('button', { name: /Search pages, records, reports/ })).toBeVisible()
  await page.keyboard.press('ControlOrMeta+k')
  const input = page.getByRole('dialog').getByRole('combobox')
  await input.fill(value)
  return page.getByRole('dialog')
}

test('Document Flow shows both derivations and opens the original document from the grandchild', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  const data = await seed(page)
  const path = '/api/documents/example.inspection'
  async function action(id: string, code: string) {
    const state = await page.request.get(`${data.origin}${path}/${id}/editor-state`, { headers: data.headers })
    expect(state.ok()).toBe(true)
    return data.send(`${path}/${id}/actions/${code}`, { expectedVersion: (await state.json()).documentVersion })
  }
  const original = data.document.id
  const child = (await action(original, 'example.reinspect')).createdDocument.id
  const saved = await page.request.put(`${data.origin}${path}/${child}`, {
    headers: data.headers, data: { fields: { equipment_id: data.equipment.id, result: 'Second check' } },
  })
  expect(saved.ok()).toBe(true)
  await action(child, 'post')
  const leaf = (await action(child, 'example.reinspect')).createdDocument.id
  const graph = page.waitForResponse(response => new URL(response.url()).pathname === `${path}/${leaf}/graph`)
  await proof.outcome('linked-chain', checks => checks.flow('example.inspection', leaf, [
    { from: leaf, to: child, code: 'created_from' }, { from: child, to: original, code: 'created_from' },
  ]))
  const nodes: { entityId: string, title: string }[] = (await (await graph).json()).nodes
  const source = nodes.find(node => node.entityId === original)!
  await page.getByRole('button', { name: source.title, exact: true }).click()
  await proof.outcome('linked-target', checks => checks.record(`/documents/example.inspection/${original}`, original))
  await proof.finish('inspection-related-chain', 'ngb-admin')
})

test('filtered report reconciles to its seeded records and excludes a second group', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  const data = await seed(page)
  const unrelated = await data.send('/api/catalogs/example.equipment', { fields: { display: 'Other group' } })
  const other = await data.send('/api/documents/example.inspection', { fields: { equipment_id: unrelated.id, result: 'Other check' } })
  const path = `/api/documents/example.inspection/${other.id}`
  const state = await page.request.get(`${data.origin}${path}/editor-state`, { headers: data.headers })
  await data.send(`${path}/actions/post`, { expectedVersion: (await state.json()).documentVersion })
  const code = 'example.inspection-summary'
  const day = new Date().toISOString().slice(0, 10)
  const parameters = { from_utc: day, to_utc: day }
  const filters = { equipment_id: { value: [data.equipment.id], includeDescendants: false } }
  const ctx = Buffer.from(JSON.stringify({ reportCode: code, request: { parameters, filters, limit: 50 } })).toString('base64url')
  const execution = page.waitForResponse(response => new URL(response.url()).pathname === `/api/reports/${code}/execute`)
  await page.goto(`${data.origin}/reports/${code}?ctx=${ctx}`)
  const response = await execution
  await proof.outcome('period', checks => checks.reportScope(response, code, parameters, filters))
  const sheet = page.getByTestId('report-sheet-table')
  await proof.outcome('figures', async checks => {
    await checks.figure(sheet.getByRole('row').last().getByRole('cell').last(), '1')
    await checks.count(sheet.getByText('Other check', { exact: true }), 0)
  })
  await sheet.getByRole('row').last().click()
  await proof.finish('inspection-filtered-figures', 'ngb-admin')
})

test('a source selected on a manual draft creates, replaces and clears its visible relationship', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  const data = await seed(page)
  const type = 'example.inspection'
  const createSource = async (label: string) => {
    const display = `${label} ${randomUUID().slice(0, 8)}`
    const source = await data.send(`/api/documents/${type}`, {
      fields: { display, equipment_id: data.equipment.id, result: label },
    })
    return { id: source.id as string, number: source.number as string, display }
  }
  const first = await createSource('Initial review')
  const second = await createSource('Replacement review')
  const selector = page.locator('[data-validation-key="source_inspection_id"]')
  const select = async (query: string, display: string) => {
    await selector.getByRole('combobox').fill(query)
    await page.getByRole('option', { name: display, exact: true }).click()
  }
  const save = async (method: string, path: string) => {
    const saved = page.waitForResponse(response => response.request().method() === method && new URL(response.url()).pathname === path)
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    const response = await saved
    expect(response.ok()).toBe(true)
    return response.json()
  }
  await page.goto(`${data.origin}/documents/${type}/new`)
  await select(first.number, first.display)
  const draft = await save('POST', `/api/documents/${type}`)
  const path = `/documents/${type}/${draft.id}`
  await proof.outcome('reference:source_inspection_id', checks => checks.reference(
    type, draft.id, 'source_inspection_id', [first.id], 'based_on',
  ))
  const openEditor = () => page.goto(`${data.origin}${path}`)
  await openEditor()
  await select(second.display, second.display)
  await save('PUT', `/api${path}`)
  await proof.outcome('linked-records', async checks => {
    await checks.flow(type, draft.id, [{ from: draft.id, to: second.id, code: 'based_on' }])
    await checks.count(page.getByRole('button', { name: first.display, exact: true }), 0)
  })
  await page.getByRole('button', { name: second.display, exact: true }).click()
  await proof.outcome('linked-target', checks => checks.record(`/documents/${type}/${second.id}`, second.id))
  await openEditor()
  await selector.getByRole('button', { name: 'Clear', exact: true }).click()
  await save('PUT', `/api${path}`)
  const loaded = page.waitForResponse(response => new URL(response.url()).pathname === `/api${path}/graph`)
  await page.goto(`${data.origin}${path}/flow`)
  const graph = await (await loaded).json()
  expect(graph.edges).toHaveLength(0)
  expect(graph.nodes.map((node: { entityId: string }) => node.entityId)).toEqual([draft.id])
  await proof.outcome('cleared-reference', checks => checks.count(page.getByRole('button', { name: second.display, exact: true }), 0))
  await proof.finish('inspection-manual-source', 'ngb-admin')
})

test('two posting requests for one version commit a single change visible on reopening', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  const data = await seed(page)
  const draft = await data.send('/api/documents/example.inspection', {
    fields: { equipment_id: data.equipment.id, result: 'Concurrent check' },
  })
  const path = `/documents/example.inspection/${draft.id}`
  const state = () => page.request.get(`${data.origin}/api${path}/editor-state`, { headers: data.headers })
  const version = (await (await state()).json()).documentVersion
  const post = () => page.request.post(`${data.origin}/api${path}/actions/post`, {
    headers: { ...data.headers, 'Idempotency-Key': randomUUID() }, data: { expectedVersion: version },
  })
  await proof.outcome('competing-requests', checks => checks.concurrent([post, post], [200, 409]))
  const palette = await search(page, draft.number)
  await palette.getByText(draft.number, { exact: true }).click()
  await proof.outcome('committed-effects', async checks => {
    await checks.record(path, draft.id)
    expect((await (await state()).json()).documentVersion).toBe(version + 1)
  })
  await page.reload()
  await proof.outcome('reopened-state', checks => checks.text(page.getByText('Posted', { exact: true }).first(), 'Posted'))
  await proof.finish('inspection-concurrent-post', 'ngb-admin')
})

test('replaying a posting preserves its execution and rejects a changed request', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  const data = await seed(page)
  const draft = await data.send('/api/documents/example.inspection', {
    fields: { equipment_id: data.equipment.id, result: 'Repeatable check' },
  })
  const path = `/api/documents/example.inspection/${draft.id}`
  const editor = () => page.request.get(`${data.origin}${path}/editor-state`, { headers: data.headers })
  const version = (await (await editor()).json()).documentVersion
  const post = (key: string, expectedVersion: number) => page.request.post(`${data.origin}${path}/actions/post`, {
    headers: { ...data.headers, 'Idempotency-Key': key }, data: { expectedVersion },
  })
  await proof.outcome('same-request', checks => checks.replay(
    key => post(key, version), (result: { executionId: string }) => result.executionId,
  ))
  await proof.outcome('conflicting-request', checks => checks.conflictingReplay(
    key => post(key, version + 1), 'document_action.idempotency_conflict',
  ))
  const dialog = await search(page, draft.number)
  await dialog.getByText(draft.number, { exact: true }).click()
  await proof.outcome('committed-effects', async checks => {
    await checks.record(`/documents/example.inspection/${draft.id}`, draft.id)
    await checks.text(page.getByText('Posted', { exact: true }).first(), 'Posted')
    expect((await (await editor()).json()).documentVersion).toBe(version + 1)
  })
  await proof.finish('inspection-post-replay', 'ngb-admin')
})

test('global search finds an existing number and catalog name and opens the exact records', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  const data = await seed(page)
  let palette = await search(page, data.document.number)
  await expect(palette.getByText(data.document.number, { exact: true })).toBeVisible()
  await page.keyboard.press('Enter')
  await proof.outcome('number-or-code', checks => checks.url(`${data.origin}/documents/example.inspection/${data.document.id}`))
  palette = await search(page, data.name)
  await expect(palette.getByText(data.name, { exact: true })).toBeVisible()
  await page.keyboard.press('Enter')
  await proof.outcome('opened-target', checks => checks.record(`/catalogs/example.equipment/${data.equipment.id}`, data.equipment.id))
  await proof.outcome('name', checks => checks.value(page.locator('[data-validation-key="display"] input'), data.name))
  await page.goto(`${data.origin}/catalogs/example.equipment`)
  palette = await search(page, data.code)
  await expect(palette.getByText(data.name, { exact: true })).toBeVisible()
  await page.keyboard.press('Enter')
  await proof.outcome('catalog-code', async checks => {
    await checks.url(`${data.origin}/catalogs/example.equipment/${data.equipment.id}`)
    await checks.value(page.locator('[data-validation-key="equipment_code"] input'), data.code)
  })
  await proof.finish('record-search', 'ngb-admin')
})

test('global search does not disclose documents to a catalog-only reader', async ({ page, browser }, info) => {
  const data = await seed(page)
  const context = await browser.newContext()
  try {
    const reader = await context.newPage()
    const proof = await acceptanceProbe(reader, info)
    await signIn(reader, 'imitator')
    const searched = reader.waitForResponse(value => value.url().includes('/api/equipment-search?'))
    const palette = await search(reader, data.document.number)
    const response = await searched
    expect(response.status()).toBe(200)
    expect((await response.json()).items).toEqual([])
    await expect(palette.getByText(data.document.number, { exact: true })).toHaveCount(0)
    await proof.outcome('denied-result', checks => checks.text(palette.getByText(`No results for “${data.document.number}”`, { exact: true }),
      `No results for “${data.document.number}”`))
    await palette.getByRole('combobox').fill(data.name)
    await proof.outcome('permitted-result', checks => checks.text(palette.getByText(data.name, { exact: true }), data.name))
    await proof.finish('record-search-denial', 'example.content-reader')
  } finally {
    await context.close()
  }
})

test('the report header and composer show the same short business description', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  await signIn(page)
  await page.getByRole('button', { name: 'Inspection summary', exact: true }).click()
  const description = 'Posted inspections by result within the inclusive UTC date range.'
  await proof.outcome('short-description', async checks => {
    await checks.text(page.getByTestId('report-page').getByText(description, { exact: true }), description)
    await page.getByRole('button', { name: 'Composer', exact: true }).click()
    await checks.text(page.getByTestId('report-composer-panel').getByText(description, { exact: true }), description)
  })
  await proof.finish('inspection-report-description', 'ngb-admin')
})

test('replacing a populated source clears its preview immediately and loads the new result', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  const data = await seed(page)
  const replacement = await data.inspect('Follow-up required')
  await page.goto(`${data.origin}/inspection-sources/${data.equipment.id}`)
  const options = page.getByRole('button', { name: 'Show options', exact: true })
  const result = page.getByRole('status')
  const equipment = page.getByTestId('source-equipment')
  await options.click()
  await page.getByRole('option', { name: data.document.number, exact: true }).click()
  await expect(result).toHaveText('Safety check passed')
  await expect(equipment).toHaveText(`Equipment: ${data.name}`)

  let release!: () => void
  const held = new Promise<void>(resolve => { release = resolve })
  await page.route(`**/api/inspection-sources/${replacement.id}?*`, async route => {
    const response = await route.fetch()
    await held
    await route.fulfill({ response })
  })
  try {
    await options.click()
    await page.getByRole('option', { name: replacement.number, exact: true }).click()
    await proof.outcome('pending-replacement', async checks => {
      await checks.text(result, 'Select an available inspection to preview its result.')
      await checks.text(equipment, 'Equipment: —')
    })
  } finally {
    release()
  }
  await proof.outcome('replacement-header', async checks => {
    await checks.value(page.getByRole('combobox'), replacement.number)
    await checks.text(result, 'Follow-up required')
    await checks.text(equipment, `Equipment: ${data.name}`)
  })
  await page.getByRole('button', { name: 'Clear', exact: true }).click()
  await proof.outcome('cleared-header', async checks => {
    await checks.value(page.getByRole('combobox'), '')
    await checks.text(result, 'Select an available inspection to preview its result.')
    await checks.text(equipment, 'Equipment: —')
  })
  await proof.finish('dependency-replacement', 'ngb-admin')
})

for (const stage of ['source', 'dependent'] as const) {
  test(`clearing during the ${stage} response keeps all dependent fields empty`, async ({ page }, info) => {
    const proof = await acceptanceProbe(page, info)
    const data = await seed(page)
    await page.goto(`${data.origin}/inspection-sources/${data.equipment.id}`)
    let release!: () => void
    let arrived!: () => void
    let delivered!: () => void
    const held = new Promise<void>(resolve => { release = resolve })
    const received = new Promise<void>(resolve => { arrived = resolve })
    const completed = new Promise<void>(resolve => { delivered = resolve })
    const pattern = stage === 'source'
      ? `**/api/inspection-sources/${data.document.id}?*`
      : `**/api/catalogs/example.equipment/${data.equipment.id}`
    await page.route(pattern, async route => {
      const response = await route.fetch()
      arrived()
      await held
      // Preserve the actual service response; do not fabricate the preview.
      try {
        await route.fulfill({ response })
      } catch {
        // The cleared request may already be aborted.
      } finally {
        delivered()
      }
    })
    await page.getByRole('button', { name: 'Show options', exact: true }).click()
    await page.getByRole('option', { name: data.document.number, exact: true }).click()
    await received
    await page.getByRole('button', { name: 'Clear', exact: true }).click()
    release()
    await completed
    await proof.outcome(`late-${stage}`, async checks => {
      await checks.value(page.getByRole('combobox'), '')
      await checks.text(page.getByTestId('source-equipment'), 'Equipment: —')
      await checks.text(page.getByRole('status'), 'Select an available inspection to preview its result.')
    })
    await page.unroute(pattern)
    // A subsequent selection must recover normally after cancellation.
    await page.getByRole('button', { name: 'Show options', exact: true }).click()
    await page.getByRole('option', { name: data.document.number, exact: true }).click()
    await proof.text(page.getByTestId('source-equipment'), `Equipment: ${data.name}`)
    await proof.text(page.getByRole('status'), 'Safety check passed')
    await page.getByRole('button', { name: 'Clear', exact: true }).click()
    await proof.text(page.getByTestId('source-equipment'), 'Equipment: —')
    await proof.finish(`dependency-${stage}`, 'ngb-admin')
  })
}
