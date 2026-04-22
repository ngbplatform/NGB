import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbPickerPopover from '../../../../src/ngb/primitives/NgbPickerPopover.vue'

async function waitForDomUpdate() {
  await new Promise((resolve) => window.setTimeout(resolve, 0))
}

const PickerPopoverHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
    grouped: {
      type: Boolean,
      default: false,
    },
    readonly: {
      type: Boolean,
      default: false,
    },
    displayValue: {
      type: String,
      default: '',
    },
  },
  setup(props) {
    return () => h(NgbPickerPopover, {
      displayValue: props.displayValue,
      placeholder: 'Choose month',
      disabled: props.disabled,
      grouped: props.grouped,
      readonly: props.readonly,
      panelClass: 'w-[420px]',
    }, {
      header: () => h('div', 'Popover header'),
      default: ({ close }: { close: () => void }) => h('div', [
        h('div', { 'data-testid': 'popover-body' }, 'Popover body'),
        h('button', { type: 'button', onClick: close }, 'Close from body'),
      ]),
      footer: ({ close }: { close: () => void }) => h('button', { type: 'button', onClick: close }, 'Close from footer'),
    })
  },
})

test('opens the popover, renders slot regions, and closes via slot-provided close handlers', async () => {
  await page.viewport(1280, 900)

  const view = await render(PickerPopoverHarness)

  await view.getByRole('button', { name: /Choose month/i }).click()
  await expect.element(view.getByText('Popover header', { exact: true })).toBeVisible()
  await expect.element(view.getByTestId('popover-body')).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Close from footer' })).toBeVisible()

  const panel = document.querySelector('[data-testid="popover-body"]')?.closest('.w-\\[420px\\]') as HTMLElement | null
  expect(panel?.className).toContain('w-[420px]')

  await view.getByRole('button', { name: 'Close from body' }).click()
  await waitForDomUpdate()
  expect(document.body.textContent?.includes('Popover body')).toBe(false)
})

test('supports grouped styling and keeps readonly or disabled triggers inactive', async () => {
  await page.viewport(1280, 900)

  const groupedView = await render(PickerPopoverHarness, {
    props: {
      grouped: true,
      displayValue: 'April 2026',
    },
  })

  const groupedButton = groupedView.getByRole('button', { name: /April 2026/i }).element() as HTMLButtonElement
  expect(groupedButton.className).toContain('h-full')

  const readonlyView = await render(PickerPopoverHarness, {
    props: {
      readonly: true,
      displayValue: 'May 2026',
    },
  })

  const readonlyButton = readonlyView.getByRole('button', { name: /May 2026/i }).element() as HTMLButtonElement
  expect(readonlyButton.disabled).toBe(true)

  const disabledView = await render(PickerPopoverHarness, {
    props: {
      disabled: true,
    },
  })
  const disabledButton = disabledView.getByRole('button', { name: /Choose month/i }).element() as HTMLButtonElement
  expect(disabledButton.disabled).toBe(true)
})
