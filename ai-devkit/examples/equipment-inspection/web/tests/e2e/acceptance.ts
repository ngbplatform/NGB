import { randomUUID } from 'node:crypto'
import { expect, type APIResponse, type ElementHandle, type Locator, type Page, type Response, type TestInfo } from '@playwright/test'

type FlowLink = {
  from: string
  to: string
  code: 'based_on' | 'created_from' | 'reversal_of' | 'related_to' | 'supersedes'
}

interface OutcomeChecks {
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
  page.on('response', response => {
    if (response.url() === `${origin}/api/security/me/access` && response.status() === 200) access = response.json()
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
    async flow(documentType: string, rootId: string, links: FlowLink[]) {
      expect(links.length).toBeGreaterThan(0)
      const path = `/documents/${encodeURIComponent(documentType)}/${rootId}`
      const loaded = page.waitForResponse(response => new URL(response.url()).pathname === `/api${path}/graph`)
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
        await expect(page.getByRole('button', { name: node!.title, exact: true })).toBeVisible()
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
