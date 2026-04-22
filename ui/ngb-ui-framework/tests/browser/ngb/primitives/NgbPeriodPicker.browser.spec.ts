import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbPeriodPicker from '../../../../src/ngb/primitives/NgbPeriodPicker.vue'
import type { PeriodValue } from '../../../../src/ngb/primitives/NgbPeriodPicker.vue'

const PeriodPickerHarness = defineComponent({
  setup() {
    const value = ref<PeriodValue>({
      kind: 'month',
      year: 2026,
      period: 3,
    })

    return () => h('div', [
      h(NgbPeriodPicker, {
        modelValue: value.value,
        label: 'Reporting period',
        hint: 'Choose a period',
        minYear: 2024,
        maxYear: 2026,
        'onUpdate:modelValue': (next: PeriodValue) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'period-state' }, `state:${value.value.kind}:${value.value.year}:${value.value.period}`),
    ])
  },
})

test('switches kind and coordinates both select controls into one period value', async () => {
  await page.viewport(1280, 900)

  const view = await render(PeriodPickerHarness)

  await expect.element(view.getByTestId('period-state')).toHaveTextContent('state:month:2026:3')

  await view.getByRole('tab', { name: 'Quarter' }).click()
  await expect.element(view.getByTestId('period-state')).toHaveTextContent('state:quarter:2026:1')

  await view.getByRole('button', { name: /2026/i }).click()
  await view.getByText('2025', { exact: true }).click()
  await expect.element(view.getByTestId('period-state')).toHaveTextContent('state:quarter:2025:1')

  await view.getByRole('button', { name: /Q1/i }).click()
  await view.getByText('Q3', { exact: true }).click()
  await expect.element(view.getByTestId('period-state')).toHaveTextContent('state:quarter:2025:3')

  await view.getByRole('tab', { name: 'Year' }).click()
  await expect.element(view.getByTestId('period-state')).toHaveTextContent('state:year:2025:1')
})
