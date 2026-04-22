import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbConfirmDialog from '../../../../src/ngb/components/NgbConfirmDialog.vue'

const ConfirmDialogHarness = defineComponent({
  props: {
    danger: {
      type: Boolean,
      default: false,
    },
    confirmLoading: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const open = ref(true)
    const confirmCount = ref(0)

    return () => h('div', [
      h(NgbConfirmDialog, {
        open: open.value,
        title: 'Delete record?',
        message: 'This change cannot be undone.',
        danger: props.danger,
        confirmLoading: props.confirmLoading,
        'onUpdate:open': (value: boolean) => {
          open.value = value
        },
        onConfirm: () => {
          confirmCount.value += 1
        },
      }),
      h('div', { 'data-testid': 'confirm-state' }, `open:${open.value};confirm:${confirmCount.value}`),
    ])
  },
})

test('uses the danger defaults and closes when cancel is pressed', async () => {
  await page.viewport(1280, 900)

  const view = await render(ConfirmDialogHarness, {
    props: {
      danger: true,
    },
  })

  await expect.element(view.getByText('Delete record?', { exact: true })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Discard' })).toBeVisible()

  await view.getByRole('button', { name: 'Cancel' }).click()
  await expect.element(view.getByTestId('confirm-state')).toHaveTextContent('open:false;confirm:0')
})

test('emits confirm and keeps the action disabled while loading', async () => {
  await page.viewport(1280, 900)

  const view = await render(ConfirmDialogHarness)
  await view.getByRole('button', { name: 'Confirm' }).click()
  await expect.element(view.getByTestId('confirm-state')).toHaveTextContent('open:true;confirm:1')
})

test('keeps the confirm action disabled while loading', async () => {
  await page.viewport(1280, 900)

  const loadingView = await render(ConfirmDialogHarness, {
    props: {
      confirmLoading: true,
    },
  })
  expect((loadingView.getByRole('button', { name: 'Confirm' }).element() as HTMLButtonElement).disabled).toBe(true)
})
