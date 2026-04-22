import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import {
  StubEditorDiscardDialog,
  StubEntityEditorDrawerActions,
  StubRecycleBinFilter,
  StubRegisterPageLayout,
} from '../metadata/stubs'
import {
  StubIcon,
  StubRegisterGrid,
} from './stubs'

const chartMocks = vi.hoisted(() => ({
  getMetadata: vi.fn(),
  getPage: vi.fn(),
  navigateBack: vi.fn(),
  editorHandle: {
    save: vi.fn().mockResolvedValue(undefined),
    copyShareLink: vi.fn().mockResolvedValue(true),
    openAuditLog: vi.fn(),
    closeAuditLog: vi.fn(),
    toggleMarkForDeletion: vi.fn(),
  },
}))

vi.mock('../../../../src/ngb/accounting/api', () => ({
  getChartOfAccountsMetadata: chartMocks.getMetadata,
  getChartOfAccountsPage: chartMocks.getPage,
}))

vi.mock('../../../../src/ngb/router/backNavigation', () => ({
  navigateBack: chartMocks.navigateBack,
}))

vi.mock('../../../../src/ngb/components/register/NgbRegisterGrid.vue', () => ({
  default: StubRegisterGrid,
}))

vi.mock('../../../../src/ngb/metadata/NgbRegisterPageLayout.vue', () => ({
  default: StubRegisterPageLayout,
}))

vi.mock('../../../../src/ngb/metadata/NgbRecycleBinFilter.vue', () => ({
  default: StubRecycleBinFilter,
}))

vi.mock('../../../../src/ngb/editor/NgbEntityEditorDrawerActions.vue', () => ({
  default: StubEntityEditorDrawerActions,
}))

vi.mock('../../../../src/ngb/editor/NgbEditorDiscardDialog.vue', () => ({
  default: StubEditorDiscardDialog,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/accounting/NgbChartOfAccountEditor.vue', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')

  return {
    default: defineComponent({
      props: {
        id: {
          type: String,
          default: null,
        },
        metadata: {
          type: Object,
          default: null,
        },
        routeBasePath: {
          type: String,
          default: '',
        },
      },
      emits: ['created', 'saved', 'changed', 'close'],
      setup(props, { emit, expose }) {
        expose(chartMocks.editorHandle)

        return () => h('div', { 'data-testid': 'chart-editor' }, [
          h('div', `editor-id:${props.id ?? 'new'}`),
          h('div', `editor-route-base:${props.routeBasePath}`),
          h('div', `editor-metadata-options:${props.metadata?.cashFlowRoleOptions?.length ?? 0}`),
          h('button', {
            type: 'button',
            onClick: () => emit('created', 'coa-created'),
          }, 'Editor emit created'),
          h('button', {
            type: 'button',
            onClick: () => emit('saved'),
          }, 'Editor emit saved'),
          h('button', {
            type: 'button',
            onClick: () => emit('changed', 'markForDeletion'),
          }, 'Editor emit changed'),
          h('button', {
            type: 'button',
            onClick: () => emit('close'),
          }, 'Editor emit close'),
        ])
      },
    }),
  }
})

import NgbChartOfAccountsPage from '../../../../src/ngb/accounting/NgbChartOfAccountsPage.vue'

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

async function flushUi() {
  await new Promise((resolve) => window.setTimeout(resolve, 40))
}

async function renderPage(initialUrl: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/admin/chart-of-accounts',
        component: NgbChartOfAccountsPage,
      },
      {
        path: '/home',
        component: {
          template: '<div>Home</div>',
        },
      },
    ],
  })

  await router.push(initialUrl)
  await router.isReady()

  const view = await render(NgbChartOfAccountsPage, {
    props: {
      backTarget: '/home',
    },
    global: {
      plugins: [router],
    },
  })

  await flushUi()

  return {
    router,
    view,
  }
}

beforeEach(() => {
  vi.clearAllMocks()

  chartMocks.getMetadata.mockResolvedValue({
    accountTypeOptions: [],
    cashFlowRoleOptions: [
      { value: 'operating', label: 'Operating', supportsLineCode: false, requiresLineCode: false },
    ],
    cashFlowLineOptions: [],
  })

  chartMocks.getPage.mockResolvedValue({
    offset: 0,
    limit: 50,
    total: 1,
    items: [
      {
        accountId: 'coa-1',
        code: '1010',
        name: 'Cash',
        accountType: 'Asset',
        cashFlowRole: 'operating',
        isActive: true,
        isDeleted: false,
        isMarkedForDeletion: false,
      },
    ],
  })
})

test('migrates legacy accountId query, loads metadata once, and wires drawer actions to the chart editor', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderPage('/admin/chart-of-accounts?accountId=legacy-1')

  expect(chartMocks.getMetadata).toHaveBeenCalledTimes(1)
  expect(chartMocks.getPage).toHaveBeenCalledWith({
    offset: 0,
    limit: 50,
    search: null,
    onlyActive: null,
    includeDeleted: false,
    onlyDeleted: null,
  })

  expect(router.currentRoute.value.query.id).toBe('legacy-1')
  expect(router.currentRoute.value.query.accountId).toBeUndefined()
  expect(router.currentRoute.value.query.panel).toBe('edit')

  await expect.element(view.getByText('title:Chart of Accounts')).toBeVisible()
  expect(document.body.textContent).toContain('storage:ngb:accounting:chart-of-accounts:/admin/chart-of-accounts')
  await expect.element(view.getByText(/code=1010/)).toBeVisible()
  await expect.element(view.getByText(/cashFlowRole=Operating/)).toBeVisible()
  await expect.element(view.getByText(/isActive=Yes/)).toBeVisible()
  await expect.element(view.getByText('editor-id:legacy-1')).toBeVisible()
  await expect.element(view.getByText('editor-route-base:/admin/chart-of-accounts')).toBeVisible()

  await view.getByRole('button', { name: 'Drawer action:share' }).click()
  await view.getByRole('button', { name: 'Drawer action:audit' }).click()
  await view.getByRole('button', { name: 'Drawer action:mark' }).click()
  await view.getByRole('button', { name: 'Drawer action:save' }).click()

  expect(chartMocks.editorHandle.copyShareLink).toHaveBeenCalledTimes(1)
  expect(chartMocks.editorHandle.openAuditLog).toHaveBeenCalledTimes(1)
  expect(chartMocks.editorHandle.toggleMarkForDeletion).toHaveBeenCalledTimes(1)
  expect(chartMocks.editorHandle.save).toHaveBeenCalledTimes(1)

  await view.getByRole('button', { name: 'Layout close drawer' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()
})

test('updates paging and trash filters, opens create and edit drawers, and refreshes/backs correctly', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderPage('/admin/chart-of-accounts')

  await expect.element(view.getByText('group-by:accountType')).toBeVisible()

  await view.getByTestId('stub-recycle-bin-filter').click()
  await flushUi()

  expect(router.currentRoute.value.query.trash).toBe('deleted')
  expect(chartMocks.getMetadata).toHaveBeenCalledTimes(1)
  expect(chartMocks.getPage).toHaveBeenLastCalledWith({
    offset: 0,
    limit: 50,
    search: null,
    onlyActive: null,
    includeDeleted: true,
    onlyDeleted: true,
  })

  await view.getByRole('button', { name: 'Layout next' }).click()
  await flushUi()

  expect(router.currentRoute.value.query.offset).toBe('50')
  expect(chartMocks.getPage).toHaveBeenLastCalledWith({
    offset: 50,
    limit: 50,
    search: null,
    onlyActive: null,
    includeDeleted: true,
    onlyDeleted: true,
  })

  await view.getByRole('button', { name: 'Layout create' }).click()
  await flushUi()
  expect(router.currentRoute.value.query.panel).toBe('new')
  await expect.element(view.getByText('editor-id:new')).toBeVisible()

  await view.getByRole('button', { name: 'Layout close drawer' }).click()
  await flushUi()

  await view.getByRole('button', { name: /code=1010/ }).click()
  await flushUi()
  expect(router.currentRoute.value.query.panel).toBe('edit')
  expect(router.currentRoute.value.query.id).toBe('coa-1')
  await expect.element(view.getByText('editor-id:coa-1')).toBeVisible()

  await view.getByRole('button', { name: 'Layout refresh' }).click()
  await view.getByRole('button', { name: 'Layout back' }).click()

  expect(chartMocks.getPage.mock.calls.length).toBeGreaterThanOrEqual(4)
  expect(chartMocks.navigateBack).toHaveBeenCalledTimes(1)
  expect(chartMocks.navigateBack.mock.calls[0]?.[2]).toBe('/home')
})

test('keeps the drawer open and rewrites the route to the created account id when the post-create reload fails', async () => {
  await page.viewport(1280, 900)

  chartMocks.getPage
    .mockResolvedValueOnce({
      offset: 0,
      limit: 50,
      total: 1,
      items: [
        {
          accountId: 'coa-1',
          code: '1010',
          name: 'Cash',
          accountType: 'Asset',
          cashFlowRole: 'operating',
          isActive: true,
          isDeleted: false,
          isMarkedForDeletion: false,
        },
      ],
    })
    .mockRejectedValueOnce(new Error('Reload failed after create'))

  const { router, view } = await renderPage('/admin/chart-of-accounts?panel=new')

  await expect.element(view.getByText('editor-id:new')).toBeVisible()
  await expect.element(view.getByText('drawer-open:true')).toBeVisible()

  await view.getByRole('button', { name: 'Editor emit created' }).click()
  await flushUi()
  await flushUi()

  expect(chartMocks.getPage.mock.calls.length).toBeGreaterThanOrEqual(2)
  expect(router.currentRoute.value.query.panel).toBe('edit')
  expect(router.currentRoute.value.query.id).toBe('coa-created')
  await expect.element(view.getByText('drawer-open:true')).toBeVisible()
  await expect.element(view.getByText('editor-id:coa-created')).toBeVisible()
})

test('applies the real commit policy by closing successful create, save, and mark-for-deletion flows', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderPage('/admin/chart-of-accounts?panel=new')

  await expect.element(view.getByText('drawer-open:true')).toBeVisible()

  await view.getByRole('button', { name: 'Editor emit created' }).click()
  await flushUi()
  await flushUi()

  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()

  await view.getByRole('button', { name: /code=1010/ }).click()
  await flushUi()
  await expect.element(view.getByText('editor-id:coa-1')).toBeVisible()

  await view.getByRole('button', { name: 'Editor emit saved' }).click()
  await flushUi()
  await flushUi()

  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()

  await view.getByRole('button', { name: /code=1010/ }).click()
  await flushUi()
  await expect.element(view.getByText('editor-id:coa-1')).toBeVisible()

  await view.getByRole('button', { name: 'Editor emit changed' }).click()
  await flushUi()
  await flushUi()

  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()
  expect(chartMocks.getPage.mock.calls.length).toBeGreaterThanOrEqual(4)
})

test('ignores stale chart page responses when route paging changes before the first request finishes', async () => {
  await page.viewport(1280, 900)

  const first = createDeferred<{
    offset: number
    limit: number
    total: number
    items: Array<Record<string, unknown>>
  }>()
  const second = createDeferred<{
    offset: number
    limit: number
    total: number
    items: Array<Record<string, unknown>>
  }>()

  chartMocks.getPage.mockImplementation(async (args: { offset: number }) => {
    if (args.offset === 0) return await first.promise
    return await second.promise
  })

  const { router, view } = await renderPage('/admin/chart-of-accounts?offset=0')

  const secondNavigation = router.push('/admin/chart-of-accounts?offset=50')
  await flushUi()

  second.resolve({
    offset: 50,
    limit: 50,
    total: 1,
    items: [
      {
        accountId: 'coa-2',
        code: '2020',
        name: 'Accounts Receivable',
        accountType: 'Asset',
        cashFlowRole: 'operating',
        isActive: true,
        isDeleted: false,
        isMarkedForDeletion: false,
      },
    ],
  })
  await secondNavigation
  await flushUi()

  await expect.element(view.getByText(/code=2020/)).toBeVisible()
  expect(document.body.textContent).not.toContain('code=1010')

  first.resolve({
    offset: 0,
    limit: 50,
    total: 1,
    items: [
      {
        accountId: 'coa-1',
        code: '1010',
        name: 'Cash',
        accountType: 'Asset',
        cashFlowRole: 'operating',
        isActive: true,
        isDeleted: false,
        isMarkedForDeletion: false,
      },
    ],
  })
  await flushUi()

  await expect.element(view.getByText(/code=2020/)).toBeVisible()
  expect(document.body.textContent).not.toContain('code=1010')
  expect(router.currentRoute.value.query.offset).toBe('50')
})
