import { readFile } from 'node:fs/promises'

import { expect, type APIRequestContext, type Page, type Request } from '@playwright/test'

type ContentScenario = {
  objectUrl: string
  storageEndpoint: string
  api: APIRequestContext
}

export async function verifyAttachmentsAndNotes(page: Page, scenario: ContentScenario): Promise<void> {
  const uploads: Request[] = []
  const trackUpload = (request: Request) => {
    if (request.url().startsWith(scenario.storageEndpoint) && request.method() === 'PUT') {
      uploads.push(request)
    }
  }

  page.on('request', trackUpload)
  try {
    await page.goto(scenario.objectUrl)
    await page.getByRole('button', { name: 'More actions', exact: true }).click()
    await page.getByRole('menuitem', { name: 'Attachments (0)', exact: true }).click()

    const completed = page.waitForResponse((response) =>
      /\/api\/attachments\/[^/]+\/complete$/.test(new URL(response.url()).pathname))
    await page.getByLabel('Choose attachment').setInputFiles({
      name: 'résumé.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('NGB real storage fixture\n'),
    })

    const response = await completed
    expect(response.ok()).toBe(true)
    const attachment = await response.json()
    await expect(page.getByText('résumé.txt', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Close', exact: true }).last().click()
    await page.getByRole('button', { name: 'More actions', exact: true }).click()
    await page.getByRole('menuitem', { name: 'Attachments (1)', exact: true }).click()

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('button', { name: 'Download', exact: true }).click(),
    ])
    // Native downloads may use decomposed Unicode on macOS.
    expect(download.suggestedFilename().normalize('NFC')).toBe('résumé.txt')
    expect(await readFile((await download.path())!, 'utf8')).toBe('NGB real storage fixture\n')

    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(page.getByText('No attachments yet.')).toBeVisible()
    await page.getByRole('button', { name: 'Close', exact: true }).last().click()
    await page.getByRole('button', { name: 'More actions', exact: true }).click()
    await expect(page.getByRole('menuitem', { name: 'Attachments (0)', exact: true })).toBeVisible()
    const denied = await scenario.api.post(`/api/attachments/${attachment.id}/download`)
    expect(denied.status()).toBe(409)

    await page.getByRole('menuitem', { name: 'Notes (0)', exact: true }).click()
    await page.getByRole('textbox', { name: 'Add note' }).fill('<b>Plain text</b>\nSecond line')
    await page.getByRole('button', { name: 'Add note', exact: true }).click()
    await expect(page.getByText('<b>Plain text</b>')).toBeVisible()
    await page.reload()
    await page.getByRole('button', { name: 'More actions', exact: true }).click()
    await page.getByRole('menuitem', { name: 'Notes (1)', exact: true }).click()
    await expect(page.getByText('<b>Plain text</b>')).toBeVisible()

    await page.getByRole('button', { name: 'Edit', exact: true }).click()
    await page.getByRole('textbox', { name: 'Edit note' }).fill('Updated plain note')
    await page.getByRole('button', { name: 'Save note' }).click()
    await expect(page.getByText('Updated plain note', { exact: true })).toBeVisible()
    await page.reload()
    await page.getByRole('button', { name: 'More actions', exact: true }).click()
    await page.getByRole('menuitem', { name: 'Notes (1)', exact: true }).click()
    await expect(page.getByText('Updated plain note', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(page.getByText('No notes yet.')).toBeVisible()
    await page.getByRole('button', { name: 'Close', exact: true }).last().click()
    await page.getByRole('button', { name: 'More actions', exact: true }).click()
    await expect(page.getByRole('menuitem', { name: 'Notes (0)', exact: true })).toBeVisible()

    expect(uploads).toHaveLength(1)
    const headers = await uploads[0]!.allHeaders()
    expect(headers.authorization).toBeUndefined()
    expect(headers.cookie).toBeUndefined()
  } finally {
    page.off('request', trackUpload)
  }
}
