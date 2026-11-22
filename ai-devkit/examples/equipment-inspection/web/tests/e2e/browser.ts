import { expect, test as base } from '@playwright/test'

export const test = base.extend<{ pageErrors: void }>({
  pageErrors: [async ({ context }, use, info) => {
    const errors: string[] = []
    context.on('weberror', event => errors.push(event.error().message))
    await use()
    await info.attach('ngb-page-errors', {
      contentType: 'application/json',
      body: Buffer.from(JSON.stringify(errors)),
    })
    expect(errors, 'Unhandled JavaScript errors in the browser context').toEqual([])
  }, { auto: true }],
})
