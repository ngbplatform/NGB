import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbDatePicker from '../../../../src/ngb/primitives/NgbDatePicker.vue'
import { toDateOnlyValue } from '../../../../src/ngb/utils/dateValues'

function dayButton(label: string): HTMLButtonElement {
  const button = Array.from(document.querySelectorAll('button')).find((node) => node.textContent?.trim() === label)
  expect(button).toBeTruthy()
  return button as HTMLButtonElement
}

const DatePickerHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
    readonly: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const value = ref<string | null>('2026-03-15')

    return () => h('div', [
      h(NgbDatePicker, {
        modelValue: value.value,
        placeholder: 'Select date',
        disabled: props.disabled,
        readonly: props.readonly,
        'onUpdate:modelValue': (next: string | null) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'date-state' }, `state:${value.value ?? 'none'}`),
    ])
  },
})

test('picks a day, clears the value, and restores today from the popover footer', async () => {
  await page.viewport(1280, 900)

  const view = await render(DatePickerHarness)

  await view.getByRole('button', { name: /03\/15\/2026/i }).click()
  dayButton('20').click()
  await expect.element(view.getByTestId('date-state')).toHaveTextContent('state:2026-03-20')

  await view.getByRole('button', { name: /03\/20\/2026/i }).click()
  await view.getByRole('button', { name: 'Clear' }).click()
  await expect.element(view.getByTestId('date-state')).toHaveTextContent('state:none')

  await view.getByRole('button', { name: /Select date/i }).click()
  await view.getByRole('button', { name: 'Today' }).click()
  await expect.element(view.getByTestId('date-state')).toHaveTextContent(`state:${toDateOnlyValue(new Date())}`)
})

test('disables the trigger when disabled', async () => {
  await page.viewport(1280, 900)

  const disabledView = await render(DatePickerHarness, {
    props: {
      disabled: true,
    },
  })
  expect((disabledView.getByRole('button', { name: /03\/15\/2026/i }).element() as HTMLButtonElement).disabled).toBe(true)
})

test('disables the trigger when readonly', async () => {
  await page.viewport(1280, 900)

  const readonlyView = await render(DatePickerHarness, {
    props: {
      readonly: true,
    },
  })
  expect((readonlyView.getByRole('button', { name: /03\/15\/2026/i }).element() as HTMLButtonElement).disabled).toBe(true)
})
