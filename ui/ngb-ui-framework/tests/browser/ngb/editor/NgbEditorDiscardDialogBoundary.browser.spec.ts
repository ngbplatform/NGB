import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

vi.mock('../../../../src/ngb/components/NgbConfirmDialog.vue', () => ({
  default: defineComponent({
    emits: ['update:open', 'confirm'],
    setup(_props, { emit }) {
      return () => h('button', {
        type: 'button',
        onClick: () => emit('update:open', true),
      }, 'Synthetic reopen')
    },
  }),
}))

import NgbEditorDiscardDialog from '../../../../src/ngb/editor/NgbEditorDiscardDialog.vue'

test('forwards a true open update without treating it as cancellation', async () => {
  await page.viewport(1280, 900)
  const state = ref('unset')
  const cancelCount = ref(0)
  const Harness = defineComponent({
    setup() {
      return () => h('div', [
        h(NgbEditorDiscardDialog, {
          open: false,
          'onUpdate:open': (value: boolean) => { state.value = String(value) },
          onCancel: () => { cancelCount.value += 1 },
        }),
        h('div', { 'data-testid': 'boundary-state' }, `${state.value}:${cancelCount.value}`),
      ])
    },
  })

  const view = await render(Harness)
  await view.getByRole('button', { name: 'Synthetic reopen' }).click()

  await expect.element(view.getByTestId('boundary-state')).toHaveTextContent('true:0')
})
