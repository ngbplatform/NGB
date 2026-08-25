import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref, type PropType } from 'vue'

type SelectOption = { value: unknown; label: string }

vi.mock('../../../../src/ngb/primitives/NgbSelect.vue', () => ({
  default: defineComponent({
    props: {
      modelValue: {
        type: [String, Number, Boolean, Object] as PropType<unknown>,
        default: null,
      },
      options: {
        type: Array as PropType<SelectOption[]>,
        default: () => [],
      },
    },
    emits: ['update:modelValue'],
    setup(props, { emit }) {
      return () => h('div', [
        h('div', { 'data-testid': 'boundary-select-options' }, `options:${props.options.length}`),
        h('button', {
          type: 'button',
          'data-testid': 'emit-invalid-select',
          onClick: () => emit('update:modelValue', 'not-a-number'),
        }, `Emit invalid ${String(props.modelValue)}`),
      ])
    },
  }),
}))

vi.mock('../../../../src/ngb/primitives/NgbTabs.vue', () => ({
  default: defineComponent({
    setup() {
      return () => h('div', 'Period kind tabs')
    },
  }),
}))

import NgbPeriodPicker, { type PeriodValue } from '../../../../src/ngb/primitives/NgbPeriodPicker.vue'

const BoundaryHarness = defineComponent({
  setup() {
    const value = ref<PeriodValue>({ kind: 'month', year: 2026, period: 3 })

    return () => h('div', [
      h(NgbPeriodPicker, {
        modelValue: value.value,
        'onUpdate:modelValue': (next: PeriodValue) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'boundary-period-state' }, `state:${value.value.kind}:${value.value.year}:${value.value.period}`),
    ])
  },
})

test('uses default year bounds and ignores non-numeric select events without optional copy', async () => {
  await page.viewport(1280, 900)

  const view = await render(BoundaryHarness)
  const optionCounts = Array.from(document.querySelectorAll('[data-testid="boundary-select-options"]'))
    .map((element) => element.textContent)

  expect(optionCounts).toEqual(['options:7', 'options:12'])
  expect(view.container.textContent).not.toContain('Reporting period')
  expect(view.container.textContent).not.toContain('Choose a period')

  for (const button of document.querySelectorAll<HTMLButtonElement>('[data-testid="emit-invalid-select"]')) {
    button.click()
  }

  await expect.element(view.getByTestId('boundary-period-state')).toHaveTextContent('state:month:2026:3')
})
