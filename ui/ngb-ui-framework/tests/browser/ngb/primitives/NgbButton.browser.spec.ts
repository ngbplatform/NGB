import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbButton from '../../../../src/ngb/primitives/NgbButton.vue'

const ButtonHarness = defineComponent({
  props: {
    variant: {
      type: String,
      default: 'primary',
    },
    size: {
      type: String,
      default: 'md',
    },
    disabled: {
      type: Boolean,
      default: false,
    },
    loading: {
      type: Boolean,
      default: false,
    },
    type: {
      type: String,
      default: 'button',
    },
  },
  setup(props) {
    const clicks = ref(0)

    return () => h('div', [
      h(NgbButton, {
        variant: props.variant as 'primary' | 'secondary' | 'ghost' | 'danger',
        size: props.size as 'sm' | 'md' | 'lg',
        disabled: props.disabled,
        loading: props.loading,
        type: props.type as 'button' | 'submit' | 'reset',
        onClick: () => {
          clicks.value += 1
        },
      }, () => 'Save changes'),
      h('div', { 'data-testid': 'button-clicks' }, `clicks:${clicks.value}`),
    ])
  },
})

test('renders the requested variant and size and emits clicks when enabled', async () => {
  await page.viewport(1280, 900)

  const view = await render(ButtonHarness, {
    props: {
      variant: 'danger',
      size: 'lg',
      type: 'submit',
    },
  })

  const button = view.getByRole('button', { name: 'Save changes' }).element() as HTMLButtonElement
  expect(button.type).toBe('submit')
  expect(button.className).toContain('h-10')
  expect(button.className).toContain('text-ngb-danger')

  await view.getByRole('button', { name: 'Save changes' }).click()
  await expect.element(view.getByTestId('button-clicks')).toHaveTextContent('clicks:1')
})

test('disables the control and shows the loading spinner state when requested', async () => {
  await page.viewport(1280, 900)

  const disabledView = await render(ButtonHarness, {
    props: {
      disabled: true,
      variant: 'ghost',
      size: 'sm',
    },
  })
  const disabledButton = disabledView.getByRole('button', { name: 'Save changes' }).element() as HTMLButtonElement
  expect(disabledButton.disabled).toBe(true)
  expect(disabledButton.className).toContain('h-8')
})

test('shows the loading spinner state when requested', async () => {
  await page.viewport(1280, 900)

  const loadingView = await render(ButtonHarness, {
    props: {
      loading: true,
    },
  })
  const loadingButton = loadingView.getByRole('button', { name: 'Save changes' }).element() as HTMLButtonElement
  expect(loadingButton.disabled).toBe(true)
  expect(loadingButton.querySelector('.ngb-spinner')).not.toBeNull()
})
