import { randomUUID } from 'node:crypto'
import { expect, type Page } from '@playwright/test'
import { test } from './browser'
import { acceptanceProbe } from './acceptance'

async function signIn(page: Page, username: string) {
  const origin = process.env.NGB_EXAMPLE_ORIGIN!
  const access = page.waitForRequest(request => request.url() === `${origin}/api/security/me/access`)
  await page.goto(origin)
  await page.locator('input[name="username"]').fill(username)
  await page.locator('input[name="password"]').fill(process.env.NGB_EXAMPLE_PASSWORD!)
  await page.locator('button[type="submit"], input[type="submit"]').first().click()
  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30000 })
  return (await (await access).allHeaders()).authorization
}

test('a secondary command preserves unsaved input and executes after an explicit save', async ({ page }, info) => {
  const probe = await acceptanceProbe(page, info)
  const headers = { Authorization: await signIn(page, 'administrator') }
  const origin = process.env.NGB_EXAMPLE_ORIGIN!
  const equipment = await page.request.post(origin + '/api/catalogs/example.equipment', {
    headers, data: { fields: { display: 'Deferred inspection bench' } },
  })
  expect(equipment.ok()).toBe(true)
  const type = 'example.inspection'
  const created = await page.request.post(origin + '/api/documents/' + type, {
    headers, data: { fields: { equipment_id: (await equipment.json()).id, result: 'Initial observation' } },
  })
  expect(created.ok()).toBe(true)
  const { id } = await created.json()
  const path = origin + '/documents/' + type + '/' + id
  const api = origin + '/api/documents/' + type + '/' + id
  async function readDocumentDay() {
    const response = await page.request.get(api + '/graph', { headers })
    expect(response.ok()).toBe(true)
    const graph = await response.json()
    const node = graph.nodes.find((item: { entityId: string }) => item.entityId === id)
    expect(node.subtitle).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    return node.subtitle as string
  }
  const originalDay = await readDocumentDay()
  const state = page.waitForResponse(r => r.url() === api + '/editor-state')
  await page.goto(path)
  await probe.outcome('commands:' + type + '/["example.defer-one-day"]', async checks =>
    checks.draftCommands(await state, type, id, ['example.defer-one-day']))
  const field = page.locator('[data-validation-key="result"] input, [data-validation-key="result"] textarea').first()
  await field.fill('Keep this observation')
  const execute = async () => {
    await page.getByRole('button', { name: 'More actions', exact: true }).click()
    await page.getByRole('menuitem', { name: 'Defer one day', exact: true }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('textbox', { name: 'Reason' }).fill('Bench unavailable today')
    await dialog.getByRole('button', { name: 'Defer', exact: true }).click()
  }
  const executions: string[] = []
  page.on('request', request => {
    if (request.url() === api + '/actions/example.defer-one-day') executions.push(request.method())
  })
  await execute()
  await expect(page.getByText('Save your changes before running this action.', { exact: true })).toBeVisible()
  await expect(field).toHaveValue('Keep this observation')
  expect(executions).toEqual([])
  expect(await readDocumentDay()).toBe(originalDay)
  const saved = page.waitForResponse(r => r.url() === api && r.request().method() === 'PUT')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await saved).ok()).toBe(true)
  const result = page.waitForResponse(r => r.url() === api + '/actions/example.defer-one-day')
  await execute()
  expect((await result).ok()).toBe(true)
  expect(executions).toEqual(['POST'])
  await expect(page.getByText('Action completed.', { exact: true })).toBeVisible()
  await page.reload()
  await expect(field).toHaveValue('Keep this observation')
  const nextDay = new Date(originalDay + 'T00:00:00Z')
  nextDay.setUTCDate(nextDay.getUTCDate() + 1)
  expect(await readDocumentDay()).toBe(nextDay.toISOString().slice(0, 10))
  await probe.finish('example-draft-command-inventory', 'ngb-admin')
})

test('server eligibility excludes draft and consumed sources before real lookup selection', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  const authorization = await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN!
  const headers = { Authorization: authorization }
  const send = async (path: string, data: unknown) => {
    const response = await page.request.post(`${origin}${path}`, { headers: { ...headers, 'Idempotency-Key': randomUUID() }, data })
    expect(response.ok(), await response.text()).toBe(true)
    return response.json()
  }
  const equipment = await send('/api/catalogs/example.equipment', { fields: { display: 'Browser source eligibility' } })
  const documents = '/api/documents/example.inspection'
  const payload = { fields: { equipment_id: equipment.id, result: 'Confirmed safety check' } }
  const create = () => send(documents, payload)
  const post = async (id: string) => {
    const state = await (await page.request.get(`${origin}${documents}/${id}/editor-state`, { headers })).json()
    return send(`${documents}/${id}/actions/post`, { expectedVersion: state.documentVersion })
  }
  const draft = await create()
  const available = await create()
  await post(available.id)
  const consumed = await create()
  const confirmed = await post(consumed.id)
  const derived = await send(`${documents}/${consumed.id}/actions/example.reinspect`, { expectedVersion: confirmed.documentVersion })
  const saved = await page.request.put(`${origin}${documents}/${derived.createdDocument.id}`, { headers, data: payload })
  expect(saved.ok()).toBe(true)
  await post(derived.createdDocument.id)

  await page.goto(`${origin}/inspection-sources/${equipment.id}`)
  const lookup = page.getByRole('combobox')
  const searchResults = (query: string) => page.waitForResponse(response => {
    const url = new URL(response.url())
    return url.pathname === '/api/inspection-sources' && url.searchParams.get('query') === query
  })
  const searched = searchResults('I-')
  await lookup.fill('I-')
  const response = await searched
  expect(response.ok()).toBe(true)
  const eligibleIds = (await response.json()).map((item: { id: string }) => item.id)
  expect(eligibleIds.sort()).toEqual([available.id, derived.createdDocument.id].sort())
  await expect(page.getByRole('option', { name: available.number, exact: true })).toBeVisible()
  await expect(page.getByRole('option', { name: draft.number, exact: true })).toHaveCount(0)
  await expect(page.getByRole('option', { name: consumed.number, exact: true })).toHaveCount(0)

  const narrowed = searchResults(available.number)
  await lookup.fill(available.number)
  const narrowedResponse = await narrowed
  expect(narrowedResponse.ok()).toBe(true)
  expect((await narrowedResponse.json()).map((item: { id: string }) => item.id)).toEqual([available.id])
  const option = page.getByRole('option', { name: available.number, exact: true })
  await expect(option).toBeVisible()
  await expect(lookup).toHaveAttribute('aria-expanded', 'true')
  await lookup.press('ArrowDown')
  await expect(lookup).toHaveAttribute('aria-activedescendant', (await option.getAttribute('id'))!)
  const previewUrl = `${origin}/api/inspection-sources/${available.id}?equipmentId=${equipment.id}`
  await page.route(previewUrl, route => route.fulfill({ status: 503, contentType: 'application/json', body: '{}' }), { times: 1 })
  const failure = page.waitForResponse(previewUrl)
  await lookup.press('Enter')
  await proof.outcome('current-retry', async checks => checks.currentRetry(await failure, page.getByRole('alert'),
    page.getByRole('button', { name: 'Retry preview' }), lookup, available.number, page.getByRole('status'),
    'Confirmed safety check', 'Unable to read this inspection. Retry the preview.'))
  await page.getByRole('button', { name: 'Clear', exact: true }).click()
  await proof.text(page.getByRole('status'), 'Select an available inspection to preview its result.')
  await proof.finish('inspection-source-eligibility', 'ngb-admin')
})

test('reader cannot load source choices and the browser displays a usable failure', async ({ page }, info) => {
  const proof = await acceptanceProbe(page, info)
  await signIn(page, 'imitator')
  await page.goto(`${process.env.NGB_EXAMPLE_ORIGIN}/inspection-sources/00000000-0000-0000-0000-000000000001`)
  await page.getByRole('combobox').fill('Inspection')
  await proof.text(page.getByRole('alert'), 'Unable to load available inspections. Retry the search.')
  await expect(page.getByRole('option')).toHaveCount(0)
  await proof.finish('inspection-source-denied', 'example.content-reader')
})
