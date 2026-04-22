import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import { StubDatePicker, StubIcon } from './stubs'

vi.mock('../../../../src/ngb/primitives/NgbDatePicker.vue', () => ({
  default: StubDatePicker,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

import NgbDashboardAsOfToolbar from '../../../../src/ngb/site/NgbDashboardAsOfToolbar.vue'

const ToolbarHarness = defineComponent({
  props: {
    loading: {
      type: Boolean,
      default: false,
    },
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const asOf = ref('2026-04-08')
    const refreshCount = ref(0)

    return () => h('div', [
      h(NgbDashboardAsOfToolbar, {
        modelValue: asOf.value,
        loading: props.loading,
        disabled: props.disabled,
        'onUpdate:modelValue': (value: string | null) => {
          asOf.value = value ?? ''
        },
        onRefresh: () => {
          refreshCount.value += 1
        },
      }),
      h('div', `as-of:${asOf.value || 'none'}`),
      h('div', `refresh-count:${refreshCount.value}`),
    ])
  },
})

test('updates the as-of value and emits refresh while enabled', async () => {
  await page.viewport(1024, 800)

  const view = await render(ToolbarHarness)
  const dateInput = view.getByTestId('stub-date-picker').element() as HTMLInputElement

  await expect.element(view.getByTestId('stub-date-picker')).toBeVisible()
  expect(dateInput.disabled).toBe(false)

  dateInput.value = '2026-04-30'
  dateInput.dispatchEvent(new Event('input', { bubbles: true }))
  dateInput.dispatchEvent(new Event('change', { bubbles: true }))

  await expect.element(view.getByText('as-of:2026-04-30')).toBeVisible()

  await view.getByRole('button', { name: 'Refresh' }).click()
  await expect.element(view.getByText('refresh-count:1')).toBeVisible()
  await expect.element(view.getByTestId('icon-refresh')).toBeVisible()
})

test('disables both the picker and refresh action while loading', async () => {
  await page.viewport(1024, 800)

  const loadingView = await render(ToolbarHarness, {
    props: {
      loading: true,
    },
  })

  expect((loadingView.getByTestId('stub-date-picker').element() as HTMLInputElement).disabled).toBe(true)
  expect((loadingView.getByRole('button', { name: 'Refresh' }).element() as HTMLButtonElement).disabled).toBe(true)
})

test('disables both the picker and refresh action when explicitly disabled', async () => {
  await page.viewport(1024, 800)

  const disabledView = await render(ToolbarHarness, {
    props: {
      disabled: true,
    },
  })

  expect((disabledView.getByTestId('stub-date-picker').element() as HTMLInputElement).disabled).toBe(true)
  expect((disabledView.getByRole('button', { name: 'Refresh' }).element() as HTMLButtonElement).disabled).toBe(true)
})
