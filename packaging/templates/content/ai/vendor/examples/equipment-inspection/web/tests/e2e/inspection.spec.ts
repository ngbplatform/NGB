import { expect, test as base, type Page } from '@playwright/test'
import type { ReportExecutionRequestDto } from '@ngbplatform/ui/contracts'

const test = base.extend<{ diagnostics: void }>({
  diagnostics: [async ({ page }, use, info) => {
    const errors: string[] = []
    const responses: { path: string, status: number }[] = []
    const numericInputEvents: string[] = []
    page.on('pageerror', error => errors.push(error.message))
    page.on('console', message => {
      if (message.type() === 'error' || message.type() === 'warning') errors.push(message.text())
      if (message.text().startsWith('ngb-test-number:') && numericInputEvents.length < 200) {
        numericInputEvents.push(message.text().slice('ngb-test-number:'.length))
      }
    })
    await page.addInitScript(() => {
      for (const type of ['focus', 'beforeinput', 'input', 'change', 'blur']) {
        document.addEventListener(type, event => {
          const input = event.target
          if (!(input instanceof HTMLInputElement)) return
          const field = input.closest('[data-validation-key]')?.getAttribute('data-validation-key')
          if (field !== 'hours' && field !== 'amount') return
          console.debug('ngb-test-number:' + JSON.stringify({
            type, field, value: input.value, attribute: input.getAttribute('value'),
            inputType: event instanceof InputEvent ? event.inputType : null,
            data: event instanceof InputEvent ? event.data : null,
          }))
        }, true)
      }
    })
    page.on('response', response => responses.push({ path: new URL(response.url()).pathname, status: response.status() }))
    await use()
    await info.attach('browser-errors', { body: JSON.stringify(errors), contentType: 'application/json' })
    await info.attach('browser-responses', { body: JSON.stringify(responses), contentType: 'application/json' })
    await info.attach('numeric-input-events', { body: JSON.stringify(numericInputEvents), contentType: 'application/json' })
  }, { auto: true }],
})

async function signIn(page: Page, username: string): Promise<void> {
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  const password = process.env.NGB_EXAMPLE_PASSWORD
  expect(origin).toBeTruthy()
  expect(password).toBeTruthy()
  await page.goto(origin!)
  await page.locator('input[name="username"]').fill(username)
  await page.locator('input[name="password"]').fill(password!)
  await page.locator('button[type="submit"], input[type="submit"]').first().click()
  await expect(page.getByTestId('site-shell')).toBeVisible({ timeout: 30000 })
}

function field(page: Page, key: string) {
  return page.locator(`[data-validation-key="${key}"]`).locator('input, textarea').first()
}

async function enterNumber(page: Page, key: string, value: string): Promise<void> {
  const input = field(page, key)
  await input.click()
  await expect(input).toBeFocused()
  await input.fill(value)
  await input.press('Tab')
  await expect(input).toHaveValue(value)
}

test('published UI entry points preserve navigation, theme, assets and recovery from an unknown route', async ({ page }) => {
  await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  const shell = page.getByTestId('site-shell')
  await expect(shell).toHaveCSS('display', 'flex')
  const light = await shell.evaluate(element => getComputedStyle(element).backgroundColor)
  expect(light).not.toBe('rgba(0, 0, 0, 0)')
  await page.getByRole('button', { name: 'Switch to dark mode', exact: true }).click()
  await expect(page.locator('html')).toHaveClass(/dark/)
  await expect.poll(() => shell.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(light)
  await page.getByRole('button', { name: 'Switch to light mode', exact: true }).click()
  await expect(shell).toHaveCSS('background-color', light)

  await page.getByRole('button', { name: 'Inspections', exact: true }).click()
  await expect(page).toHaveURL(`${origin}/documents/example.inspection`)
  await page.getByRole('button', { name: 'Work Center', exact: true }).click()
  await page.getByRole('button', { name: 'View all', exact: true }).click()
  await expect.poll(() => new URL(page.url()).pathname).toBe('/work-center')
  await expect(page.getByTestId('work-center-tabs')).toBeVisible()
  await page.goto(`${origin}/missing-page`)
  await expect(page.getByRole('heading', { name: 'Page not found', exact: true })).toBeVisible()
  await page.getByTestId('site-main').getByRole('button', { name: 'Back', exact: true }).click()
  await expect(page).toHaveURL(`${origin}/catalogs/example.equipment`)

  const favicon = await page.request.get(`${origin}/favicon.svg`)
  expect(favicon.status()).toBe(200)
  expect(favicon.headers()['content-type']).toContain('image/svg+xml')
  expect(await favicon.text()).toContain('<svg')
  const silentLogin = await page.request.get(`${origin}/silent-check-sso.html`)
  expect(silentLogin.status()).toBe(200)
  expect(await silentLogin.text()).toContain('parent.postMessage')
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByTestId('site-sidebar')).toBeHidden()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
})

test('shared attachments upload and download real bytes while a reader cannot replace or delete them', async ({ page, browser }) => {
  await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  await page.goto(`${origin}/catalogs/example.equipment/new`)
  await field(page, 'display').fill('Equipment with inspection attachment')
  const created = page.waitForResponse(response => response.url().endsWith('/api/catalogs/example.equipment')
    && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const equipment = await (await created).json()
  const path = `${origin}/catalogs/example.equipment/${equipment.id}`
  await expect(page).toHaveURL(path)
  await page.getByRole('button', { name: 'More actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Attachments (0)', exact: true }).click()
  const drawer = page.getByRole('dialog', { name: 'Attachments', exact: true })
  await expect(drawer.getByText('No attachments yet.')).toBeVisible()
  const text = 'Equipment inspection: safety guard passed.\n'
  const completed = page.waitForResponse(response => /\/api\/attachments\/[^/]+\/complete$/.test(new URL(response.url()).pathname)
    && response.request().method() === 'POST')
  await drawer.getByLabel('Choose attachment').setInputFiles({
    name: 'inspection.txt', mimeType: 'text/plain', buffer: Buffer.from(text),
  })
  const completion = await completed
  expect(completion.status()).toBe(200)
  const attachment = await completion.json()
  await expect(drawer.getByText('inspection.txt', { exact: true })).toBeVisible()
  const downloaded = page.waitForEvent('download')
  await drawer.getByRole('button', { name: 'Download', exact: true }).click()
  const download = await downloaded
  expect(download.suggestedFilename()).toBe('inspection.txt')
  const stream = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of stream) chunks.push(Buffer.from(chunk))
  expect(Buffer.concat(chunks).toString('utf8')).toBe(text)
  await page.reload()
  await page.getByRole('button', { name: 'More actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Attachments (1)', exact: true }).click()
  await expect(drawer.getByText('inspection.txt', { exact: true })).toBeVisible()

  const readerContext = await browser.newContext()
  try {
    const reader = await readerContext.newPage()
    await signIn(reader, 'imitator')
    await reader.goto(path)
    await reader.getByRole('button', { name: 'More actions', exact: true }).click()
    await reader.getByRole('menuitem', { name: 'Attachments (1)', exact: true }).click()
    const readOnly = reader.getByRole('dialog', { name: 'Attachments', exact: true })
    await expect(readOnly.getByText('inspection.txt', { exact: true })).toBeVisible()
    await expect(readOnly.getByRole('button', { name: 'Download', exact: true })).toBeEnabled()
    await expect(readOnly.getByRole('button', { name: 'Upload file', exact: true })).toBeDisabled()
    await expect(readOnly.getByRole('button', { name: 'Mark for deletion', exact: true })).toBeDisabled()
  } finally {
    await readerContext.close()
  }

  const deleted = page.waitForResponse(response => new URL(response.url()).pathname === `/api/attachments/${attachment.id}`
    && response.request().method() === 'DELETE')
  await drawer.getByRole('button', { name: 'Mark for deletion', exact: true }).click()
  expect((await deleted).status()).toBe(204)
  await expect(drawer.getByText('No attachments yet.')).toBeVisible()
})

test('shared notes preserve plain text and allow readers to view without editing', async ({ page, browser }) => {
  await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  await page.goto(`${origin}/catalogs/example.equipment/new`)
  await field(page, 'display').fill('Equipment with inspection notes')
  const created = page.waitForResponse(response => response.url().endsWith('/api/catalogs/example.equipment')
    && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const equipment = await (await created).json()
  const path = `${origin}/catalogs/example.equipment/${equipment.id}`
  await expect(page).toHaveURL(path)
  await page.getByRole('button', { name: 'More actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Notes (0)', exact: true }).click()
  const drawer = page.getByRole('dialog', { name: 'Notes', exact: true })
  await expect(drawer.getByText('No notes yet.')).toBeVisible()
  const text = '<b>Inspect safety guard</b>'
  await drawer.getByLabel('Add note', { exact: true }).fill(text)
  const added = page.waitForResponse(response => new URL(response.url()).pathname === '/api/notes'
    && response.request().method() === 'POST')
  await drawer.getByRole('button', { name: 'Add note', exact: true }).click()
  const addedResponse = await added
  expect(addedResponse.status()).toBe(200)
  const note = await addedResponse.json()
  await expect(drawer.getByText(text, { exact: true })).toBeVisible()
  await expect(drawer.locator('b')).toHaveCount(0)
  await drawer.getByRole('button', { name: 'Edit', exact: true }).click()
  await drawer.getByLabel('Edit note', { exact: true }).fill('Safety guard checked')
  const edited = page.waitForResponse(response => new URL(response.url()).pathname === `/api/notes/${note.id}`
    && response.request().method() === 'PUT')
  await drawer.getByRole('button', { name: 'Save note', exact: true }).click()
  expect((await edited).status()).toBe(200)
  await expect(drawer.getByText('Safety guard checked', { exact: true })).toBeVisible()
  await page.reload()
  await page.getByRole('button', { name: 'More actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Notes (1)', exact: true }).click()
  await expect(drawer.getByText('Safety guard checked', { exact: true })).toBeVisible()

  const readerContext = await browser.newContext()
  try {
    const reader = await readerContext.newPage()
    await signIn(reader, 'imitator')
    await reader.goto(path)
    await reader.getByRole('button', { name: 'More actions', exact: true }).click()
    await reader.getByRole('menuitem', { name: 'Notes (1)', exact: true }).click()
    const readOnly = reader.getByRole('dialog', { name: 'Notes', exact: true })
    await expect(readOnly.getByText('Safety guard checked', { exact: true })).toBeVisible()
    await expect(readOnly.getByLabel('Add note', { exact: true })).toBeDisabled()
    await expect(readOnly.getByRole('button', { name: 'Add note', exact: true })).toBeDisabled()
    await expect(readOnly.getByRole('button', { name: 'Edit', exact: true })).toBeDisabled()
    await expect(readOnly.getByRole('button', { name: 'Mark for deletion', exact: true })).toBeDisabled()
  } finally {
    await readerContext.close()
  }

  const deleted = page.waitForResponse(response => new URL(response.url()).pathname === `/api/notes/${note.id}`
    && response.request().method() === 'DELETE')
  await drawer.getByRole('button', { name: 'Mark for deletion', exact: true }).click()
  expect((await deleted).status()).toBe(204)
  await expect(drawer.getByText('No notes yet.')).toBeVisible()
  await page.reload()
  await page.getByRole('button', { name: 'More actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Notes (0)', exact: true }).click()
  await expect(drawer.getByText('No notes yet.')).toBeVisible()
})

test('administrator closes and explicitly reopens a month through the shared period page', async ({ page }) => {
  await signIn(page, 'administrator')
  const month = new Date().toISOString().slice(0, 7)
  const url = `${process.env.NGB_EXAMPLE_ORIGIN}/admin/accounting/period-closing?year=${month.slice(0, 4)}&month=${month}&fy=${month}`
  await page.goto(url)
  await expect(page.getByTestId('period-closing-page')).toBeVisible()
  await page.getByRole('button', { name: 'Close Month', exact: true }).click()
  const closed = page.waitForResponse(response => response.url().endsWith('/api/accounting/period-closing/month/close')
    && response.request().method() === 'POST')
  await page.getByRole('dialog', { name: 'Close month?' }).getByRole('button', { name: 'Close Month', exact: true }).click()
  const closeResponse = await closed
  expect(closeResponse.status()).toBe(200)
  expect(await closeResponse.json()).toMatchObject({ period: `${month}-01`, isClosed: true, canReopen: true })
  await page.reload()
  const reopen = page.getByRole('button', { name: 'Reopen Month', exact: true })
  await expect(reopen).toBeDisabled()
  await page.getByPlaceholder('Explain why the chain needs to be reopened').fill('Review service reconciliation')
  await expect(reopen).toBeEnabled()
  await reopen.click()
  const reopened = page.waitForResponse(response => response.url().endsWith('/api/accounting/period-closing/month/reopen')
    && response.request().method() === 'POST')
  await page.getByRole('dialog', { name: 'Reopen month?' }).getByRole('button', { name: 'Reopen Month', exact: true }).click()
  const reopenResponse = await reopened
  expect(reopenResponse.status()).toBe(200)
  expect(await reopenResponse.json()).toMatchObject({ period: `${month}-01`, isClosed: false })
  await expect(page.getByRole('button', { name: 'Close Month', exact: true })).toBeEnabled()
})

test('administrator creates equipment, saves an incomplete Draft, completes and posts an inspection without effects', async ({ page }) => {
  await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  const equipmentName = `Inspection bench ${test.info().repeatEachIndex}`
  await page.goto(`${origin}/catalogs/example.equipment/new`)
  await field(page, 'display').fill(equipmentName)
  const equipmentCreated = page.waitForResponse(response => response.url().endsWith('/api/catalogs/example.equipment')
    && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const equipment = await (await equipmentCreated).json()
  expect(equipment.id).toBeTruthy()

  await page.goto(`${origin}/documents/example.inspection/new`)
  await field(page, 'display').fill('Initial safety check')
  const draftCreated = page.waitForResponse(response => response.url().endsWith('/api/documents/example.inspection')
    && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const draft = await (await draftCreated).json()
  expect(draft.status).toBe('Draft')
  await expect(page).toHaveURL(`${origin}/documents/example.inspection/${draft.id}`)

  const rejected = page.waitForResponse(response => response.url().endsWith('/actions/post'))
  await page.getByRole('button', { name: 'Post', exact: true }).click()
  expect((await rejected).status()).toBe(400)
  await expect(page.locator('[data-validation-key="equipment_id"]'))
    .toContainText('Select equipment before posting the inspection.')
  await expect(page.getByText('Posted', { exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Post', exact: true })).toBeEnabled()

  const equipmentLookup = field(page, 'equipment_id')
  await equipmentLookup.fill(equipmentName)
  await page.getByRole('option', { name: equipmentName }).click()
  await expect(page.getByRole('option', { name: equipmentName })).toHaveCount(0)
  await expect(equipmentLookup).toHaveValue(equipmentName)
  await field(page, 'result').click()
  await expect(field(page, 'result')).toBeFocused()
  await field(page, 'result').fill('All checks passed')
  await field(page, 'result').blur()
  await expect(field(page, 'result')).toHaveValue('All checks passed')
  const saved = page.waitForResponse(response => response.url().endsWith(`/api/documents/example.inspection/${draft.id}`)
    && response.request().method() === 'PUT')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const savedResponse = await saved
  expect(savedResponse.status()).toBe(200)
  expect((await savedResponse.json()).payload.fields.result).toBe('All checks passed')
  await expect(field(page, 'result')).toHaveValue('All checks passed')
  const posted = page.waitForResponse(response => response.url().endsWith('/actions/post'))
  await page.getByRole('button', { name: 'Post', exact: true }).click()
  const postedResponse = await posted
  const result = await postedResponse.json()
  expect(postedResponse.status(), JSON.stringify(result)).toBe(200)
  expect(result.document.status).toBe('Posted')
  expect(result.document.payload.fields.equipment_id).toEqual({ id: equipment.id, display: equipmentName })
  expect(result.document.payload.fields.result).toBe('All checks passed')
  await expect(page.getByText('Posted', { exact: true }).first()).toBeVisible()
  await page.reload()
  await expect(page.getByText('Posted', { exact: true }).first()).toBeVisible()
  const effectsLoaded = page.waitForResponse(response => new URL(response.url()).pathname
    === `/api/documents/example.inspection/${draft.id}/effects`)
  await page.goto(`${origin}/documents/example.inspection/${draft.id}/effects`)
  const effectsResponse = await effectsLoaded
  expect(effectsResponse.status()).toBe(200)
  const effects = await effectsResponse.json()
  expect(effects.accountingEntries).toEqual([])
  expect(effects.operationalRegisterMovements).toEqual([])
  expect(effects.referenceRegisterWrites).toEqual([])
  await expect(page.getByRole('tab', { name: 'Accounting Entries (0)', exact: true })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Operational Registers (0)', exact: true })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Reference Registers (0)', exact: true })).toBeVisible()

  const now = Date.now()
  const day = 24 * 60 * 60 * 1000
  const reportRequest: ReportExecutionRequestDto = {
    parameters: {
      from_utc: new Date(now - day).toISOString().slice(0, 10),
      to_utc: new Date(now + day).toISOString().slice(0, 10),
    },
    filters: { equipment_id: { value: [equipment.id] } },
    limit: 50,
  }
  const context = Buffer.from(JSON.stringify({
    reportCode: 'example.inspection-summary', request: reportRequest,
  })).toString('base64url')
  const reportLoaded = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/reports/example.inspection-summary/execute')
  await page.goto(`${origin}/reports/example.inspection-summary?ctx=${context}`)
  const reportResponse = await reportLoaded
  expect(reportResponse.status()).toBe(200)
  const report = await reportResponse.json()
  expect(report.hasMore).toBe(false)
  // Raw HTTP enum values are names; the UI client normalizes them for its numeric DTOs.
  expect(report.sheet.rows.filter((row: { rowKind: string }) => row.rowKind === 'Group'), JSON.stringify(report)).toHaveLength(1)
  expect(report.sheet.rows.find((row: { rowKind: string }) => row.rowKind === 'Total')?.cells.at(-1)?.value).toBe(1)
  await expect(page.getByTestId('report-sheet-table')).toContainText('All checks passed')
  await expect(page.getByTestId('report-sheet-table').getByRole('row').filter({ hasText: 'All checks passed' }))
    .toContainText('1')
})

test('ordinary actor receives a real API denial and cannot edit through a direct document URL', async ({ page }) => {
  await signIn(page, 'reader')
  const rejected = page.waitForResponse(response => response.url().includes('/api/documents/example.inspection/metadata')
    && response.status() === 403)
  await page.goto(`${process.env.NGB_EXAMPLE_ORIGIN}/documents/example.inspection/new`)
  await rejected
  await expect(page.getByText('Permission denied.', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
  await expect(page.locator('[data-validation-key]')).toHaveCount(0)

  const reportDenied = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/report-definitions/example.inspection-summary')
  await page.goto(`${process.env.NGB_EXAMPLE_ORIGIN}/reports/example.inspection-summary`)
  expect((await reportDenied).status()).toBe(403)
  await expect(page.getByTestId('report-page')).toContainText('Permission denied.')
  await expect(page.getByTestId('report-sheet-table')).toHaveCount(0)

  const periodDenied = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/accounting/period-closing/calendar')
  await page.goto(`${process.env.NGB_EXAMPLE_ORIGIN}/admin/accounting/period-closing`)
  expect((await periodDenied).status()).toBe(403)
  await expect(page.getByTestId('period-closing-page')).toContainText('Permission denied.')
  await expect(page.getByRole('button', { name: 'Close Month', exact: true })).toBeDisabled()
})

test('equipment usage posts measured hours and displays the real operational register movement', async ({ page }) => {
  await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  const equipmentName = `Usage bench ${test.info().repeatEachIndex}`
  await page.goto(`${origin}/catalogs/example.equipment/new`)
  await field(page, 'display').fill(equipmentName)
  const equipmentSaved = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/catalogs/example.equipment' && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await equipmentSaved).status()).toBe(200)

  await page.goto(`${origin}/documents/example.equipment-usage/new`)
  await field(page, 'display').fill('Morning shift')
  await field(page, 'equipment_id').fill(equipmentName)
  await page.getByRole('option', { name: equipmentName, exact: true }).click()
  await expect(field(page, 'equipment_id')).toHaveValue(equipmentName)
  await enterNumber(page, 'hours', '6.25')
  const usageSaved = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/documents/example.equipment-usage' && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const usage = await (await usageSaved).json()
  expect(usage.payload.fields.hours).toBe(6.25)
  await expect(page).toHaveURL(`${origin}/documents/example.equipment-usage/${usage.id}`)
  const posted = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/actions/post'))
  await page.getByRole('button', { name: 'Post', exact: true }).click()
  const postResponse = await posted
  expect(postResponse.status(), await postResponse.text()).toBe(200)
  await expect(page.getByText('Posted', { exact: true }).first()).toBeVisible()

  const effectsLoaded = page.waitForResponse(response => new URL(response.url()).pathname
    === `/api/documents/example.equipment-usage/${usage.id}/effects`)
  await page.goto(`${origin}/documents/example.equipment-usage/${usage.id}/effects`)
  const effects = await (await effectsLoaded).json()
  expect(effects.operationalRegisterMovements).toHaveLength(1)
  expect(effects.operationalRegisterMovements[0].resources).toEqual([{ code: 'hours', value: 6.25 }])
  expect(effects.operationalRegisterMovements[0].dimensions[0].display).toBe(equipmentName)
  await page.getByRole('tab', { name: 'Operational Registers (1)', exact: true }).click()
  await expect(page.getByText('Equipment operating hours', { exact: true })).toBeVisible()
  await expect(page.getByText(equipmentName, { exact: true })).toBeVisible()
})

test('equipment assessment displays its current reference effect and clears it after Unpost', async ({ page }) => {
  await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  const equipmentName = `Assessment bench ${test.info().repeatEachIndex}`
  await page.goto(`${origin}/catalogs/example.equipment/new`)
  await field(page, 'display').fill(equipmentName)
  const equipmentSaved = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/catalogs/example.equipment' && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await equipmentSaved).status()).toBe(200)

  await page.goto(`${origin}/documents/example.equipment-condition/new`)
  await field(page, 'display').fill('Pre-service assessment')
  await field(page, 'equipment_id').fill(equipmentName)
  await page.getByRole('option', { name: equipmentName, exact: true }).click()
  await expect(page.getByRole('option', { name: equipmentName, exact: true })).toHaveCount(0)
  await expect(field(page, 'equipment_id')).toHaveValue(equipmentName)
  const condition = field(page, 'condition')
  await condition.click()
  await expect(condition).toBeFocused()
  await condition.fill('Ready for service')
  await condition.press('Tab')
  await expect(condition).toHaveValue('Ready for service')
  const assessmentSaved = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/documents/example.equipment-condition' && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const savedResponse = await assessmentSaved
  expect(savedResponse.status(), await savedResponse.text()).toBe(200)
  const assessment = await savedResponse.json()
  expect(assessment.payload.fields.condition).toBe('Ready for service')
  const documentPath = `/documents/example.equipment-condition/${assessment.id}`
  await expect(page).toHaveURL(`${origin}${documentPath}`)
  const posted = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/actions/post'))
  await page.getByRole('button', { name: 'Post', exact: true }).click()
  const postResponse = await posted
  expect(postResponse.status(), await postResponse.text()).toBe(200)
  await expect(page.getByText('Posted', { exact: true }).first()).toBeVisible()

  const effectsLoaded = page.waitForResponse(response => new URL(response.url()).pathname === `/api${documentPath}/effects`)
  await page.goto(`${origin}${documentPath}/effects`)
  const effects = await (await effectsLoaded).json()
  expect(effects.referenceRegisterWrites).toHaveLength(1)
  expect(effects.referenceRegisterWrites[0].fields.condition).toBe('Ready for service')
  expect(effects.referenceRegisterWrites[0].isTombstone).toBe(false)
  await page.getByRole('tab', { name: 'Reference Registers (1)', exact: true }).click()
  await expect(page.getByText('Equipment assessment records', { exact: true })).toBeVisible()
  await expect(page.getByText(equipmentName, { exact: true })).toBeVisible()
  await expect(page.getByText('Condition: Ready for service', { exact: true })).toBeVisible()

  await page.goto(`${origin}${documentPath}`)
  const unposted = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/actions/unpost'))
  await page.getByRole('button', { name: 'Unpost', exact: true }).click()
  await page.getByRole('dialog', { name: 'Unpost document?' }).getByRole('button', { name: 'Unpost', exact: true }).click()
  const unpostResponse = await unposted
  expect(unpostResponse.status(), await unpostResponse.text()).toBe(200)
  await expect(page.getByText('Draft', { exact: true }).first()).toBeVisible()
  const clearedEffects = page.waitForResponse(response => new URL(response.url()).pathname === `/api${documentPath}/effects`)
  await page.goto(`${origin}${documentPath}/effects`)
  const clearedResponse = await clearedEffects
  expect(clearedResponse.status()).toBe(200)
  expect((await clearedResponse.json()).referenceRegisterWrites).toEqual([])
  await page.getByRole('tab', { name: 'Reference Registers (0)', exact: true }).click()
  await expect(page.getByText('Equipment assessment records', { exact: true })).toHaveCount(0)
})

test('equipment service posts accounting, operational and reference effects together', async ({ page }) => {
  await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  const equipmentName = `Service bench ${test.info().repeatEachIndex}`
  await page.goto(`${origin}/catalogs/example.equipment/new`)
  await field(page, 'display').fill(equipmentName)
  const equipmentSaved = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/catalogs/example.equipment' && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await equipmentSaved).status()).toBe(200)

  await page.goto(`${origin}/documents/example.equipment-service/new`)
  await field(page, 'display').fill('Scheduled service')
  await field(page, 'equipment_id').fill(equipmentName)
  await page.getByRole('option', { name: equipmentName, exact: true }).click()
  await expect(page.getByRole('option', { name: equipmentName, exact: true })).toHaveCount(0)
  await field(page, 'hours').click()
  await enterNumber(page, 'hours', '2.5')
  await enterNumber(page, 'amount', '125.50')
  await field(page, 'condition').fill('Ready after service')
  await field(page, 'condition').blur()
  const serviceSaved = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/documents/example.equipment-service' && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const savedResponse = await serviceSaved
  expect(savedResponse.status()).toBe(200)
  const service = await savedResponse.json()
  expect(service.payload.fields.amount).toBe(125.5)
  expect(service.payload.fields.hours).toBe(2.5)
  const documentPath = `/documents/example.equipment-service/${service.id}`
  await expect(page).toHaveURL(`${origin}${documentPath}`)
  const posted = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/actions/post'))
  await page.getByRole('button', { name: 'Post', exact: true }).click()
  const postResponse = await posted
  expect(postResponse.status(), await postResponse.text()).toBe(200)
  await expect(page.getByText('Posted', { exact: true }).first()).toBeVisible()

  const effectsLoaded = page.waitForResponse(response => new URL(response.url()).pathname === `/api${documentPath}/effects`)
  await page.goto(`${origin}${documentPath}/effects`)
  const effectsResponse = await effectsLoaded
  expect(effectsResponse.status()).toBe(200)
  const effects = await effectsResponse.json()
  expect(effects.accountingEntries).toHaveLength(1)
  expect(effects.accountingEntries[0].amount).toBe(125.5)
  expect(effects.operationalRegisterMovements).toHaveLength(1)
  expect(effects.operationalRegisterMovements[0].resources).toEqual([{ code: 'hours', value: 2.5 }])
  expect(effects.referenceRegisterWrites).toHaveLength(1)
  expect(effects.referenceRegisterWrites[0].fields.condition).toBe('Ready after service')
  await expect(page.getByRole('tab', { name: 'Accounting Entries (1)', exact: true })).toBeVisible()
  await expect(page.getByText('example.service-expense — Equipment service expense', { exact: true })).toBeVisible()
  await expect(page.getByText('example.service-payable — Equipment service payable', { exact: true })).toBeVisible()
  await expect(page.getByRole('tabpanel', { name: 'Accounting Entries (1)' }).getByText('125.50', { exact: true })).toHaveCount(2)
  await page.getByRole('tab', { name: 'Operational Registers (1)', exact: true }).click()
  await expect(page.getByText('Equipment service hours', { exact: true })).toBeVisible()
  await page.getByRole('tab', { name: 'Reference Registers (1)', exact: true }).click()
  await expect(page.getByText('Condition: Ready after service', { exact: true })).toBeVisible()
})
test('withdrawal reaches another tab through SignalR and Work Center preferences preserve required reviews', async ({ page, context }) => {
  await signIn(page, 'administrator')
  const origin = process.env.NGB_EXAMPLE_ORIGIN
  const equipmentName = `Withdrawal review bench ${test.info().repeatEachIndex}`
  await page.goto(`${origin}/catalogs/example.equipment/new`)
  await field(page, 'display').fill(equipmentName)
  const equipmentSaved = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/catalogs/example.equipment' && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await equipmentSaved).status()).toBe(200)

  await page.goto(`${origin}/documents/example.inspection/new`)
  await field(page, 'display').fill('Withdrawal review inspection')
  await field(page, 'equipment_id').fill(equipmentName)
  await page.getByRole('option', { name: equipmentName, exact: true }).click()
  await expect(page.getByRole('option', { name: equipmentName, exact: true })).toHaveCount(0)
  await expect(field(page, 'equipment_id')).toHaveValue(equipmentName)
  const result = field(page, 'result')
  await result.click()
  await expect(result).toBeFocused()
  await result.fill('Passed before withdrawal')
  await result.press('Tab')
  await expect(result).toHaveValue('Passed before withdrawal')
  const saved = page.waitForResponse(response => new URL(response.url()).pathname
    === '/api/documents/example.inspection' && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const savedResponse = await saved
  expect(savedResponse.status(), await savedResponse.text()).toBe(200)
  const document = await savedResponse.json()
  expect(document.payload.fields.result).toBe('Passed before withdrawal')
  const documentPath = `/documents/example.inspection/${document.id}`
  await expect(page).toHaveURL(`${origin}${documentPath}`)
  const posted = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/actions/post'))
  await page.getByRole('button', { name: 'Post', exact: true }).click()
  const postResponse = await posted
  expect(postResponse.status(), await postResponse.text()).toBe(200)
  await expect(page.getByText('Posted', { exact: true }).first()).toBeVisible()

  const feed = await context.newPage()
  let connections = 0
  let changes = 0
  feed.on('websocket', socket => {
    if (new URL(socket.url()).pathname !== '/hubs/work-center') return
    socket.on('framereceived', frame => {
      for (const chunk of frame.payload.toString().split('\u001e').filter(Boolean)) {
        const message = JSON.parse(chunk)
        if (Object.keys(message).length === 0) connections += 1
        if (message.type === 1 && message.target === 'workCenterChanged') changes += 1
      }
    })
  })
  await feed.goto(`${origin}/work-center`)
  await expect(feed.getByRole('region', { name: 'Work Center feed' })).toBeVisible()
  await expect.poll(() => connections).toBeGreaterThan(0)
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(0)

  const unposted = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/actions/unpost'))
  await page.getByRole('button', { name: 'Unpost', exact: true }).click()
  await page.getByRole('dialog', { name: 'Unpost document?' }).getByRole('button', { name: 'Unpost', exact: true }).click()
  expect((await unposted).status()).toBe(200)
  await expect.poll(() => changes, { timeout: 15000 }).toBeGreaterThan(0)
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(2)
  await expect(feed.getByTestId('work-center-feed-item').filter({ hasText: 'Review withdrawn inspection' })).toHaveCount(1)
  await expect(feed.getByTestId('work-center-feed-item').filter({ hasText: 'Inspection withdrawn' })).toHaveCount(1)

  await feed.getByRole('tab', { name: /^Notifications/ }).click()
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(1)
  await feed.getByRole('button', { name: 'More actions for Inspection withdrawn', exact: true }).click()
  const dismissed = feed.waitForResponse(response => /\/api\/work-center\/notifications\/[^/]+\/dismiss$/.test(new URL(response.url()).pathname))
  await feed.getByRole('menuitem', { name: 'Dismiss', exact: true }).click()
  expect((await dismissed).status()).toBe(204)
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(0)
  await feed.getByRole('tab', { name: /^Tasks/ }).click()
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(1)
  await feed.getByRole('button', { name: 'More actions for Review withdrawn inspection', exact: true }).click()
  const snoozed = feed.waitForResponse(response => /\/api\/work-center\/tasks\/[^/]+\/snooze$/.test(new URL(response.url()).pathname))
  await feed.getByRole('menuitem', { name: 'Snooze 1 day', exact: true }).click()
  expect((await snoozed).status()).toBe(204)
  await expect(feed.getByTestId('work-center-feed-item')).toContainText('Snoozed until')
  await feed.getByRole('tab', { name: /^Needs Attention/ }).click()
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(0)

  await feed.goto(`${origin}/settings/notifications`)
  const required = feed.getByRole('checkbox', { name: /Withdrawn inspection reviews/ })
  await expect(required).toBeChecked()
  await expect(required).toBeDisabled()
  await feed.getByRole('checkbox', { name: /Inspection withdrawals/ }).uncheck()
  const preferencesSaved = feed.waitForResponse(response => new URL(response.url()).pathname
    === '/api/me/notification-preferences' && response.request().method() === 'PUT')
  await feed.getByRole('button', { name: 'Save preferences', exact: true }).click()
  expect((await preferencesSaved).status()).toBe(204)
  await feed.reload()
  await expect(feed.getByRole('checkbox', { name: /Inspection withdrawals/ })).not.toBeChecked()
  await expect(feed.getByRole('checkbox', { name: /Withdrawn inspection reviews/ })).toBeChecked()
  const previousConnections = connections
  await feed.goto(`${origin}/work-center`)
  await expect(feed.getByRole('region', { name: 'Work Center feed' })).toBeVisible()
  await expect.poll(() => connections).toBeGreaterThan(previousConnections)
  const before = changes
  const postedAgain = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/actions/post'))
  await page.getByRole('button', { name: 'Post', exact: true }).click()
  expect((await postedAgain).status()).toBe(200)
  await expect(page.getByText('Posted', { exact: true }).first()).toBeVisible()
  const withdrawnAgain = page.waitForResponse(response => new URL(response.url()).pathname.endsWith('/actions/unpost'))
  await page.getByRole('button', { name: 'Unpost', exact: true }).click()
  await page.getByRole('dialog', { name: 'Unpost document?' }).getByRole('button', { name: 'Unpost', exact: true }).click()
  expect((await withdrawnAgain).status()).toBe(200)
  await expect.poll(() => changes, { timeout: 15000 }).toBeGreaterThan(before)
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(1)
  await expect(feed.getByTestId('work-center-feed-item')).toContainText('Review withdrawn inspection')
  await feed.getByRole('tab', { name: /^Tasks/ }).click()
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(2)
  await feed.getByRole('tab', { name: /^Notifications/ }).click()
  await expect(feed.getByTestId('work-center-feed-item')).toHaveCount(0)
  await feed.close()
})

