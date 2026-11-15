import { expect, type Locator, type Page, type TestInfo } from '@playwright/test'

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
  return {
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
        body: Buffer.from(JSON.stringify({ schemaVersion: 1, caseId, runId, origin,
          role, subject: actor!.authSubject, url: page.url(), interactions, assertions })),
      })
    },
  }
}
