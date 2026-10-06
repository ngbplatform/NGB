import { expect, request, test } from '@playwright/test'

import { verifyAttachmentsAndNotes } from '../support/contentScenarios'
import { readContentStackSettings } from './contentRealStack'

for (const kind of ['catalog', 'journal'] as const) {
  test(`attachments and notes preserve the ${kind} through the real stack`, async ({ page }) => {
    const settings = await readContentStackSettings()
    const api = await request.newContext({
      baseURL: settings.api,
      extraHTTPHeaders: { authorization: `Bearer ${settings.token}` },
    })

    try {
      const collection = kind === 'catalog' ? '/api/catalogs/pm.party' : '/api/accounting/general-journal-entries'
      const response = await api.post(collection, {
        data: kind === 'catalog'
          ? { fields: { display: 'Attachment browser fixture' } }
          : { dateUtc: '2026-10-01T00:00:00Z' },
      })
      expect(response.ok()).toBe(true)
      const created = await response.json()
      const id = kind === 'catalog' ? created.id : created.document.id
      const objectPath = `${collection.replace(/^\/api/, '')}/${id}`
      const before = await (await api.get(`${collection}/${id}`)).json()

      await page.goto(`${settings.webOrigin}${objectPath}`)
      await page.locator('input[name="username"]').fill(settings.username)
      await page.locator('input[name="password"]').fill(settings.password)
      await page.locator('button[type="submit"], input[type="submit"]').first().click()
      await expect(page.getByTestId('site-shell')).toBeVisible()

      await verifyAttachmentsAndNotes(page, {
        objectUrl: `${settings.webOrigin}${objectPath}`,
        storageEndpoint: settings.storage,
        api,
      })

      const after = await (await api.get(`${collection}/${id}`)).json()
      expect(after).toEqual(before)
    } finally {
      await api.dispose()
    }
  })
}
