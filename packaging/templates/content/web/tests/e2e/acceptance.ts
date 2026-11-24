import { randomUUID } from 'node:crypto'
import type { MainMenuDto } from '@ngbplatform/ui'
import { expect, type APIResponse, type ElementHandle, type Locator, type Page, type Response, type TestInfo } from '@playwright/test'

type FlowLink = {
  from: string
  to: string
  code: 'based_on' | 'created_from' | 'reversal_of' | 'related_to' | 'supersedes'
}

type RegisterFieldExpectation = { label: string, display: string, literalText?: boolean }

type ReportDestination = { code: string, parameters: object, filters: object | null }

interface OutcomeChecks {
  postedRegisters(response: Response, type: string, id: string, operational: string[], reference: string[]): Promise<void>
  journalCreateAccess(expected: boolean, create: Locator, deniedRequest?: APIResponse): Promise<void>
  reportReference(field: string, control: Locator, display: string, target: string | ReportDestination, id: string): Promise<void>
  dashboardHome(control: Locator, path: string): Promise<void>
  dashboardContext(context: Locator, expected: string): Promise<void>
  currentRetry(failed: Response, message: Locator, retry: Locator, input: Locator, selected: string,
    result: Locator, expected: string, expectedMessage?: string): Promise<void>
  saveRetriedDraft(type: string, id: string | null, save: Locator, payloadPath: (string | number)[],
    expected: number, field: Locator, display: string): Promise<void>
  dashboardLayout(size: 'desktop' | 'mobile', toolbar: Locator, attention: Locator, metrics: Locator): Promise<void>
  businessCalendar(inputs: [Locator, Locator], expected: string): Promise<void>
  journalDraft(memo: string): Promise<void>
  flowAmount(type: string, id: string, expected: number, root?: { type: string, id: string }): Promise<void>
  registerFields(response: Response, type: string, id: string, code: string, recordId: number | string,
    expected: Record<string, RegisterFieldExpectation>): Promise<void>
  draftCommands(response: Response, type: string, id: string, codes: string[]): Promise<void>
  commandChange(type: string, id: string, code: string, trigger: () => Promise<void>,
    field: Locator, before: string, after: string): Promise<void>
  text(locator: Locator, expected: string | RegExp): Promise<void>
  value(locator: Locator, expected: string): Promise<void>
  disabled(locator: Locator): Promise<void>
  count(locator: Locator, expected: number): Promise<void>
  url(expected: string | RegExp): Promise<void>
  accessibleFields(editor: Locator, fields: { label: string, control: Locator }[]): Promise<void>
  figure(locator: Locator, expected: string): Promise<void>
  record(path: string, id: string): Promise<void>
  reportScope(response: Response, code: string, parameters: object, filters: object | null): Promise<void>
  metricReport(trigger: Locator, code: string, parameters: object, filters: object | null): Promise<void>
  metricList(trigger: Locator, kind: 'documents' | 'catalogs', code: string, query: URLSearchParams): Promise<void>
  navigation(control: Locator, expectedPath: string | null): Promise<void>
  reportRecord(response: Response, code: string, parameters: object, filters: object | null, id: string): Promise<void>
  flow(documentType: string, rootId: string, links: FlowLink[]): Promise<void>
  reference(documentType: string, rootId: string, field: string, sources: string[], relationship: FlowLink['code']): Promise<void>
  permissions(response: Response, path: string, expected: Record<string, unknown> | null, absent?: string[]): Promise<void>
  concurrent(requests: (() => Promise<APIResponse>)[], statuses: number[]): Promise<void>
  replay<T>(send: (key: string) => Promise<APIResponse>, identity: (body: T) => string): Promise<void>
  conflictingReplay(send: (key: string) => Promise<APIResponse>, code: string): Promise<void>
}

interface Access {
  authSubject: string | null
  isAuthenticated: boolean
  isActive: boolean
  isBootstrapAdmin: boolean
  roles: { code: string, isActive: boolean }[]
}

// Start before signing in. One test uses one actor and proves one matrix case.
export async function acceptanceProbe(page: Page, info: TestInfo) {
  const origin = process.env.NGB_TEST_ORIGIN
  const runId = process.env.NGB_TEST_RUN_ID
  expect(origin).toBeTruthy()
  expect(runId).toBeTruthy()
  let interactions = 0
  let assertions = 0
  const outcomes = new Set<string>()
  const evidence: Record<string, string[]> = Object.create(null)
  let activeChecks: Set<string> | undefined
  let lastReplay: { key: string, url: string } | undefined
  let access: Promise<Access> | undefined
  let mainMenu: Response | undefined
  const isMainMenu = (response: Response) =>
    response.url() === `${origin}/api/main-menu` && response.request().method() === 'GET'
  const missingApiPaths: string[] = []
  page.on('response', response => {
    if (isMainMenu(response)) mainMenu = response
    if (response.url() === `${origin}/api/security/me/access` && response.status() === 200) access = response.json()
    if (response.status() === 404 && response.url().startsWith(origin + '/api/')) {
      missingApiPaths.push(new URL(response.url()).pathname)
    }
  })
  await page.exposeBinding('__ngbAcceptanceInput', ({ frame }) => {
    if (frame === page.mainFrame() && new URL(frame.url()).origin === origin) interactions += 1
  })
  await page.addInitScript(() => {
    for (const type of ['click', 'input', 'keydown']) {
      document.addEventListener(type, event => {
        if (event.isTrusted) {
          const target = window as unknown as { __ngbAcceptanceInput: () => Promise<void> }
          void target.__ngbAcceptanceInput()
        }
      }, true)
    }
  })
  const probe = {
    async outcome(name: string, verify: (checks: OutcomeChecks) => Promise<void>) {
      expect(name.trim()).toBeTruthy()
      expect(outcomes.has(name)).toBe(false)
      const before = assertions
      expect(activeChecks).toBeUndefined()
      const checks = new Set<string>()
      activeChecks = checks
      try {
        await verify(probe)
      } finally {
        activeChecks = undefined
      }
      expect(assertions).toBeGreaterThan(before)
      outcomes.add(name)
      evidence[name] = [...checks]
    },
    async count(locator: Locator, expected: number) {
      await expect(locator).toHaveCount(expected)
      assertions += 1
    },
    async url(expected: string | RegExp) {
      await expect(page).toHaveURL(expected)
      assertions += 1
    },
    async text(locator: Locator, expected: string | RegExp) {
      await expect(locator).toBeVisible()
      await expect(locator).toHaveText(expected)
      assertions += 1
    },
    async value(locator: Locator, expected: string) {
      await expect(locator).toBeVisible()
      await expect(locator).toHaveValue(expected)
      assertions += 1
    },
    async disabled(locator: Locator) {
      await expect(locator).toBeVisible()
      await expect(locator).toBeDisabled()
      assertions += 1
    },
    async accessibleFields(editor: Locator, fields: { label: string, control: Locator }[]) {
      await expect(editor).toBeVisible()
      const wholeForm = await editor.evaluate(root =>
        root.matches('[data-ngb-entity-form]') || Boolean(root.querySelector('[data-ngb-entity-form]')))
      expect(wholeForm, 'Inspect the complete metadata editor').toBe(true)
      expect(fields.length).toBeGreaterThan(0)
      const checked: ElementHandle<HTMLElement | SVGElement>[] = []
      try {
        for (const { label, control } of fields) {
          expect(label.trim()).toBeTruthy()
          await expect(control).toBeVisible()
          await expect(control).toHaveAccessibleName(label)
          const handle = await control.elementHandle()
          if (!handle) throw new Error(`Field "${label}" was removed from the editor.`)
          checked.push(handle)
          expect(await editor.evaluate((root, element) => root.contains(element), handle)).toBe(true)
        }
        const missing = await editor.evaluate((root, controls) => {
          const selector = [
            'input:not([type="hidden"])', 'textarea', 'select', '[contenteditable="true"]',
            '[role="combobox"]', '[role="textbox"]', '[role="checkbox"]', '[role="switch"]', '[role="radio"]',
            '[role="spinbutton"]', '[role="slider"]', '[role="listbox"]', 'button[aria-haspopup]',
            'button[aria-expanded]', '[data-validation-key] button',
          ].join(', ')
          return [...root.querySelectorAll<HTMLElement>(selector)].filter(element =>
            element.getClientRects().length && getComputedStyle(element).visibility !== 'hidden'
            && !element.closest('[aria-hidden="true"]') && !controls.includes(element))
            .map(element => element.id || element.tagName)
        }, checked)
        expect(missing).toEqual([])
      } finally {
        await Promise.all(checked.map(handle => handle.dispose()))
      }
      assertions += 1
      activeChecks?.add('accessible-fields')
    },
    async figure(locator: Locator, expected: string) {
      expect(typeof expected).toBe('string')
      expect(expected.trim()).toMatch(/^\(?[+−-]?\s*(?:\p{Sc}|[A-Z]{3})?\s*\p{Nd}[\p{Nd}.,'’\s]*(?:%|\p{Sc}|[A-Z]{3})?\)?$/u)
      await probe.text(locator, expected)
      activeChecks?.add('figure')
    },
    async record(path: string, id: string) {
      expect(id).toMatch(/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i)
      expect(path.split('/').at(-1)).toBe(id)
      await expect.poll(() => new URL(page.url()).pathname).toBe(path)
      expect(new URL(page.url()).origin).toBe(origin)
      assertions += 1
      activeChecks?.add('record')
    },
    async reportScope(response: Response, code: string, parameters: object, filters: object | null) {
      expect(new URL(response.url()).origin).toBe(origin)
      expect(new URL(response.url()).pathname).toBe(`/api/reports/${encodeURIComponent(code)}/execute`)
      expect(response.ok()).toBe(true)
      expect(response.request().method()).toBe('POST')
      const request = response.request().postDataJSON()
      expect(request.parameters ?? {}).toEqual(parameters)
      expect(request.filters ?? {}).toEqual(filters ?? {})
      assertions += 1
      activeChecks?.add('report-scope')
    },
    async reportRecord(response: Response, code: string, parameters: object, filters: object | null, id: string) {
      expect(id).toMatch(/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i)
      expect(JSON.stringify([parameters, filters])).toContain(JSON.stringify(id))
      await expect.poll(() => new URL(page.url()).pathname).toBe(`/reports/${encodeURIComponent(code)}`)
      await probe.reportScope(response, code, parameters, filters)
      activeChecks?.add('record')
    },
    async metricReport(trigger: Locator, code: string, parameters: object, filters: object | null) {
      const path = `/reports/${encodeURIComponent(code)}`
      const execution = page.waitForResponse(response => new URL(response.url()).pathname === `/api${path}/execute`)
      await trigger.click()
      await expect.poll(() => new URL(page.url()).pathname).toBe(path)
      await probe.reportScope(await execution, code, parameters, filters)
      activeChecks?.add('metric-drilldown')
    },
    async metricList(trigger: Locator, kind: 'documents' | 'catalogs', code: string, query: URLSearchParams) {
      const path = `/${kind}/${encodeURIComponent(code)}`
      const loaded = page.waitForResponse(response => new URL(response.url()).pathname === `/api${path}` && response.request().method() === 'GET')
      await trigger.click()
      await expect.poll(() => new URL(page.url()).pathname).toBe(path)
      const response = await loaded
      expect(response.ok()).toBe(true)
      const url = new URL(response.url())
      expect(url.origin).toBe(origin)
      url.searchParams.sort()
      const expected = new URLSearchParams(query)
      expected.sort()
      expect(url.searchParams.toString()).toBe(expected.toString())
      assertions += 1
      activeChecks?.add('metric-drilldown')
    },
    async navigation(control: Locator, expectedPath: string | null) {
      if (expectedPath === null) {
        await expect(control).toBeHidden()
      } else {
        await control.click()
        await expect.poll(() => new URL(page.url()).pathname).toBe(expectedPath)
        expect(new URL(page.url()).origin).toBe(origin)
      }
      assertions += 1
      activeChecks?.add('navigation')
    },
    async journalCreateAccess(expected: boolean, create: Locator, deniedRequest?: APIResponse) {
      if (expected) {
        await expect(create).toBeVisible()
        await expect(create).toBeEnabled()
        await create.click()
        await expect(page).toHaveURL(origin + '/accounting/general-journal-entries/new')
        await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeEnabled()
      } else {
        expect(deniedRequest?.url()).toBe(origin + '/api/accounting/general-journal-entries')
        expect(deniedRequest?.status()).toBe(403)
        if (await create.count()) await expect(create).toBeDisabled()
        await page.goto(origin + '/accounting/general-journal-entries/new')
        const save = page.getByRole('button', { name: 'Save', exact: true })
        const editor = page.getByTestId('journal-entry-edit-page')
        const denial = page.getByText(/access denied|permission denied/i).first()
        await expect(editor.or(denial).first()).toBeVisible()
        if (await editor.isVisible()) {
          await expect(save).toBeDisabled()
          await expect(editor.getByPlaceholder('Explain the journal entry')).toBeDisabled()
        }
      }
      assertions += 1
      activeChecks?.add('journal-create-access')
    },
    async reportReference(field: string, control: Locator, display: string, target: string | ReportDestination, id: string) {
      expect(display.trim()).toBeTruthy()
      expect(display).not.toContain(id)
      await expect(control).toHaveText(display)
      expect(await control.evaluate(element => element.closest('td')?.dataset.column)).toBe(field)
      if (typeof target === 'string') {
        await control.click()
        await probe.record(target, id)
      } else {
        const execution = page.waitForResponse(response => new URL(response.url()).pathname
          === '/api/reports/' + encodeURIComponent(target.code) + '/execute')
        await control.click()
        await probe.reportRecord(await execution, target.code, target.parameters, target.filters, id)
      }
      activeChecks?.add('report-reference:' + field)
    },
    async dashboardHome(control: Locator, path: string) {
      const response = mainMenu ?? await page.waitForResponse(isMainMenu)
      expect(response.status()).toBe(200)
      const menu: MainMenuDto = await response.json()
      const group = menu.groups[0]
      expect(group?.items).toHaveLength(1)
      const [item] = group.items
      expect(group).toMatchObject({ label: item.label, icon: 'home' })
      expect(item).toMatchObject({ route: path, icon: 'home' })
      const button = page.getByTestId('site-sidebar-nav').getByRole('button', { name: item.label, exact: true })
      const home = control.and(button)
      await expect(home.locator('svg[data-icon="home"]')).toBeVisible()
      expect(new URL(page.url()).pathname).not.toBe(path)
      await home.click()
      await expect(page).toHaveURL(url => url.origin === origin && url.pathname === path)
      assertions += 1
      activeChecks?.add('dashboard-home')
    },
    async dashboardContext(context: Locator, expected: string) {
      expect(expected.trim()).toBeTruthy()
      await probe.text(context, expected)
      activeChecks?.add('dashboard-context')
    },
    async currentRetry(failed: Response, message: Locator, retry: Locator, input: Locator, selected: string,
      result: Locator, expected: string, expectedMessage?: string) {
      const request = failed.request()
      expect(failed.status()).toBeGreaterThanOrEqual(500)
      expect(new URL(failed.url()).origin).toBe(origin)
      await expect(message).toBeVisible()
      const explanation = (await message.innerText()).toLowerCase()
        .replace(failed.statusText().toLowerCase(), '')
        .replace(/\b(?:http|error|status(?:\s+code)?)\b/g, '')
      expect(explanation, 'Explain the failed operation to the user.').toMatch(/\p{L}/u)
      if (expectedMessage !== undefined) await expect(message).toHaveText(expectedMessage)
      const checkSelection = await input.evaluate(element => element.matches('input, textarea, select')) ? probe.value : probe.text
      await checkSelection(input, selected)
      await expect(retry).toBeEnabled()
      const response = page.waitForResponse(next => next.url() === failed.url()
        && next.request().method() === request.method() && next.request().postData() === request.postData())
      await retry.click()
      expect((await response).ok()).toBe(true)
      await checkSelection(input, selected)
      await expect(message).not.toBeVisible()
      expect(expected.trim()).toBeTruthy()
      await probe.text(result, expected)
      activeChecks?.add('current-retry')
    },
    async saveRetriedDraft(type: string, id: string | null, save: Locator, payloadPath: (string | number)[],
      expected: number, field: Locator, display: string) {
      expect(activeChecks?.has('current-retry')).toBe(true)
      expect(Number.isFinite(expected)).toBe(true)
      expect(payloadPath.length).toBeGreaterThan(0)
      const collection = '/documents/' + encodeURIComponent(type)
      const endpoint = origin + '/api' + collection
      const written = page.waitForResponse(response => response.url() === (id ? endpoint + '/' + id : endpoint)
        && response.request().method() === (id ? 'PUT' : 'POST'))
      await save.click()
      const response = await written
      expect(response.ok()).toBe(true)
      const document = await response.json()
      expect(document.id).toBeTruthy()
      if (id) expect(document.id).toBe(id)
      const path = collection + '/' + encodeURIComponent(document.id)
      const loaded = page.waitForResponse(r => r.url() === origin + '/api' + path + '/editor-state' && r.request().method() === 'GET')
      await page.goto(origin + path)
      const reopened = await loaded
      expect(reopened.ok()).toBe(true)
      const state = (await reopened.json()).document
      expect(state.id).toBe(document.id)
      expect(payloadPath.reduce((value, key) => value[key], state.payload)).toEqual(expected)
      const check = await field.evaluate(element => element.matches('input, textarea, select')) ? probe.value : probe.text
      await check(field, display)
      activeChecks?.add('persisted-retry:' + type)
    },
    async dashboardLayout(size: 'desktop' | 'mobile', toolbar: Locator, attention: Locator, metrics: Locator) {
      const width = await page.evaluate(() => window.innerWidth)
      expect(size === 'mobile' ? width <= 480 : width >= 1024).toBe(true)
      for (const element of [toolbar, attention, metrics]) await expect(element).toBeVisible()
      await expect(attention).toBeInViewport()
      await expect(attention.getByRole('button').or(attention.getByRole('link')).first()).toBeVisible()
      const controls = await toolbar.boundingBox()
      const action = await attention.boundingBox()
      const summary = await metrics.boundingBox()
      if (size === 'desktop') {
        expect(controls!.width).toBeLessThan(width * 0.75)
        expect(controls!.height).toBeLessThan(action!.height)
      }
      expect(controls!.y + controls!.height).toBeLessThanOrEqual(action!.y + 1)
      expect(action!.y).toBeLessThan(summary!.y)
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1)
      await info.attach('ngb-dashboard-' + size, { contentType: 'image/png', body: await page.screenshot({ scale: 'css' }) })
      assertions += 1
      activeChecks?.add('dashboard-layout:' + size)
    },
    async businessCalendar(inputs: [Locator, Locator], expected: string) {
      expect(expected).toMatch(/^\d{4}-\d{2}-\d{2}$/)
      const localDays = []
      for (const input of inputs) {
        expect(new URL(input.page().url()).origin).toBe(origin)
        localDays.push(await input.evaluate(() => new Date().toLocaleDateString('en-CA')))
        await expect(input).toBeVisible()
        if (await input.evaluate(element => element.tagName === 'TIME')) await expect(input).toHaveAttribute('datetime', expected)
        else await expect(input).toHaveValue(expected)
      }
      expect(localDays[0]).not.toBe(localDays[1])
      assertions += 1
      activeChecks?.add('business-calendar')
    },
    async journalDraft(memo: string) {
      expect(memo.trim()).toBeTruthy()
      const before = missingApiPaths.length
      const path = '/accounting/general-journal-entries'
      await page.goto(origin + path)
      await page.getByTestId('journal-entry-list-page').getByRole('button', { name: 'Create' }).click()
      await expect(page).toHaveURL(origin + path + '/new')
      const editor = page.getByTestId('journal-entry-edit-page')
      const input = editor.getByPlaceholder('Explain the journal entry')
      await expect(editor.getByText('HTTP 404', { exact: true })).toHaveCount(0)
      await input.fill(memo)
      const created = page.waitForResponse(r => r.url() === origin + '/api' + path && r.request().method() === 'POST')
      await editor.getByRole('button', { name: 'Save', exact: true }).click()
      const response = await created
      expect(response.ok()).toBe(true)
      const { document } = await response.json()
      await probe.record(path + '/' + document.id, document.id)
      // Save also persists the header and the draft lines after assigning the ID.
      await expect(editor.getByRole('button', { name: 'Save', exact: true })).toBeEnabled()
      const read = page.waitForResponse(r => r.url() === origin + '/api' + path + '/' + document.id && r.request().method() === 'GET')
      await page.reload()
      const reloaded = await read
      expect(reloaded.ok()).toBe(true)
      expect(await reloaded.json()).toMatchObject({ document: { id: document.id }, header: { memo } })
      await probe.value(input, memo)
      expect(missingApiPaths.slice(before), 'Unexpected missing API route while creating or reopening the journal').toEqual([])
      activeChecks?.add('journal-draft')
    },
    async flowAmount(type: string, id: string, expected: number, root = { type, id }) {
      expect(Number.isFinite(expected)).toBe(true)
      const path = '/documents/' + encodeURIComponent(root.type) + '/' + root.id
      const read = page.waitForResponse(r => r.url().split('?')[0] === origin + '/api' + path + '/graph' && r.request().method() === 'GET')
      await page.goto(origin + path + '/flow')
      const response = await read
      expect(response.ok()).toBe(true)
      const { nodes } = await response.json()
      const node = nodes.find((item: { typeCode: string, entityId: string }) => item.typeCode === type && item.entityId === id)
      expect(node).toMatchObject({ amount: expected })
      const amount = await page.evaluate(n => n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }), expected)
      const button = page.locator('[data-ngb-flow-document=' + JSON.stringify(id) + ']')
      await expect(button).toHaveAttribute('title', node.title)
      await probe.figure(button.getByText(amount, { exact: true }), amount)
      activeChecks?.add('flow-amount:' + type)
    },
    async postedRegisters(response: Response, type: string, id: string, operational: string[], reference: string[]) {
      const path = '/documents/' + encodeURIComponent(type) + '/' + id + '/effects'
      const url = new URL(response.url())
      expect(url.origin + url.pathname).toBe(origin + '/api' + path)
      expect(response.ok()).toBe(true)
      await expect.poll(() => new URL(page.url()).pathname).toBe(path)
      await expect(page.getByText('Posted', { exact: true })).toBeVisible()
      const snapshot = await response.json()
      const codes = (rows: { registerCode: string }[]) => [...new Set(rows.map(row => row.registerCode))].sort()
      const observed = [codes(snapshot.operationalRegisterMovements), codes(snapshot.referenceRegisterWrites)]
      expect(observed).toEqual([[...operational].sort(), [...reference].sort()])
      assertions += 1
      activeChecks?.add('registers:' + type + '/' + JSON.stringify(observed))
    },
    async registerFields(response: Response, type: string, id: string, code: string, recordId: number | string,
      expected: Record<string, RegisterFieldExpectation>) {
      const path = '/documents/' + encodeURIComponent(type) + '/' + id + '/effects'
      const url = new URL(response.url())
      expect(url.origin + url.pathname).toBe(origin + '/api' + path)
      expect(response.ok()).toBe(true)
      await expect.poll(() => new URL(page.url()).pathname).toBe(path)
      const { referenceRegisterWrites } = await response.json()
      const record = referenceRegisterWrites.find((item: { registerCode: string, recordId: number | string }) =>
        item.registerCode === code && item.recordId === recordId)
      expect(record).toBeDefined()
      expect(Object.keys(expected).length).toBeGreaterThan(0)
      expect(Object.keys(record.fields).sort()).toEqual(Object.keys(expected).sort())
      await page.getByRole('tab', { name: /^Reference Registers/ }).click()
      const panel = page.getByRole('tabpanel', { name: /^Reference Registers/ })
      const row = panel.locator('[data-ngb-register-row=' + JSON.stringify(String(recordId)) + ']')
      for (const [key, { label, display, literalText }] of Object.entries(expected)) {
        expect(label.trim()).toBeTruthy()
        expect(display.trim()).toBeTruthy()
        if (literalText) expect(record.fields[key]).toBe(display)
        else expect(display).not.toMatch(/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$|^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/i)
        await expect(row.locator('[data-ngb-register-column="fields"]').getByText(label + ': ' + display, { exact: true })).toBeVisible()
      }
      assertions += 1
      activeChecks?.add('register:' + type + '/' + code)
    },
    async draftCommands(response: Response, type: string, id: string, codes: string[]) {
      expect(response.url()).toBe(origin + '/api/documents/' + encodeURIComponent(type) + '/' + id + '/editor-state')
      expect(response.ok()).toBe(true)
      const state = await response.json()
      expect(state.document).toMatchObject({ id, status: 'Draft' })
      const lifecycle = ['post', 'unpost', 'repost', 'mark_for_deletion', 'unmark_for_deletion']
      const commands = state.actions.filter((a: { code: string, executionKind: string }) =>
        a.executionKind === 'Command' && !lifecycle.includes(a.code)).map((a: { code: string }) => a.code).sort()
      expect(commands).toEqual([...codes].sort())
      assertions += 1
      activeChecks?.add('commands:' + type + '/' + JSON.stringify(commands))
    },
    async commandChange(type: string, id: string, code: string, trigger: () => Promise<void>,
      field: Locator, before: string, after: string) {
      expect(after).not.toBe(before)
      const check = await field.evaluate(element => element.matches('input, textarea, select')) ? probe.value : probe.text
      await check(field, before)
      const path = origin + '/api/documents/' + encodeURIComponent(type) + '/' + id + '/actions/' + encodeURIComponent(code)
      const execution = page.waitForResponse(r => r.url() === path && r.request().method() === 'POST')
      await trigger()
      expect((await execution).ok()).toBe(true)
      await check(field, after)
      await page.reload()
      await check(field, after)
      activeChecks?.add('command:' + type + '/' + code)
    },
    async flow(documentType: string, rootId: string, links: FlowLink[]) {
      expect(links.length).toBeGreaterThan(0)
      const path = `/documents/${encodeURIComponent(documentType)}/${rootId}`
      const loaded = page.waitForResponse(r => r.url().split('?')[0] === origin + '/api' + path + '/graph' && r.request().method() === 'GET')
      await page.goto(`${origin}${path}/flow`)
      const response = await loaded
      expect(response.ok()).toBe(true)
      const graph: {
        nodes: { nodeId: string, entityId: string, title: string }[]
        edges: { fromNodeId: string, toNodeId: string, relationshipType: string }[]
      } = await response.json()
      const nodes = new Map(graph.nodes.map(node => [node.entityId, node]))
      const ids = new Set(links.flatMap(edge => [edge.from, edge.to]))
      expect(ids.has(rootId)).toBe(true)
      const connected = new Set([rootId])
      for (const id of connected) {
        for (const { from, to } of links) {
          if (from === id) connected.add(to)
          if (to === id) connected.add(from)
        }
      }
      expect(connected).toEqual(ids)
      for (const id of ids) {
        const node = nodes.get(id)
        expect(node, `Missing related document ${id}`).toBeDefined()
        const button = page.locator('[data-ngb-flow-document=' + JSON.stringify(id) + ']')
        await expect(button).toBeVisible()
        await expect(button).toHaveAttribute('title', node!.title)
      }
      for (const link of links) {
        expect(link.from).not.toBe(link.to)
        expect(graph.edges).toContainEqual(expect.objectContaining({
          relationshipType: link.code, fromNodeId: nodes.get(link.from)!.nodeId, toNodeId: nodes.get(link.to)!.nodeId,
        }))
      }
      assertions += 1
      activeChecks?.add('flow')
      if (links.some(first => links.some(second => first.to === second.from && first.from !== second.to))) {
        activeChecks?.add('chain')
      }
    },
    async reference(documentType: string, rootId: string, field: string, sources: string[], relationship: FlowLink['code']) {
      const path = `/documents/${encodeURIComponent(documentType)}/${rootId}`
      // A preceding Save may still be refreshing this same editor. Observe a fresh document load.
      await page.goto('about:blank')
      const loaded = page.waitForResponse(response => new URL(response.url()).pathname === `/api${path}/editor-state`)
      await page.goto(`${origin}${path}`)
      const response = await loaded
      expect(response.ok()).toBe(true)
      const { document } = await response.json()
      expect(document.id).toBe(rootId)
      const separator = field.lastIndexOf('.')
      const values = separator < 0
        ? [document.payload.fields[field]]
        : document.payload.parts[field.slice(0, separator)].rows.map((row: Record<string, unknown>) => row[field.slice(separator + 1)])
      const ids = values.filter(Boolean).map((value: string | { id: string }) => typeof value === 'string' ? value : value.id)
      expect([...new Set(ids)].sort()).toEqual([...sources].sort())
      await probe.flow(documentType, rootId, sources.map(to => ({ from: rootId, to, code: relationship })))
      activeChecks?.add(`reference:${field}`)
    },
    async permissions(response: Response, path: string, expected: Record<string, unknown> | null, absent: string[] = []) {
      expect(response.url().split('?')[0]).toBe(new URL(path, origin).href)
      if (expected === null) {
        expect(response.status()).toBe(403)
      } else {
        expect(response.ok()).toBe(true)
        expect(Object.keys(expected).length + absent.length).toBeGreaterThan(0)
        const body = await response.json()
        for (const [key, value] of Object.entries(expected)) expect(body).toHaveProperty(key, value)
        for (const key of absent) expect(body).not.toHaveProperty(key)
      }
      assertions += 1
      activeChecks?.add('permissions')
    },
    async concurrent(requests: (() => Promise<APIResponse>)[], statuses: number[]) {
      expect(requests.length).toBeGreaterThanOrEqual(2)
      const responses = await Promise.all(requests.map(send => send()))
      for (const response of responses) expect(new URL(response.url()).origin).toBe(origin)
      expect(responses.map(response => response.status()).sort()).toEqual([...statuses].sort())
      assertions += 1
      activeChecks?.add('concurrent')
    },
    async replay<T>(send: (key: string) => Promise<APIResponse>, identity: (body: T) => string) {
      const key = randomUUID()
      const first = await send(key)
      expect(first.ok()).toBe(true)
      expect(new URL(first.url()).origin).toBe(origin)
      const id = identity(await first.json())
      expect(id).toBeTruthy()
      const repeated = await send(key)
      expect(repeated.url()).toBe(first.url())
      expect(repeated.ok()).toBe(true)
      expect(identity(await repeated.json())).toBe(id)
      lastReplay = { key, url: first.url() }
      assertions += 1
      activeChecks?.add('replay')
    },
    async conflictingReplay(send: (key: string) => Promise<APIResponse>, code: string) {
      if (!lastReplay) throw new Error('Exercise the original request and its replay first.')
      const response = await send(lastReplay.key)
      expect(response.url()).toBe(lastReplay.url)
      expect(response.status()).toBe(409)
      const body = await response.json()
      expect(body.error?.code).toBe(code)
      assertions += 1
      activeChecks?.add('conflicting-replay')
    },
    async finish(caseId: string, role: string) {
      const actor = await access
      expect(actor?.isAuthenticated).toBe(true)
      expect(actor?.isActive).toBe(true)
      expect(actor?.authSubject).toBeTruthy()
      if (role === 'ngb-admin') {
        expect(actor?.isBootstrapAdmin).toBe(true)
      } else {
        expect(actor?.isBootstrapAdmin).toBe(false)
        expect(actor?.roles.filter(item => item.isActive).map(item => item.code)).toEqual([role])
      }
      await expect.poll(() => interactions).toBeGreaterThan(0)
      expect(assertions).toBeGreaterThan(0)
      expect(new URL(page.url()).origin).toBe(origin)
      await info.attach('ngb-acceptance', {
        contentType: 'application/json',
        body: Buffer.from(JSON.stringify({ schemaVersion: outcomes.size ? 2 : 1, outcomes: [...outcomes], evidence, caseId, runId, origin,
          role, subject: actor!.authSubject, url: page.url(), interactions, assertions })),
      })
    },
  }
  return probe
}
