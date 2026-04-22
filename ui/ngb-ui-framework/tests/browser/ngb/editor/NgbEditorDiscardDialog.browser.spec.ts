import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbEditorDiscardDialog from '../../../../src/ngb/editor/NgbEditorDiscardDialog.vue'

const DiscardHarness = defineComponent({
  setup() {
    const open = ref(true)
    const confirmCount = ref(0)
    const cancelCount = ref(0)

    return () => h('div', [
      h(NgbEditorDiscardDialog, {
        open: open.value,
        'onUpdate:open': (value: boolean) => {
          open.value = value
        },
        onCancel: () => {
          cancelCount.value += 1
        },
        onConfirm: () => {
          confirmCount.value += 1
        },
      }),
      h('div', { 'data-testid': 'discard-state' }, `open:${open.value};confirm:${confirmCount.value};cancel:${cancelCount.value}`),
    ])
  },
})

test('uses the editor discard defaults and emits cancel when the user keeps editing', async () => {
  await page.viewport(1280, 900)

  const view = await render(DiscardHarness)

  await expect.element(view.getByText('Discard changes?', { exact: true })).toBeVisible()
  await expect.element(view.getByText('You have unsaved changes. If you close this panel now, they won’t be saved.', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Keep editing' }).click()
  await expect.element(view.getByTestId('discard-state')).toHaveTextContent('open:false;confirm:0;cancel:1')
})

test('emits confirm when the user discards changes', async () => {
  await page.viewport(1280, 900)

  const view = await render(DiscardHarness)
  await view.getByRole('button', { name: 'Discard' }).click()

  await expect.element(view.getByTestId('discard-state')).toHaveTextContent('open:true;confirm:1;cancel:0')
})
