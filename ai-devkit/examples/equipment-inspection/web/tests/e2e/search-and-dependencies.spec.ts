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
  return { ...session, equipment, document, name, code, inspect }
}

async function search(page: Page, value: string) {
  await expect(page.getByRole('button', { name: /Search pages, records, reports/ })).toBeVisible()
  await page.keyboard.press('ControlOrMeta+k')
  const input = page.getByRole('dialog').getByRole('combobox')
  await input.fill(value)
  return page.getByRole('dialog')
}

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
  await proof.outcome('opened-target', checks => checks.url(`${data.origin}/catalogs/example.equipment/${data.equipment.id}`))
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
