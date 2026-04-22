import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbRadioGroup from '../../../../src/ngb/primitives/NgbRadioGroup.vue'

const options = [
  { value: 'draft', label: 'Draft' },
  { value: 'posted', label: 'Posted' },
  { value: 'deleted', label: 'Deleted' },
]

const RadioGroupHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const value = ref('draft')

    return () => h('div', [
      h(NgbRadioGroup, {
        modelValue: value.value,
        options,
        name: 'status',
        label: 'Status',
        hint: 'Choose the current status.',
        disabled: props.disabled,
        'onUpdate:modelValue': (next: string) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'radio-state' }, `value:${value.value}`),
    ])
  },
})

test('renders label and hint text and updates the selected value when a different option is chosen', async () => {
  await page.viewport(1280, 900)

  const view = await render(RadioGroupHarness)

  await expect.element(view.getByText('Status', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Choose the current status.', { exact: true })).toBeVisible()

  await view.getByText('Posted', { exact: true }).click()
  await expect.element(view.getByTestId('radio-state')).toHaveTextContent('value:posted')
})

test('keeps all radio inputs disabled and does not emit value updates when disabled', async () => {
  await page.viewport(1280, 900)

  const view = await render(RadioGroupHarness, {
    props: {
      disabled: true,
    },
  })

  const inputs = Array.from(document.querySelectorAll('input[type="radio"]')) as HTMLInputElement[]
  expect(inputs.every((input) => input.disabled)).toBe(true)

  ;(view.getByText('Deleted', { exact: true }).element() as HTMLElement).click()
  await expect.element(view.getByTestId('radio-state')).toHaveTextContent('value:draft')
})
