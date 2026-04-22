import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbTooltip from '../../../../src/ngb/primitives/NgbTooltip.vue'

const TooltipHarness = defineComponent({
  setup() {
    return () => h('div', { 'data-testid': 'tooltip-wrap' }, [
      h(NgbTooltip, { text: 'Helpful hint' }, {
        default: () => h('button', { type: 'button' }, 'Tooltip target'),
      }),
    ])
  },
})

test('shows the tooltip on hover and focus, then hides it when interaction ends', async () => {
  await page.viewport(1280, 900)

  const view = await render(TooltipHarness)

  const host = document.querySelector('[data-testid="tooltip-wrap"] > span') as HTMLElement
  const button = view.getByRole('button', { name: 'Tooltip target' }).element() as HTMLButtonElement

  host.dispatchEvent(new MouseEvent('mouseenter'))
  await expect.element(view.getByRole('tooltip')).toHaveTextContent('Helpful hint')

  host.dispatchEvent(new MouseEvent('mouseleave'))
  await expect.poll(() => document.querySelector('[role="tooltip"]')).toBeNull()

  button.dispatchEvent(new FocusEvent('focusin', { bubbles: true }))
  await expect.element(view.getByRole('tooltip')).toHaveTextContent('Helpful hint')

  button.dispatchEvent(new FocusEvent('focusout', { bubbles: true }))
  await expect.poll(() => document.querySelector('[role="tooltip"]')).toBeNull()
})
