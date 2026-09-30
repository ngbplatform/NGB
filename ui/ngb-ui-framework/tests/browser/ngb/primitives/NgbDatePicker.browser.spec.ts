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

const DatePickerBoundaryHarness = defineComponent({
  setup() {
    const value = ref<string | null>(null)

    return () => h('div', [
      h(NgbDatePicker, {
        modelValue: value.value,
        grouped: true,
        'onUpdate:modelValue': (next: string | null) => {
          value.value = next
        },
      }),
      h('button', { type: 'button', onClick: () => { value.value = '2027-01-10' } }, 'Set January'),
      h('button', { type: 'button', onClick: () => { value.value = 'not-a-date' } }, 'Set invalid'),
    ])
  },
})

function localizedMonth(year: number, month: number): string {
  return new Date(year, month, 1).toLocaleString(undefined, { month: 'long', year: 'numeric' })
}

function displayDate(value: Date): string {
  return `${String(value.getMonth() + 1).padStart(2, '0')}/${String(value.getDate()).padStart(2, '0')}/${value.getFullYear()}`
}

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

  await view.getByRole('button', { name: displayDate(new Date()) }).click()
  expect(dayButton(String(new Date().getDate())).className).toContain('ring-1')
})

test('uses the default placeholder, follows external values, and navigates across a year boundary', async () => {
  await page.viewport(1280, 900)

  const view = await render(DatePickerBoundaryHarness)
  await view.getByRole('button', { name: 'mm/dd/yyyy' }).click()

  await view.getByText('Set January').click()
  await view.getByRole('button', { name: '01/10/2027' }).click()
  await expect.element(view.getByText(localizedMonth(2027, 0))).toBeVisible()

  await view.getByRole('button', { name: 'Previous year' }).click()
  await expect.element(view.getByText(localizedMonth(2026, 11))).toBeVisible()

  await view.getByRole('button', { name: 'Next year' }).click()
  await expect.element(view.getByText(localizedMonth(2027, 0))).toBeVisible()

  await view.getByText('Set invalid').click()
  await expect.element(view.getByRole('button', { name: 'not-a-date' })).toBeVisible()
  await view.getByRole('button', { name: 'not-a-date' }).click()
  await expect.element(view.getByText(localizedMonth(2027, 0))).toBeVisible()
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
