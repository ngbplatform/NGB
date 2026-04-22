import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbSwitch from '../../../../src/ngb/primitives/NgbSwitch.vue'

const SwitchHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const value = ref(false)

    return () => h('div', [
      h(NgbSwitch, {
        modelValue: value.value,
        label: 'Auto post',
        disabled: props.disabled,
        'onUpdate:modelValue': (next: boolean) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'switch-state' }, `checked:${value.value}`),
    ])
  },
})

test('toggles the switch and updates the aria-checked state', async () => {
  await page.viewport(1280, 900)

  const view = await render(SwitchHarness)
  const switchButton = view.getByRole('switch', { name: 'Auto post' }).element() as HTMLButtonElement

  expect(switchButton.getAttribute('aria-checked')).toBe('false')
  await view.getByRole('switch', { name: 'Auto post' }).click()

  await expect.element(view.getByTestId('switch-state')).toHaveTextContent('checked:true')
  expect((view.getByRole('switch', { name: 'Auto post' }).element() as HTMLButtonElement).getAttribute('aria-checked')).toBe('true')
})

test('prevents toggling when the switch is disabled', async () => {
  await page.viewport(1280, 900)

  const view = await render(SwitchHarness, {
    props: {
      disabled: true,
    },
  })

  const switchButton = view.getByRole('switch', { name: 'Auto post' }).element() as HTMLButtonElement
  expect(switchButton.disabled).toBe(true)
  await expect.element(view.getByTestId('switch-state')).toHaveTextContent('checked:false')
})
