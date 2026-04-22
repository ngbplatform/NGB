import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

vi.mock('../../../../src/ngb/primitives/NgbDatePicker.vue', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')

  return {
    default: defineComponent({
      props: {
        modelValue: {
          type: String,
          default: null,
        },
        placeholder: {
          type: String,
          default: '',
        },
        grouped: {
          type: Boolean,
          default: false,
        },
        disabled: {
          type: Boolean,
          default: false,
        },
      },
      emits: ['update:modelValue'],
      setup(props, { emit }) {
        return () => h('div', { 'data-testid': `stub-range-picker:${props.placeholder}` }, [
          h('div', `value:${props.modelValue ?? 'none'}`),
          h('div', `grouped:${String(props.grouped)}`),
          h('div', `disabled:${String(props.disabled)}`),
          h('button', {
            type: 'button',
            disabled: props.disabled,
            onClick: () => emit('update:modelValue', props.placeholder?.includes('Start') ? '2026-04-01' : '2026-04-30'),
          }, `set:${props.placeholder}`),
          h('button', {
            type: 'button',
            disabled: props.disabled,
            onClick: () => emit('update:modelValue', null),
          }, `clear:${props.placeholder}`),
        ])
      },
    }),
  }
})

import NgbReportDateRangeFilter from '../../../../src/ngb/reporting/NgbReportDateRangeFilter.vue'

const DateRangeHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const fromDate = ref('2026-04-08')
    const toDate = ref('2026-04-21')

    return () => h('div', [
      h(NgbReportDateRangeFilter, {
        fromDate: fromDate.value,
        toDate: toDate.value,
        disabled: props.disabled,
        title: 'Posting date range',
        'onUpdate:fromDate': (value: string) => {
          fromDate.value = value
        },
        'onUpdate:toDate': (value: string) => {
          toDate.value = value
        },
      }),
      h('div', { 'data-testid': 'from-state' }, fromDate.value),
      h('div', { 'data-testid': 'to-state' }, toDate.value),
    ])
  },
})

test('normalizes the input values and forwards date updates from both pickers', async () => {
  await page.viewport(1280, 900)

  const view = await render(DateRangeHarness)

  await expect.element(view.getByTestId('stub-range-picker:Start date')).toHaveTextContent('value:2026-04-08')
  await expect.element(view.getByTestId('stub-range-picker:End date')).toHaveTextContent('value:2026-04-21')
  await expect.element(view.getByTestId('stub-range-picker:Start date')).toHaveTextContent('grouped:true')
  await expect.element(view.getByTestId('stub-range-picker:End date')).toHaveTextContent('grouped:true')

  await view.getByRole('button', { name: 'set:Start date' }).click()
  await view.getByRole('button', { name: 'clear:End date' }).click()

  await expect.element(view.getByTestId('from-state')).toHaveTextContent('2026-04-01')
  await expect.element(view.getByTestId('to-state')).toHaveTextContent('')
})

test('passes the disabled state through to both date pickers', async () => {
  await page.viewport(1280, 900)

  const view = await render(DateRangeHarness, {
    props: {
      disabled: true,
    },
  })

  await expect.element(view.getByTestId('stub-range-picker:Start date')).toHaveTextContent('disabled:true')
  await expect.element(view.getByTestId('stub-range-picker:End date')).toHaveTextContent('disabled:true')
})
