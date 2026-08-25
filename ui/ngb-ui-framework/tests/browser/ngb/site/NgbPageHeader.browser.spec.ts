import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbPageHeader from '../../../../src/ngb/layout/NgbPageHeader.vue'

function top(locator: { element(): Element }): number {
  return locator.element().getBoundingClientRect().top
}

function bottom(locator: { element(): Element }): number {
  return locator.element().getBoundingClientRect().bottom
}

const PageHeaderHarness = defineComponent({
  setup() {
    return () => h(
      NgbPageHeader,
      {
        title: 'Properties',
        canBack: true,
      },
      {
        secondary: () => h('span', 'Portfolio summary'),
        actions: () => h('button', { type: 'button' }, 'Refresh'),
      },
    )
  },
})

test('stacks header actions below the title on narrow viewports', async () => {
  await page.viewport(375, 800)

  const view = await render(PageHeaderHarness)

  const title = view.getByText('Properties')
  const action = view.getByRole('button', { name: 'Refresh' })

  await expect.element(title).toBeVisible()
  await expect.element(action).toBeVisible()

  expect(top(action)).toBeGreaterThanOrEqual(bottom(title))
})

test('keeps header actions aligned with the title on wide viewports', async () => {
  await page.viewport(1280, 900)

  const view = await render(PageHeaderHarness)

  const title = view.getByText('Properties')
  const action = view.getByRole('button', { name: 'Refresh' })

  await expect.element(title).toBeVisible()
  await expect.element(action).toBeVisible()

  expect(top(action)).toBeLessThan(bottom(title))
})

test('renders compact headers without back or secondary regions and default headers with breadcrumbs', async () => {
  await page.viewport(1280, 900)

  const view = await render(NgbPageHeader, {
    props: {
      title: 'Invoice',
      breadcrumbs: ['Sales', 'Invoices'],
      variant: 'compact',
    },
    slots: {
      secondary: () => h('span', 'Should stay hidden'),
    },
  })

  expect(document.body.textContent?.includes('Sales')).toBe(false)
  expect(document.body.textContent?.includes('Should stay hidden')).toBe(false)
  expect(document.querySelector('button[aria-label="Back"]')).toBeNull()
  expect(view.getByText('Invoice', { exact: true }).element().className).toContain('text-base')

  const breadcrumbView = await render(NgbPageHeader, {
    props: {
      title: 'Invoice details',
      breadcrumbs: ['Sales', 'Invoices'],
      variant: 'default',
    },
  })
  await expect.element(breadcrumbView.getByText('Sales', { exact: true })).toBeVisible()
  await expect.element(breadcrumbView.getByText('Invoices', { exact: true })).toBeVisible()
})
