import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbCheckbox from '../../../../src/ngb/primitives/NgbCheckbox.vue'

const CheckboxHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const value = ref(false)

    return () => h('div', [
      h(NgbCheckbox, {
        modelValue: value.value,
        label: 'Include archived',
        disabled: props.disabled,
        'onUpdate:modelValue': (next: boolean) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'checkbox-state' }, `checked:${value.value}`),
    ])
  },
})

test('toggles the checkbox value and renders the label text', async () => {
  await page.viewport(1280, 900)

  const view = await render(CheckboxHarness)

  await expect.element(view.getByText('Include archived', { exact: true })).toBeVisible()

  const input = document.querySelector('input[type="checkbox"]') as HTMLInputElement
  input.click()

  await expect.element(view.getByTestId('checkbox-state')).toHaveTextContent('checked:true')
  expect(document.body.textContent?.includes('✓')).toBe(true)
})

test('keeps the checkbox disabled when requested', async () => {
  await page.viewport(1280, 900)

  const view = await render(CheckboxHarness, {
    props: {
      disabled: true,
    },
  })

  expect((document.querySelector('input[type="checkbox"]') as HTMLInputElement).disabled).toBe(true)
  await expect.element(view.getByTestId('checkbox-state')).toHaveTextContent('checked:false')
})
