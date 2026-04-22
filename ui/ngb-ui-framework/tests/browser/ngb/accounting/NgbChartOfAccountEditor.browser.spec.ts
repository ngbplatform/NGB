import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, h, ref } from 'vue'

import {
  StubCheckbox,
  StubConfirmDialog,
  StubEntityAuditSidebar,
  StubFormLayout,
  StubFormRow,
  StubInput,
  StubSelect,
  StubValidationSummary,
} from './stubs'

const chartEditorMocks = vi.hoisted(() => ({
  copyAppLink: vi.fn(),
  createChartOfAccount: vi.fn(),
  getChartOfAccountById: vi.fn(),
  markChartOfAccountForDeletion: vi.fn(),
  toasts: {
    push: vi.fn(),
  },
  unmarkChartOfAccountForDeletion: vi.fn(),
  updateChartOfAccount: vi.fn(),
}))

vi.mock('../../../../src/ngb/components/NgbConfirmDialog.vue', () => ({
  default: StubConfirmDialog,
}))

vi.mock('../../../../src/ngb/components/forms/NgbFormLayout.vue', () => ({
  default: StubFormLayout,
}))

vi.mock('../../../../src/ngb/components/forms/NgbFormRow.vue', () => ({
  default: StubFormRow,
}))

vi.mock('../../../../src/ngb/components/forms/NgbValidationSummary.vue', () => ({
  default: StubValidationSummary,
}))

vi.mock('../../../../src/ngb/editor/NgbEntityAuditSidebar.vue', () => ({
  default: StubEntityAuditSidebar,
}))

vi.mock('../../../../src/ngb/primitives/NgbCheckbox.vue', () => ({
  default: StubCheckbox,
}))

vi.mock('../../../../src/ngb/primitives/NgbInput.vue', () => ({
  default: StubInput,
}))

vi.mock('../../../../src/ngb/primitives/NgbSelect.vue', () => ({
  default: StubSelect,
}))

vi.mock('../../../../src/ngb/primitives/toast', () => ({
  useToasts: () => chartEditorMocks.toasts,
}))

vi.mock('../../../../src/ngb/router/shareLink', () => ({
  copyAppLink: chartEditorMocks.copyAppLink,
}))

vi.mock('../../../../src/ngb/accounting/api', () => ({
  createChartOfAccount: chartEditorMocks.createChartOfAccount,
  getChartOfAccountById: chartEditorMocks.getChartOfAccountById,
  markChartOfAccountForDeletion: chartEditorMocks.markChartOfAccountForDeletion,
  unmarkChartOfAccountForDeletion: chartEditorMocks.unmarkChartOfAccountForDeletion,
  updateChartOfAccount: chartEditorMocks.updateChartOfAccount,
}))

import NgbChartOfAccountEditor from '../../../../src/ngb/accounting/NgbChartOfAccountEditor.vue'

const chartMetadata = {
  accountTypeOptions: [
    { value: 'Asset', label: 'Asset' },
    { value: 'Liability', label: 'Liability' },
  ],
  cashFlowRoleOptions: [
    {
      value: 'OPERATING',
      label: 'Operating activities',
      supportsLineCode: true,
      requiresLineCode: true,
    },
    {
      value: 'OTHER',
      label: 'Other',
      supportsLineCode: false,
      requiresLineCode: false,
    },
  ],
  cashFlowLineOptions: [
    {
      value: 'NET_INCOME',
      label: 'Net income',
      section: 'Operating',
      allowedRoles: ['OPERATING'],
    },
  ],
}

function setInputValue(input: HTMLInputElement, value: string) {
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  input.dispatchEvent(new Event('change', { bubbles: true }))
}

function setSelectValue(select: HTMLSelectElement, value: string) {
  select.value = value
  select.dispatchEvent(new Event('input', { bubbles: true }))
  select.dispatchEvent(new Event('change', { bubbles: true }))
}

const ChartOfAccountEditorHarness = defineComponent({
  props: {
    id: {
      type: String,
      default: null,
    },
  },
  setup(props) {
    const editorRef = ref<{
      save: () => void
      copyShareLink: () => Promise<boolean>
      openAuditLog: () => void
      toggleMarkForDeletion: () => void
    } | null>(null)
    const events = ref<string[]>([])
    const flags = ref('none')
    const shell = ref('none')
    const title = ref('none')

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => editorRef.value?.save(),
      }, 'Invoke save'),
      h('button', {
        type: 'button',
        onClick: () => editorRef.value?.copyShareLink(),
      }, 'Invoke share'),
      h('button', {
        type: 'button',
        onClick: () => editorRef.value?.openAuditLog(),
      }, 'Invoke audit'),
      h('button', {
        type: 'button',
        onClick: () => editorRef.value?.toggleMarkForDeletion(),
      }, 'Invoke mark'),
      h(NgbChartOfAccountEditor, {
        ref: editorRef,
        id: props.id,
        metadata: chartMetadata,
        routeBasePath: '/admin/chart-of-accounts',
        onCreated: (id: string) => {
          events.value = [...events.value, `created:${id}`]
        },
        onSaved: () => {
          events.value = [...events.value, 'saved']
        },
        onChanged: () => {
          events.value = [...events.value, 'changed']
        },
        onState: (value: { title: string }) => {
          title.value = value.title
        },
        onFlags: (value: Record<string, unknown>) => {
          flags.value = JSON.stringify(value)
        },
        onShell: (value: { hideHeader: boolean; flushBody: boolean }) => {
          shell.value = JSON.stringify(value)
        },
      }),
      h('div', { 'data-testid': 'chart-events' }, events.value.join('|') || 'none'),
      h('div', { 'data-testid': 'chart-flags' }, `flags:${flags.value}`),
      h('div', { 'data-testid': 'chart-shell' }, `shell:${shell.value}`),
      h('div', { 'data-testid': 'chart-title' }, `title:${title.value}`),
    ])
  },
})

const SwitchingChartOfAccountEditorHarness = defineComponent({
  setup() {
    const currentId = ref('coa-stale')
    const title = ref('none')

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => {
          currentId.value = 'coa-fresh'
        },
      }, 'Switch account id'),
      h(NgbChartOfAccountEditor, {
        id: currentId.value,
        metadata: chartMetadata,
        routeBasePath: '/admin/chart-of-accounts',
        onState: (value: { title: string }) => {
          title.value = value.title
        },
      }),
      h('div', { 'data-testid': 'switching-editor-id' }, `editor-id:${currentId.value}`),
      h('div', { 'data-testid': 'switching-editor-title' }, `title:${title.value}`),
    ])
  },
})

beforeEach(() => {
  vi.clearAllMocks()
})

async function renderChartEditor(id: string | null = null) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/admin/chart-of-accounts',
        component: {
          template: '<div />',
        },
      },
    ],
  })

  await router.push('/admin/chart-of-accounts')
  await router.isReady()

  const view = await render(ChartOfAccountEditorHarness, {
    props: {
      id,
    },
    global: {
      plugins: [router],
    },
  })

  return {
    router,
    view,
  }
}

async function renderSwitchingChartEditor() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/admin/chart-of-accounts',
        component: {
          template: '<div />',
        },
      },
    ],
  })

  await router.push('/admin/chart-of-accounts')
  await router.isReady()

  const view = await render(SwitchingChartOfAccountEditorHarness, {
    global: {
      plugins: [router],
    },
  })

  return {
    router,
    view,
  }
}

function readFlags(view: Awaited<ReturnType<typeof renderChartEditor>>['view']) {
  const raw = view.getByTestId('chart-flags').element().textContent ?? 'flags:none'
  const json = raw.replace(/^flags:/, '')
  return json === 'none' ? null : JSON.parse(json)
}

test('validates create mode, enforces cash-flow line requirements, and emits created after save', async () => {
  await page.viewport(1280, 900)

  chartEditorMocks.createChartOfAccount.mockResolvedValue({
    accountId: 'coa-1',
    code: '1100',
    name: 'Cash',
    accountType: 'Asset',
    cashFlowRole: 'OPERATING',
    cashFlowLineCode: 'NET_INCOME',
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })

  const { view } = await renderChartEditor()

  await expect.element(view.getByText('Code is required.', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Name is required.', { exact: true })).toBeVisible()

  const inputs = Array.from(document.querySelectorAll('input[type="text"]')) as HTMLInputElement[]
  setInputValue(inputs[0]!, '1100')
  setInputValue(inputs[1]!, 'Cash')

  const selects = Array.from(document.querySelectorAll('select')) as HTMLSelectElement[]
  setSelectValue(selects[1]!, 'OPERATING')
  await expect.element(view.getByText('Cash Flow Line Code is required for the selected role.', { exact: true })).toBeVisible()

  setSelectValue(selects[2]!, 'NET_INCOME')
  await view.getByRole('button', { name: 'Invoke save' }).click()

  await vi.waitFor(() => {
    expect(chartEditorMocks.createChartOfAccount).toHaveBeenCalledWith({
      code: '1100',
      name: 'Cash',
      accountType: 'Asset',
      isActive: true,
      cashFlowRole: 'OPERATING',
      cashFlowLineCode: 'NET_INCOME',
    })
  })
  await expect.element(view.getByTestId('chart-events')).toHaveTextContent('created:coa-1')
  await expect.element(view.getByTestId('chart-title')).toHaveTextContent('title:New account')
})

test('loads an existing account, updates it, shares links, opens audit, and toggles mark-for-deletion', async () => {
  await page.viewport(1280, 900)

  chartEditorMocks.getChartOfAccountById.mockResolvedValue({
    accountId: 'coa-2',
    code: '1200',
    name: 'Receivables',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })
  chartEditorMocks.updateChartOfAccount.mockResolvedValue({
    accountId: 'coa-2',
    code: '1200',
    name: 'Receivables updated',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })
  chartEditorMocks.copyAppLink.mockResolvedValue(true)
  chartEditorMocks.markChartOfAccountForDeletion.mockResolvedValue(undefined)
  chartEditorMocks.unmarkChartOfAccountForDeletion.mockResolvedValue(undefined)

  const { view } = await renderChartEditor('coa-2')
  await expect.element(view.getByTestId('chart-title')).toHaveTextContent('title:1200 — Receivables')

  const inputs = Array.from(document.querySelectorAll('input[type="text"]')) as HTMLInputElement[]
  setInputValue(inputs[1]!, 'Receivables updated')
  await view.getByRole('button', { name: 'Invoke save' }).click()

  await vi.waitFor(() => {
    expect(chartEditorMocks.updateChartOfAccount).toHaveBeenCalledWith('coa-2', expect.objectContaining({
      code: '1200',
      name: 'Receivables updated',
    }))
  })
  await expect.element(view.getByTestId('chart-events')).toHaveTextContent('saved')

  await view.getByRole('button', { name: 'Invoke share' }).click()
  expect(chartEditorMocks.copyAppLink).toHaveBeenCalledWith(
    expect.anything(),
    chartEditorMocks.toasts,
    '/admin/chart-of-accounts?panel=edit&id=coa-2',
    { message: 'Shareable account link copied to clipboard.' },
  )

  await view.getByRole('button', { name: 'Invoke audit' }).click()
  await expect.element(view.getByTestId('audit-sidebar')).toBeVisible()
  await expect.element(view.getByTestId('chart-shell')).toHaveTextContent('shell:{"hideHeader":true,"flushBody":true}')
  await view.getByRole('button', { name: 'Audit back' }).click()
  await expect.element(view.getByTestId('chart-shell')).toHaveTextContent('shell:{"hideHeader":false,"flushBody":false}')

  await view.getByRole('button', { name: 'Invoke mark' }).click()
  await expect.element(view.getByTestId('confirm-dialog')).toBeVisible()
  await view.getByRole('button', { name: 'Dialog confirm:Mark' }).click()
  await vi.waitFor(() => {
    expect(chartEditorMocks.markChartOfAccountForDeletion).toHaveBeenCalledWith('coa-2')
  })

  await view.getByRole('button', { name: 'Invoke mark' }).click()
  await vi.waitFor(() => {
    expect(chartEditorMocks.unmarkChartOfAccountForDeletion).toHaveBeenCalledWith('coa-2')
  })
  await expect.element(view.getByTestId('chart-events')).toHaveTextContent('saved|changed|changed')
})

test('emits live state, flags, and shell updates as the real editor changes state', async () => {
  await page.viewport(1280, 900)

  chartEditorMocks.getChartOfAccountById.mockResolvedValue({
    accountId: 'coa-6',
    code: '1600',
    name: 'Deposits',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })
  chartEditorMocks.updateChartOfAccount.mockResolvedValue({
    accountId: 'coa-6',
    code: '1600',
    name: 'Deposits revised',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })
  chartEditorMocks.markChartOfAccountForDeletion.mockResolvedValue(undefined)

  const { view } = await renderChartEditor('coa-6')

  await expect.element(view.getByTestId('chart-title')).toHaveTextContent('title:1600 — Deposits')
  expect(readFlags(view)).toMatchObject({
    canSave: true,
    isDirty: false,
    canMarkForDeletion: true,
    canUnmarkForDeletion: false,
    canShowAudit: true,
    canShareLink: true,
  })

  const inputs = Array.from(document.querySelectorAll('input[type="text"]')) as HTMLInputElement[]
  setInputValue(inputs[1]!, 'Deposits revised')

  await vi.waitFor(() => {
    expect(readFlags(view)).toMatchObject({
      canSave: true,
      isDirty: true,
    })
  })

  await view.getByRole('button', { name: 'Invoke audit' }).click()
  await expect.element(view.getByTestId('chart-shell')).toHaveTextContent('shell:{"hideHeader":true,"flushBody":true}')
  await view.getByRole('button', { name: 'Audit back' }).click()
  await expect.element(view.getByTestId('chart-shell')).toHaveTextContent('shell:{"hideHeader":false,"flushBody":false}')

  await view.getByRole('button', { name: 'Invoke save' }).click()
  await vi.waitFor(() => {
    expect(chartEditorMocks.updateChartOfAccount).toHaveBeenCalledWith('coa-6', expect.objectContaining({
      name: 'Deposits revised',
    }))
  })
  await expect.element(view.getByTestId('chart-title')).toHaveTextContent('title:1600 — Deposits revised')
  await vi.waitFor(() => {
    expect(readFlags(view)).toMatchObject({
      canSave: true,
      isDirty: false,
      canMarkForDeletion: true,
    })
  })

  await view.getByRole('button', { name: 'Invoke mark' }).click()
  await view.getByRole('button', { name: 'Dialog confirm:Mark' }).click()

  await vi.waitFor(() => {
    expect(chartEditorMocks.markChartOfAccountForDeletion).toHaveBeenCalledWith('coa-6')
  })
  await vi.waitFor(() => {
    expect(readFlags(view)).toMatchObject({
      canSave: false,
      isDirty: false,
      canMarkForDeletion: false,
      canUnmarkForDeletion: true,
      canShowAudit: true,
      canShareLink: true,
    })
  })
})

test('ignores stale account loads when the editor id switches before the first request resolves', async () => {
  await page.viewport(1280, 900)

  const stale = (() => {
    let resolve!: (value: Record<string, unknown>) => void
    const promise = new Promise<Record<string, unknown>>((nextResolve) => {
      resolve = nextResolve
    })
    return { promise, resolve }
  })()
  const fresh = (() => {
    let resolve!: (value: Record<string, unknown>) => void
    const promise = new Promise<Record<string, unknown>>((nextResolve) => {
      resolve = nextResolve
    })
    return { promise, resolve }
  })()

  chartEditorMocks.getChartOfAccountById.mockImplementation(async (accountId: string) => {
    if (accountId === 'coa-stale') return await stale.promise
    return await fresh.promise
  })

  const { view } = await renderSwitchingChartEditor()

  await view.getByRole('button', { name: 'Switch account id' }).click()
  await expect.element(view.getByTestId('switching-editor-id')).toHaveTextContent('editor-id:coa-fresh')

  fresh.resolve({
    accountId: 'coa-fresh',
    code: '2200',
    name: 'Receivables',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })

  await expect.element(view.getByTestId('switching-editor-title')).toHaveTextContent('title:2200 — Receivables')

  stale.resolve({
    accountId: 'coa-stale',
    code: '1100',
    name: 'Cash',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })

  await vi.waitFor(() => {
    expect(view.getByTestId('switching-editor-title').element().textContent ?? '').toBe('title:2200 — Receivables')
  })
  expect(document.body.textContent ?? '').not.toContain('1100 — Cash')
})

test('shows an inline load error when an existing account cannot be fetched', async () => {
  await page.viewport(1280, 900)

  chartEditorMocks.getChartOfAccountById.mockRejectedValueOnce(new Error('Account lookup failed.'))

  const { view } = await renderChartEditor('coa-load-error')

  await expect.element(view.getByText('Account lookup failed.')).toBeVisible()
  await expect.element(view.getByTestId('chart-title')).toHaveTextContent('title:Account')
})

test('keeps the editor usable after save failures and allows a successful retry', async () => {
  await page.viewport(1280, 900)

  chartEditorMocks.getChartOfAccountById.mockResolvedValue({
    accountId: 'coa-3',
    code: '1300',
    name: 'Prepayments',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })
  chartEditorMocks.updateChartOfAccount
    .mockRejectedValueOnce(new Error('Save failed for the account.'))
    .mockResolvedValueOnce({
      accountId: 'coa-3',
      code: '1300',
      name: 'Prepayments revised',
      accountType: 'Asset',
      cashFlowRole: null,
      cashFlowLineCode: null,
      isActive: true,
      isDeleted: false,
      isMarkedForDeletion: false,
    })

  const { view } = await renderChartEditor('coa-3')

  const inputs = Array.from(document.querySelectorAll('input[type="text"]')) as HTMLInputElement[]
  setInputValue(inputs[1]!, 'Prepayments revised')

  await view.getByRole('button', { name: 'Invoke save' }).click()
  await expect.element(view.getByText('Save failed for the account.')).toBeVisible()
  expect(chartEditorMocks.updateChartOfAccount).toHaveBeenCalledTimes(1)

  await view.getByRole('button', { name: 'Invoke save' }).click()
  await vi.waitFor(() => {
    expect(chartEditorMocks.updateChartOfAccount).toHaveBeenCalledTimes(2)
  })
  await expect.element(view.getByTestId('chart-events')).toHaveTextContent('saved')
  await expect.element(view.getByTestId('chart-title')).toHaveTextContent('title:1300 — Prepayments revised')
})

test('renders mark-for-deletion failures inline without emitting a change event', async () => {
  await page.viewport(1280, 900)

  chartEditorMocks.getChartOfAccountById.mockResolvedValue({
    accountId: 'coa-4',
    code: '1400',
    name: 'Inventory',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  })
  chartEditorMocks.markChartOfAccountForDeletion.mockRejectedValueOnce(new Error('Cannot mark this account yet.'))

  const { view } = await renderChartEditor('coa-4')

  await view.getByRole('button', { name: 'Invoke mark' }).click()
  await view.getByRole('button', { name: 'Dialog confirm:Mark' }).click()

  await expect.element(view.getByText('Cannot mark this account yet.')).toBeVisible()
  await expect.element(view.getByTestId('chart-events')).toHaveTextContent('none')
})

test('renders restore failures inline when an account cannot be unmarked', async () => {
  await page.viewport(1280, 900)

  chartEditorMocks.getChartOfAccountById.mockResolvedValue({
    accountId: 'coa-5',
    code: '1500',
    name: 'Fixed Assets',
    accountType: 'Asset',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: true,
  })
  chartEditorMocks.unmarkChartOfAccountForDeletion.mockRejectedValueOnce(new Error('Cannot restore this account.'))

  const { view } = await renderChartEditor('coa-5')

  await view.getByRole('button', { name: 'Invoke mark' }).click()

  await expect.element(view.getByText('Cannot restore this account.')).toBeVisible()
  await expect.element(view.getByText('This account is marked for deletion. Restore it to edit.')).toBeVisible()
  await expect.element(view.getByTestId('chart-events')).toHaveTextContent('none')
})
