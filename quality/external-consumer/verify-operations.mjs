import assert from 'node:assert/strict'
import { randomBytes } from 'node:crypto'
import { expect } from '@playwright/test'

export async function verifyStarterOperations(page, { urls, email, seqPassword, pgAdminPassword }) {
  const context = page.context()
  for (const [label, url] of [['Background Jobs', urls.jobs], ['Health', urls.health]]) {
    const previousCount = context.pages().length
    const opened = context.waitForEvent('page')
    await page.getByRole('button', { name: label, exact: true }).click()
    const consolePage = await opened
    await consolePage.waitForURL(url, { timeout: 30_000 })
    await consolePage.waitForLoadState('networkidle')
    assert.equal(context.pages().length, previousCount + 1, 'Each menu click must open exactly one console.')
    if (label === 'Health') {
      await expect.poll(async () => consolePage.evaluate(async () => {
        const response = await fetch('/health-ui-api')
        if (!response.ok) return []
        const results = await response.json()
        return results.map(result => result.status)
      }), { timeout: 30_000 }).toEqual(['Healthy', 'Healthy', 'Healthy'])
    }
    await consolePage.close()
  }

  const pgadmin = await context.newPage()
  await pgadmin.goto(urls.pgadmin)
  await pgadmin.locator('input[name="email"]').fill(email)
  await pgadmin.locator('input[name="password"]').fill(pgAdminPassword)
  await pgadmin.locator('input[type="submit"], button[type="submit"]').first().click()
  await pgadmin.waitForURL(new URL('/browser/', urls.pgadmin).href, { timeout: 30_000 })
  await pgadmin.close()

  const seq = await context.newPage()
  await seq.goto(urls.seq)
  await seq.getByLabel('Username', { exact: true }).fill(email)
  await seq.getByLabel('Password', { exact: true }).fill(seqPassword)
  const loginResponse = seq.waitForResponse(response => new URL(response.url()).pathname === '/api/users/login')
  await seq.getByRole('button', { name: 'Log in', exact: true }).click()
  const login = await loginResponse
  const result = await login.json()
  if (result.MustChangePassword) {
    // Seq requires changing its first-run password; this identity is disposable.
    const password = randomBytes(32).toString('base64url')
    await seq.getByLabel('New password', { exact: true }).fill(password)
    await seq.getByLabel('Repeat new password', { exact: true }).fill(password)
    const changed = seq.waitForResponse(response => new URL(response.url()).pathname === '/api/users/login')
    await seq.getByRole('button', { name: 'Log in', exact: true }).click()
    assert.equal((await changed).status(), 200, 'Seq initial password change must succeed.')
  } else {
    assert.equal(login.status(), 200, 'Seq login must succeed.')
  }
  await seq.getByRole('button', { name: 'Log in', exact: true }).waitFor({ state: 'hidden' })
  await expect.poll(async () => seq.evaluate(async () => {
    const response = await fetch('/api/events?count=5')
    if (!response.ok) return 0
    return (await response.json()).length
  }), { timeout: 30_000 }).toBeGreaterThan(0)
  await seq.close()
}
