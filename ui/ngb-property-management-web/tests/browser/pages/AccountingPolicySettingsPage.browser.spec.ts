import { nextTick } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  routerBack: vi.fn(),
  toastPush: vi.fn(),
  ensureCatalogType: vi.fn(),
  getCatalogPage: vi.fn(),
  updateCatalog: vi.fn(),
  httpPost: vi.fn(),
  copyAppLink: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ back: mocks.routerBack }),
}))

vi.mock('@ngbplatform/ui', async () => {
  const { defineComponent, h } = await import('vue')

  const PageHeader = defineComponent({
    props: { title: { type: String, required: true } },
    emits: ['back'],
    setup(props, { emit, slots }) {
      return () => h('header', [
        h('h1', props.title),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Header back'),
        h('div', slots.secondary?.()),
        h('div', slots.actions?.()),
      ])
    },
  })
  const Icon = defineComponent({
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  })
  const Button = defineComponent({
    props: { disabled: { type: Boolean, default: false } },
    emits: ['click'],
    setup(props, { emit, slots }) {
      return () => h('button', {
        type: 'button',
        disabled: props.disabled,
        onClick: () => emit('click'),
      }, slots.default?.())
    },
  })
  const Drawer = defineComponent({
    props: { open: { type: Boolean, default: false } },
    emits: ['update:open'],
    setup(props, { emit, slots }) {
      return () => props.open
        ? h('aside', { 'data-testid': 'drawer' }, [
            slots.default?.(),
            h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Drawer close'),
          ])
        : null
    },
  })
  const AuditSidebar = defineComponent({
    props: {
      entityTitle: { type: String, default: '' },
    },
    emits: ['back', 'close'],
    setup(props, { emit }) {
      return () => h('div', { 'data-testid': 'audit-sidebar' }, [
        h('span', props.entityTitle),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Audit back'),
        h('button', { type: 'button', onClick: () => emit('close') }, 'Audit close'),
      ])
    },
  })
  const EntityForm = defineComponent({
    props: {
      form: { type: Object, required: true },
      model: { type: Object, required: true },
    },
    setup(props) {
      return () => h('div', { 'data-testid': 'entity-form' }, (
        props.form as { sections?: Array<{ title?: string; rows?: Array<{ fields?: Array<{ key: string; label: string }> }> }> }
      ).sections?.flatMap((section) => [
        h('h2', section.title),
        ...(section.rows?.flatMap((row) => row.fields?.map((item) => h('label', { key: item.key }, [
          h('span', item.label),
          h('input', {
            'aria-label': item.label,
            value: String((props.model as Record<string, unknown>)[item.key] ?? ''),
            onInput: (event: Event) => {
              ;(props.model as Record<string, unknown>)[item.key] = (event.target as HTMLInputElement).value
            },
          }),
        ])) ?? []) ?? []),
      ]) ?? [])
    },
  })

  return {
    NgbButton: Button,
    NgbDrawer: Drawer,
    NgbEntityAuditSidebar: AuditSidebar,
    NgbEntityForm: EntityForm,
    NgbIcon: Icon,
    NgbPageHeader: PageHeader,
    buildFieldsPayload: (_form: unknown, model: Record<string, unknown>) => ({ ...model }),
    clonePlainData: <T>(value: T) => JSON.parse(JSON.stringify(value)) as T,
    copyAppLink: mocks.copyAppLink,
    ensureModelKeys: (form: { sections?: Array<{ rows?: Array<{ fields?: Array<{ key: string }> }> }> }, model: Record<string, unknown>) => {
      for (const section of form.sections ?? [])
        for (const row of section.rows ?? [])
          for (const item of row.fields ?? [])
            if (!(item.key in model)) model[item.key] = null
    },
    getCatalogPage: mocks.getCatalogPage,
    httpPost: mocks.httpPost,
    stableStringify: (value: unknown) => JSON.stringify(value),
    toErrorMessage: (cause: unknown, fallback: string) => cause instanceof Error ? cause.message : fallback,
    updateCatalog: mocks.updateCatalog,
    useMetadataStore: () => ({ ensureCatalogType: mocks.ensureCatalogType }),
    useToasts: () => ({ push: mocks.toastPush }),
  }
})

import AccountingPolicySettingsPage from '../../../src/pages/AccountingPolicySettingsPage.vue'

function form() {
  const item = (key: string, label: string) => ({
    key,
    label,
    dataType: 'Guid',
    uiControl: 1,
    isRequired: false,
    isReadOnly: false,
  })
  return {
    sections: [
      {
        title: 'Main',
        rows: [
          {
            fields: [
              item('display', 'Display'),
              item('tenant_balances_register_id', 'Tenant balances register'),
              item('receivables_open_items_register_id', 'Receivables register'),
              item('payables_open_items_register_id', 'Payables register'),
              item('cash_account_id', 'Cash'),
              item('ar_tenants_account_id', 'AR'),
              item('ap_vendors_account_id', 'AP'),
              item('rent_income_account_id', 'Rent'),
              item('late_fee_income_account_id', 'Late fee'),
            ],
          },
        ],
      },
    ],
  }
}

function policy(id = 'policy-1') {
  return {
    id,
    display: 'Existing policy',
    payload: {
      fields: {
        display: '',
        cash_account_id: 'cash-1',
        ar_tenants_account_id: 'ar-1',
        ap_vendors_account_id: 'ap-1',
        rent_income_account_id: 'rent-1',
        late_fee_income_account_id: 'late-1',
        tenant_balances_register_id: 'tenant-register',
      },
    },
  }
}

async function flushUi() {
  await Promise.resolve()
  await nextTick()
  await Promise.resolve()
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.ensureCatalogType.mockResolvedValue({ form: form() })
  mocks.getCatalogPage.mockResolvedValue({ items: [policy()] })
  mocks.updateCatalog.mockResolvedValue(undefined)
  mocks.httpPost.mockResolvedValue(undefined)
})

test('renders the PM form, preserves hidden values, saves changes, and wires header actions', async () => {
  let resolveSave!: () => void
  mocks.updateCatalog.mockReturnValue(new Promise<void>((resolve) => {
    resolveSave = resolve
  }))
  const view = await render(AccountingPolicySettingsPage)
  await flushUi()

  await expect.element(view.getByText('Settings', { exact: true }).first()).toBeVisible()
  for (const label of [
    'Default Cash Control Account',
    'Tenant Receivables (A/R) Account',
    'Vendor Payables (A/P) Account',
    'Rental Income Account',
    'Late Fee Income Account',
  ]) await expect.element(view.getByText(label)).toBeVisible()
  expect(document.body.textContent).not.toContain('Tenant balances register')

  await view.getByRole('button', { name: 'Header back' }).click()
  expect(mocks.routerBack).toHaveBeenCalledOnce()
  await view.getByTitle('Share link').click()
  expect(mocks.copyAppLink).toHaveBeenCalledWith(expect.any(Object), expect.any(Object), { path: '/catalogs/pm.accounting_policy' })

  await view.getByTitle('Audit log').click()
  await expect.element(view.getByTestId('audit-sidebar')).toHaveTextContent('Accounting Policy')
  await view.getByRole('button', { name: 'Audit back' }).click()
  await view.getByTitle('Audit log').click()
  await view.getByRole('button', { name: 'Audit close' }).click()
  await view.getByTitle('Audit log').click()
  await view.getByRole('button', { name: 'Drawer close' }).click()

  const cash = view.getByLabelText('Default Cash Control Account').element() as HTMLInputElement
  cash.value = 'cash-2'
  cash.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()
  const save = view.getByTitle('Save').element()
  save.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  save.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.updateCatalog).toHaveBeenCalledTimes(1)
  resolveSave()
  await flushUi()
  await flushUi()

  expect(mocks.updateCatalog).toHaveBeenCalledWith('pm.accounting_policy', 'policy-1', {
    fields: expect.objectContaining({
      display: 'Accounting Policy',
      cash_account_id: 'cash-2',
      tenant_balances_register_id: 'tenant-register',
    }),
  })
  expect(mocks.toastPush).toHaveBeenCalledWith({ title: 'Saved', message: 'Accounting policy updated.', tone: 'success' })
  await view.getByTitle('Refresh').click()
  expect(mocks.getCatalogPage).toHaveBeenCalledTimes(3)
})

test('applies defaults from the empty state and ignores a concurrent second request', async () => {
  let resolveDefaults!: () => void
  mocks.getCatalogPage
    .mockResolvedValueOnce({ items: [] })
    .mockResolvedValueOnce({ items: [policy()] })
  mocks.httpPost.mockReturnValue(new Promise<void>((resolve) => {
    resolveDefaults = resolve
  }))

  const view = await render(AccountingPolicySettingsPage)
  await flushUi()
  view.getByTitle('Save').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.updateCatalog).not.toHaveBeenCalled()
  const apply = view.getByRole('button', { name: 'Apply defaults' }).element()
  apply.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  apply.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.httpPost).toHaveBeenCalledTimes(1)

  resolveDefaults()
  await flushUi()
  await flushUi()
  expect(mocks.toastPush).toHaveBeenCalledWith({
    title: 'Defaults applied',
    message: 'Default configuration has been created/updated.',
    tone: 'success',
  })
  await expect.element(view.getByTestId('accounting-policy-form')).toBeVisible()
})

test('normalizes sparse forms and guards share and audit without a policy id', async () => {
  mocks.ensureCatalogType.mockResolvedValue({
    form: {
      sections: [
        { title: 'No rows' },
        { title: 'No fields', rows: [{}] },
        { title: 'Other', rows: [{ fields: [
          { key: 'display', label: 'Hidden' },
          { key: 'custom', label: 'Custom Setting' },
        ] }] },
      ],
    },
  })
  mocks.getCatalogPage.mockResolvedValue({ items: [{ id: '', display: null, payload: null }] })

  const view = await render(AccountingPolicySettingsPage)
  await flushUi()
  await expect.element(view.getByText('Custom Setting')).toBeVisible()
  expect(document.body.textContent).not.toContain('Hidden')

  view.getByTitle('Share link').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  view.getByTitle('Audit log').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  view.getByTitle('Save').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.copyAppLink).not.toHaveBeenCalled()
  expect(mocks.updateCatalog).toHaveBeenCalledWith('pm.accounting_policy', '', {
    fields: { display: 'Accounting Policy', custom: null },
  })
  expect(document.querySelector('[data-testid="drawer"]')).toBeNull()
})

test('handles absent and untitled form sections', async () => {
  mocks.ensureCatalogType
    .mockResolvedValueOnce({ form: {} })
    .mockResolvedValueOnce({ form: { sections: [{ rows: [{ fields: [{ key: 'custom', label: 'Custom Setting' }] }] }] } })
  mocks.getCatalogPage.mockResolvedValue({ items: [policy()] })

  const empty = await render(AccountingPolicySettingsPage)
  await flushUi()
  expect(empty.getByTestId('accounting-policy-form').element()).not.toBeNull()
  empty.unmount()

  const untitled = await render(AccountingPolicySettingsPage)
  await flushUi()
  await expect.element(untitled.getByText('Custom Setting')).toBeVisible()
})

test('renders no-form metadata and reports load, save, and defaults failures', async () => {
  mocks.ensureCatalogType.mockResolvedValueOnce({ form: null })
  const noForm = await render(AccountingPolicySettingsPage)
  await flushUi()
  await expect.element(noForm.getByText('No form metadata available.')).toBeVisible()
  noForm.getByTitle('Save').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.updateCatalog).not.toHaveBeenCalled()
  noForm.unmount()

  mocks.ensureCatalogType.mockRejectedValueOnce(new Error('Metadata unavailable'))
  const loadFailure = await render(AccountingPolicySettingsPage)
  await flushUi()
  await expect.element(loadFailure.getByText('Metadata unavailable')).toBeVisible()
  loadFailure.unmount()

  mocks.ensureCatalogType.mockResolvedValue({ form: form() })
  mocks.updateCatalog.mockRejectedValueOnce(new Error('Save rejected'))
  const saveFailure = await render(AccountingPolicySettingsPage)
  await flushUi()
  const cash = saveFailure.getByLabelText('Default Cash Control Account').element() as HTMLInputElement
  cash.value = 'cash-2'
  cash.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()
  await saveFailure.getByTitle('Save').click()
  await flushUi()
  await expect.element(saveFailure.getByText('Save rejected')).toBeVisible()
  saveFailure.unmount()

  mocks.getCatalogPage.mockResolvedValue({ items: [] })
  mocks.httpPost.mockRejectedValueOnce(new Error('Defaults rejected'))
  const defaultsFailure = await render(AccountingPolicySettingsPage)
  await flushUi()
  await defaultsFailure.getByRole('button', { name: 'Apply defaults' }).click()
  await flushUi()
  await expect.element(defaultsFailure.getByText('Defaults rejected')).toBeVisible()
})

test('ignores successful and failed policy loads that settle after unmount', async () => {
  let resolvePage!: (value: { items: unknown[] }) => void
  mocks.getCatalogPage.mockReturnValueOnce(new Promise((resolve) => {
    resolvePage = resolve
  }))
  const successful = await render(AccountingPolicySettingsPage)
  await vi.waitFor(() => expect(mocks.getCatalogPage).toHaveBeenCalledOnce())
  successful.unmount()
  resolvePage({ items: [] })
  await flushUi()

  mocks.getCatalogPage.mockReset()
  let rejectPage!: (cause: unknown) => void
  mocks.getCatalogPage.mockReturnValueOnce(new Promise((_resolve, reject) => {
    rejectPage = reject
  }))
  const failed = await render(AccountingPolicySettingsPage)
  await vi.waitFor(() => expect(mocks.getCatalogPage).toHaveBeenCalledOnce())
  failed.unmount()
  rejectPage(new Error('late failure'))
  await flushUi()
})
