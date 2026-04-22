import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbDialog from '../../../../src/ngb/components/NgbDialog.vue'

const DialogHarness = defineComponent({
  props: {
    customFooter: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const open = ref(true)
    const confirmCount = ref(0)
    const customCount = ref(0)

    return () => h('div', [
      h(NgbDialog, {
        open: open.value,
        title: 'Rename variant',
        subtitle: 'Update the saved report name.',
        'onUpdate:open': (value: boolean) => {
          open.value = value
        },
        onConfirm: () => {
          confirmCount.value += 1
        },
      }, {
        default: () => h('div', { 'data-testid': 'dialog-body' }, 'Dialog content'),
        footer: props.customFooter
          ? () => h('button', {
            type: 'button',
            onClick: () => {
              customCount.value += 1
            },
          }, 'Custom footer action')
          : undefined,
      }),
      h('div', { 'data-testid': 'dialog-state' }, `open:${open.value};confirm:${confirmCount.value};custom:${customCount.value}`),
    ])
  },
})

test('renders the default footer, emits confirm, and closes from cancel', async () => {
  await page.viewport(1280, 900)

  const view = await render(DialogHarness)

  await expect.element(view.getByTestId('dialog-body')).toBeVisible()
  await expect.element(view.getByText('Update the saved report name.', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Confirm' }).click()
  await expect.element(view.getByTestId('dialog-state')).toHaveTextContent('open:true;confirm:1;custom:0')

  await view.getByRole('button', { name: 'Cancel' }).click()
  await expect.element(view.getByTestId('dialog-state')).toHaveTextContent('open:false;confirm:1;custom:0')
})

test('allows the footer slot to replace the default actions', async () => {
  await page.viewport(1280, 900)

  const view = await render(DialogHarness, {
    props: {
      customFooter: true,
    },
  })

  await expect.element(view.getByRole('button', { name: 'Custom footer action' })).toBeVisible()
  expect(document.body.textContent?.includes('Confirm')).toBe(false)
  expect(document.body.textContent?.includes('Cancel')).toBe(false)

  await view.getByRole('button', { name: 'Custom footer action' }).click()
  await expect.element(view.getByTestId('dialog-state')).toHaveTextContent('open:true;confirm:0;custom:1')
})
