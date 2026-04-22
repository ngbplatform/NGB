import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbRecycleBinFilter from '../../../../src/ngb/metadata/NgbRecycleBinFilter.vue'

const RecycleBinHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const mode = ref<'active' | 'deleted' | 'all'>('active')

    return () => h('div', [
      h(NgbRecycleBinFilter, {
        modelValue: mode.value,
        disabled: props.disabled,
        'onUpdate:modelValue': (value: 'active' | 'deleted' | 'all') => {
          mode.value = value
        },
      }),
      h('div', { 'data-testid': 'recycle-mode' }, mode.value),
    ])
  },
})

test('switches trash modes when a different segment is clicked', async () => {
  await page.viewport(1280, 900)

  const view = await render(RecycleBinHarness)

  await expect.element(view.getByTestId('recycle-mode')).toHaveTextContent('active')

  await view.getByRole('button', { name: 'Deleted' }).click()
  await expect.element(view.getByTestId('recycle-mode')).toHaveTextContent('deleted')

  await view.getByRole('button', { name: 'All' }).click()
  await expect.element(view.getByTestId('recycle-mode')).toHaveTextContent('all')

  await view.getByRole('button', { name: 'All' }).click()
  await expect.element(view.getByTestId('recycle-mode')).toHaveTextContent('all')
})

test('does not emit mode changes while disabled', async () => {
  await page.viewport(1280, 900)

  const view = await render(RecycleBinHarness, {
    props: {
      disabled: true,
    },
  })

  expect((view.getByRole('button', { name: 'Deleted' }).element() as HTMLButtonElement).disabled).toBe(true)
  await expect.element(view.getByTestId('recycle-mode')).toHaveTextContent('active')
})
