import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'

import NgbValidationSummary from '../../../../../src/ngb/components/forms/NgbValidationSummary.vue'

test('renders the heading and all validation messages when messages are present', async () => {
  await page.viewport(1280, 900)

  const view = await render(NgbValidationSummary, {
    props: {
      messages: ['Code is required.', 'Name is required.'],
    },
  })

  await expect.element(view.getByText('Please fix the following', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Code is required.', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Name is required.', { exact: true })).toBeVisible()
})

test('exposes an assertive alert region with list semantics for validation errors', async () => {
  await page.viewport(1280, 900)

  await render(NgbValidationSummary, {
    props: {
      messages: ['Code is required.', 'Name is required.'],
    },
  })

  const alert = document.querySelector('[role="alert"]') as HTMLElement | null
  const list = alert?.querySelector('ul') as HTMLUListElement | null

  expect(alert).not.toBeNull()
  expect(alert?.getAttribute('aria-live')).toBe('assertive')
  expect(alert?.getAttribute('aria-atomic')).toBe('true')
  expect(list).not.toBeNull()
  expect(list?.querySelectorAll('li')).toHaveLength(2)
})

test('renders nothing when the messages list is empty', async () => {
  await page.viewport(1280, 900)

  await render(NgbValidationSummary, {
    props: {
      messages: [],
    },
  })

  expect(document.body.textContent?.includes('Please fix the following')).toBe(false)
})
