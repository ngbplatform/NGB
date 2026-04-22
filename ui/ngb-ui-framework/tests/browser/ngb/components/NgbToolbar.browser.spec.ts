import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbToolbar from '../../../../src/ngb/components/NgbToolbar.vue'

function top(locator: { element(): Element }): number {
  return locator.element().getBoundingClientRect().top
}

function bottom(locator: { element(): Element }): number {
  return locator.element().getBoundingClientRect().bottom
}

const ToolbarHarness = defineComponent({
  setup() {
    return () => h(
      NgbToolbar,
      null,
      {
        left: () => h('input', { 'aria-label': 'Search', style: 'width: 220px' }),
        default: () => [
          h('button', { type: 'button' }, 'Add item'),
          h('button', { type: 'button' }, 'Export'),
        ],
      },
    )
  },
})

test('moves toolbar actions below the primary controls on narrow viewports', async () => {
  await page.viewport(375, 800)

  const view = await render(ToolbarHarness)

  const search = view.getByRole('textbox', { name: 'Search' })
  const addItem = view.getByRole('button', { name: 'Add item' })

  await expect.element(search).toBeVisible()
  await expect.element(addItem).toBeVisible()

  expect(top(addItem)).toBeGreaterThanOrEqual(bottom(search))
})

test('keeps toolbar actions on the same row on wide viewports', async () => {
  await page.viewport(1280, 900)

  const view = await render(ToolbarHarness)

  const search = view.getByRole('textbox', { name: 'Search' })
  const addItem = view.getByRole('button', { name: 'Add item' })

  await expect.element(search).toBeVisible()
  await expect.element(addItem).toBeVisible()

  expect(Math.abs(top(addItem) - top(search))).toBeLessThan(24)
})
