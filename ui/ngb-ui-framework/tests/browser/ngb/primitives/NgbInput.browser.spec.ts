import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbInput from '../../../../src/ngb/primitives/NgbInput.vue'

const InputHarness = defineComponent({
  props: {
    variant: {
      type: String,
      default: 'default',
    },
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
    const value = ref('Initial value')

    return () => h('div', [
      h(NgbInput, {
        modelValue: value.value,
        label: 'Document number',
        hint: 'Displayed on the register grid',
        placeholder: 'Enter number',
        title: 'Input tooltip',
        variant: props.variant as 'default' | 'grid',
        disabled: props.disabled,
        readonly: props.readonly,
        'onUpdate:modelValue': (next: string) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'input-state' }, value.value),
    ])
  },
})

test('renders label, hint, and updates the model value through input events', async () => {
  await page.viewport(1280, 900)

  const view = await render(InputHarness)

  await expect.element(view.getByText('Document number', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Displayed on the register grid', { exact: true })).toBeVisible()

  const input = document.querySelector('input[title="Input tooltip"]') as HTMLInputElement
  input.value = 'INV-2026-001'
  input.dispatchEvent(new Event('input', { bubbles: true }))

  await expect.element(view.getByTestId('input-state')).toHaveTextContent('INV-2026-001')
})

test('supports the grid variant and respects disabled and readonly states', async () => {
  await page.viewport(1280, 900)

  const gridView = await render(InputHarness, {
    props: {
      variant: 'grid',
    },
  })
  expect((gridView.getByRole('textbox').element() as HTMLInputElement).className).toContain('rounded-none')
})

test('respects the disabled state', async () => {
  await page.viewport(1280, 900)

  const disabledView = await render(InputHarness, {
    props: {
      disabled: true,
    },
  })
  expect((disabledView.getByRole('textbox').element() as HTMLInputElement).disabled).toBe(true)
})

test('respects the readonly state', async () => {
  await page.viewport(1280, 900)

  const readonlyView = await render(InputHarness, {
    props: {
      readonly: true,
    },
  })
  expect((readonlyView.getByRole('textbox').element() as HTMLInputElement).readOnly).toBe(true)
})
