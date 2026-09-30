import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

vi.mock('../../../../src/ngb/primitives/NgbButton.vue', () => ({
  default: defineComponent({
    inheritAttrs: false,
    setup(_props, { attrs, slots }) {
      return () => h('button', { ...attrs, disabled: false }, slots.default?.())
    },
  }),
}))

import NgbConfirmDialog from '../../../../src/ngb/components/NgbConfirmDialog.vue'

test('does not confirm when a synthetic click reaches the loading guard', async () => {
  await page.viewport(1280, 900)
  const count = ref(0)
  const Harness = defineComponent({
    setup() {
      return () => h('div', [
        h(NgbConfirmDialog, {
          open: true,
          title: 'Guarded action',
          message: 'Please wait.',
          confirmLoading: true,
          onConfirm: () => { count.value += 1 },
        }),
        h('div', { 'data-testid': 'confirm-count' }, String(count.value)),
      ])
    },
  })

  const view = await render(Harness)
  await view.getByRole('button', { name: 'Confirm' }).click()

  await expect.element(view.getByTestId('confirm-count')).toHaveTextContent('0')
})
