import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbHeaderActionCluster from '../../../../src/ngb/components/NgbHeaderActionCluster.vue'

async function waitForDomUpdate() {
  await new Promise((resolve) => window.setTimeout(resolve, 0))
}

const HeaderActionClusterHarness = defineComponent({
  props: {
    closeDisabled: {
      type: Boolean,
      default: false,
    },
    withMoreActions: {
      type: Boolean,
      default: true,
    },
  },
  setup(props) {
    const actionLog = ref<string[]>([])
    const closeCount = ref(0)

    return () => h('div', [
      h(NgbHeaderActionCluster, {
        primaryActions: [
          { key: 'save', title: 'Save draft', icon: 'save' },
          { key: 'print', title: 'Print', icon: 'printer', disabled: true },
        ],
        moreGroups: props.withMoreActions
          ? [
              {
                key: 'document',
                label: 'Document',
                items: [
                  { key: 'audit', title: 'Open audit log', icon: 'history' },
                  { key: 'share', title: 'Share', icon: 'share', disabled: true },
                ],
              },
              {
                key: 'empty',
                items: [],
              },
            ]
          : [],
        closeDisabled: props.closeDisabled,
        onAction: (key: string) => {
          actionLog.value = [...actionLog.value, key]
        },
        onClose: () => {
          closeCount.value += 1
        },
      }),
      h('div', { 'data-testid': 'cluster-state' }, `actions:${actionLog.value.join(',') || 'none'};close:${closeCount.value}`),
    ])
  },
})

test('emits primary, grouped menu, and close actions while respecting disabled items', async () => {
  await page.viewport(1280, 900)

  const view = await render(HeaderActionClusterHarness)

  await view.getByRole('button', { name: 'Save draft' }).click()
  await expect.element(view.getByTestId('cluster-state')).toHaveTextContent('actions:save;close:0')

  const disabledPrimary = view.getByRole('button', { name: 'Print' }).element() as HTMLButtonElement
  expect(disabledPrimary.disabled).toBe(true)

  await view.getByRole('button', { name: 'More actions' }).click()
  await expect.element(view.getByText('Document', { exact: true })).toBeVisible()

  await expect.element(view.getByText('Share', { exact: true })).toBeVisible()
  const disabledMoreAction = view.getByText('Share', { exact: true }).element()?.closest('button') as HTMLButtonElement
  expect(disabledMoreAction.disabled).toBe(true)

  await expect.element(view.getByText('Open audit log', { exact: true })).toBeVisible()
  ;(view.getByText('Open audit log', { exact: true }).element()?.closest('button') as HTMLButtonElement).click()
  await expect.element(view.getByTestId('cluster-state')).toHaveTextContent('actions:save,audit;close:0')

  await view.getByRole('button', { name: 'Close' }).click()
  await expect.element(view.getByTestId('cluster-state')).toHaveTextContent('actions:save,audit;close:1')
})

test('omits the more-actions menu when there are no visible groups and disables close when requested', async () => {
  await page.viewport(1280, 900)

  const view = await render(HeaderActionClusterHarness, {
    props: {
      closeDisabled: true,
      withMoreActions: false,
    },
  })

  expect(document.body.textContent?.includes('More actions')).toBe(false)

  const closeButton = view.getByRole('button', { name: 'Close' }).element() as HTMLButtonElement
  expect(closeButton.disabled).toBe(true)

  closeButton.click()
  await waitForDomUpdate()
  await expect.element(view.getByTestId('cluster-state')).toHaveTextContent('actions:none;close:0')
})
