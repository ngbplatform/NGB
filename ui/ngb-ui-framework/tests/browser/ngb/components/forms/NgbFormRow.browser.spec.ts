import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbFormRow from '../../../../../src/ngb/components/forms/NgbFormRow.vue'

function rect(locator: { element(): Element }): DOMRect {
  return locator.element().getBoundingClientRect()
}

const FormRowHarness = defineComponent({
  setup() {
    return () => h(
      NgbFormRow,
      {
        label: 'Property',
        hint: 'Choose the target property',
        error: 'Property is required',
      },
      {
        default: () => h('input', { 'aria-label': 'Property value' }),
      },
    )
  },
})

test('stacks the label above the field on narrow viewports', async () => {
  await page.viewport(375, 800)

  const view = await render(FormRowHarness)

  const label = view.getByText('Property', { exact: true })
  const field = view.getByRole('textbox', { name: 'Property value' })

  await expect.element(label).toBeVisible()
  await expect.element(field).toBeVisible()
  await expect.element(view.getByText('Property is required')).toBeVisible()

  expect(rect(field).top).toBeGreaterThanOrEqual(rect(label).bottom)
})

test('keeps the label and field side-by-side on wide viewports', async () => {
  await page.viewport(1280, 900)

  const view = await render(FormRowHarness)

  const label = view.getByText('Property', { exact: true })
  const field = view.getByRole('textbox', { name: 'Property value' })

  await expect.element(label).toBeVisible()
  await expect.element(field).toBeVisible()

  const labelRect = rect(label)
  const fieldRect = rect(field)

  expect(fieldRect.left).toBeGreaterThan(labelRect.left)
  expect(Math.abs(fieldRect.top - labelRect.top)).toBeLessThan(24)
})
