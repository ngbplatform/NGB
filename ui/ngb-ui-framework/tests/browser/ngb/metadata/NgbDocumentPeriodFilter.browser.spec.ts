import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import { StubMonthPicker } from './stubs'

vi.mock('../../../../src/ngb/primitives/NgbMonthPicker.vue', () => ({
  default: StubMonthPicker,
}))

import NgbDocumentPeriodFilter from '../../../../src/ngb/metadata/NgbDocumentPeriodFilter.vue'

function text(testId: string): string {
  const element = document.querySelector(`[data-testid="${testId}"]`)
  return element?.textContent ?? ''
}

function pickerButtons(testId: string): HTMLButtonElement[] {
  return Array.from(document.querySelectorAll(`[data-testid="${testId}"] button`)) as HTMLButtonElement[]
}

const PeriodFilterHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const fromMonth = ref(' 2026-03 ')
    const toMonth = ref('invalid')

    return () => h('div', [
      h(NgbDocumentPeriodFilter, {
        fromMonth: fromMonth.value,
        toMonth: toMonth.value,
        disabled: props.disabled,
        'onUpdate:fromMonth': (value: string) => {
          fromMonth.value = value
        },
        'onUpdate:toMonth': (value: string) => {
          toMonth.value = value
        },
      }),
      h('div', { 'data-testid': 'period-from' }, fromMonth.value),
      h('div', { 'data-testid': 'period-to' }, toMonth.value),
    ])
  },
})

test('normalizes incoming month values and forwards picker updates', async () => {
  await page.viewport(1280, 900)

  const view = await render(PeriodFilterHarness)

  expect(text('stub-month-picker:Start month')).toContain('month-value:2026-03')
  expect(text('stub-month-picker:End month')).toContain('month-value:none')

  pickerButtons('stub-month-picker:Start month')[0]?.click()
  await expect.element(view.getByTestId('period-from')).toHaveTextContent('2026-04')

  pickerButtons('stub-month-picker:End month')[1]?.click()
  await expect.element(view.getByTestId('period-to')).toHaveTextContent('')
})

test('passes the disabled state through to both month pickers', async () => {
  await page.viewport(1280, 900)

  const view = await render(PeriodFilterHarness, {
    props: {
      disabled: true,
    },
  })

  expect(text('stub-month-picker:Start month')).toContain('month-disabled:true')
  expect(text('stub-month-picker:End month')).toContain('month-disabled:true')
})
