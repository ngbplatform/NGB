import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbEffectiveAccessPanel from '../../../../src/ngb/security/NgbEffectiveAccessPanel.vue'

const EffectiveAccessHarness = defineComponent({
  setup() {
    const refreshCount = ref(0)

    return () => h('div', [
      h(NgbEffectiveAccessPanel, {
        access: {
          userId: 'user-1',
          accessVersion: 7,
          groups: [
            {
              group: 'Accounting',
              resources: [
                {
                  resourceKind: 'Document',
                  resourceCode: 'general_journal_entry',
                  displayName: 'General Journal Entry',
                  actions: ['Read', 'Post'],
                },
              ],
            },
          ],
        },
        onRefresh: () => {
          refreshCount.value += 1
        },
      }),
      h('div', { 'data-testid': 'effective-refresh-count' }, String(refreshCount.value)),
    ])
  },
})

test('renders grouped effective access and emits refresh', async () => {
  await page.viewport(1280, 900)

  const view = await render(EffectiveAccessHarness)

  await expect.element(view.getByText('Version 7')).toBeVisible()
  await expect.element(view.getByText('Accounting')).toBeVisible()
  await expect.element(view.getByText('General Journal Entry')).toBeVisible()
  await expect.element(view.getByText('Document.general_journal_entry')).toBeVisible()
  await expect.element(view.getByText('Read', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Post', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Refresh' }).click()
  await expect.element(view.getByTestId('effective-refresh-count')).toHaveTextContent('1')
})

test('renders loading and error states before access data', async () => {
  await page.viewport(1280, 900)

  const loadingView = await render(NgbEffectiveAccessPanel, {
    props: {
      access: null,
      loading: true,
    },
  })
  await expect.element(loadingView.getByText('Version -')).toBeVisible()
  await expect.element(loadingView.getByText('Loading...')).toBeVisible()
  expect((loadingView.getByRole('button', { name: 'Refresh' }).element() as HTMLButtonElement).disabled).toBe(true)

  loadingView.unmount()
  const errorView = await render(NgbEffectiveAccessPanel, {
    props: {
      access: null,
      error: 'Access service unavailable',
    },
  })
  await expect.element(errorView.getByText('Access service unavailable')).toBeVisible()
})

test('renders both empty access variants and supports hiding refresh', async () => {
  await page.viewport(1280, 900)

  const emptyView = await render(NgbEffectiveAccessPanel, {
    props: {
      access: {
        userId: 'user-1',
        accessVersion: 8,
        groups: [],
      },
      showRefresh: false,
    },
  })
  await expect.element(emptyView.getByText('No effective permissions.')).toBeVisible()
  expect(document.querySelector('button')).toBeNull()

  emptyView.unmount()
  const absentView = await render(NgbEffectiveAccessPanel, {
    props: {
      access: null,
    },
  })
  await expect.element(absentView.getByText('No effective permissions.')).toBeVisible()
})
