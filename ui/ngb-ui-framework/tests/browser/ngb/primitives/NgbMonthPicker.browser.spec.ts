import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbMonthPicker from '../../../../src/ngb/primitives/NgbMonthPicker.vue'
import { currentMonthValue } from '../../../../src/ngb/utils/dateValues'

const MonthPickerHarness = defineComponent({
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
    const value = ref<string | null>('2026-03')

    return () => h('div', [
      h(NgbMonthPicker, {
        modelValue: value.value,
        placeholder: 'Select month',
        disabled: props.disabled,
        readonly: props.readonly,
        'onUpdate:modelValue': (next: string | null) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'month-state' }, `state:${value.value ?? 'none'}`),
    ])
  },
})

test('picks a month, clears the value, and restores the current month', async () => {
  await page.viewport(1280, 900)

  const view = await render(MonthPickerHarness)

  await view.getByRole('button', { name: /March 2026/i }).click()
  await view.getByRole('button', { name: 'Apr' }).click()
  await expect.element(view.getByTestId('month-state')).toHaveTextContent('state:2026-04')

  await view.getByRole('button', { name: /April 2026/i }).click()
  await view.getByRole('button', { name: 'Clear' }).click()
  await expect.element(view.getByTestId('month-state')).toHaveTextContent('state:none')

  await view.getByRole('button', { name: /Select month/i }).click()
  await view.getByRole('button', { name: 'This month' }).click()
  await expect.element(view.getByTestId('month-state')).toHaveTextContent(`state:${currentMonthValue()}`)
})

test('disables the trigger when disabled', async () => {
  await page.viewport(1280, 900)

  const disabledView = await render(MonthPickerHarness, {
    props: {
      disabled: true,
    },
  })
  expect((disabledView.getByRole('button', { name: /March 2026/i }).element() as HTMLButtonElement).disabled).toBe(true)
})

test('disables the trigger when readonly', async () => {
  await page.viewport(1280, 900)

  const readonlyView = await render(MonthPickerHarness, {
    props: {
      readonly: true,
    },
  })
  expect((readonlyView.getByRole('button', { name: /March 2026/i }).element() as HTMLButtonElement).disabled).toBe(true)
})
