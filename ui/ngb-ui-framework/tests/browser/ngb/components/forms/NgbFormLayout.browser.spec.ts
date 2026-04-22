import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbFormLayout from '../../../../../src/ngb/components/forms/NgbFormLayout.vue'

const FormLayoutHarness = defineComponent({
  setup() {
    return () => h(
      NgbFormLayout,
      null,
      {
        default: () => [
          h('div', 'General'),
          h('div', 'Accounting'),
        ],
      },
    )
  },
})

test('renders slot content inside the form layout wrapper', async () => {
  const view = await render(FormLayoutHarness)

  await expect.element(view.getByText('General', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Accounting', { exact: true })).toBeVisible()

  const wrapper = document.querySelector('.space-y-4')
  expect(wrapper).not.toBeNull()
  expect(wrapper?.children.length).toBe(2)
})
