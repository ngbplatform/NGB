import { nextTick, reactive } from 'vue'
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
  useRouter: () => ({
    back: mocks.routerBack,
  }),
}))

vi.mock('@ngbplatform/ui', async () => {
  const { defineComponent, h } = await import('vue')

  const StubPageHeader = defineComponent({
    name: 'StubPageHeader',
    props: {
      title: { type: String, required: true },
    },
    emits: ['back'],
    setup(props, { emit, slots }) {
      return () => h('header', { 'data-testid': 'policy-header' }, [
        h('h1', props.title),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Header back'),
        h('div', slots.secondary?.()),
        h('div', slots.actions?.()),
      ])
    },
  })

  const StubIcon = defineComponent({
    name: 'StubIcon',
    props: {
      name: { type: String, required: true },
    },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  })

  const StubButton = defineComponent({
    name: 'StubButton',
    props: {
      disabled: { type: Boolean, default: false },
    },
    emits: ['click'],
    setup(props, { emit, slots }) {
      return () => h('button', {
        type: 'button',
        disabled: props.disabled,
        onClick: () => emit('click'),
      }, slots.default?.())
    },
  })

  const StubDrawer = defineComponent({
    name: 'StubDrawer',
    props: {
      open: { type: Boolean, default: false },
    },
    emits: ['update:open'],
    setup(props, { emit, slots }) {
      return () => props.open ? h('aside', { 'data-testid': 'drawer' }, [
        slots.default?.(),
        h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Drawer close'),
      ]) : h('div', { hidden: true }, slots.default?.())
    },
  })

  const StubAuditSidebar = defineComponent({
    name: 'StubAuditSidebar',
    emits: ['back', 'close'],
    setup(_, { emit }) {
      return () => h('div', { 'data-testid': 'audit-sidebar' }, [
        'Audit sidebar',
        h('button', { type: 'button', onClick: () => emit('back') }, 'Audit back'),
        h('button', { type: 'button', onClick: () => emit('close') }, 'Audit close'),
      ])
    },
  })

  const StubEntityForm = defineComponent({
    name: 'StubEntityForm',
    props: {
      form: { type: Object, required: true },
      model: { type: Object, required: true },
    },
    setup(props) {
      return () => h('div', { 'data-testid': 'entity-form' }, (
        props.form as { sections?: Array<{ rows?: Array<{ fields?: Array<{ key: string; label: string }> }> }> }
      ).sections?.flatMap((section) =>
        section.rows?.flatMap((row) =>
          row.fields?.map((field) =>
            h('label', { key: field.key }, [
              h('span', field.label),
              h('input', {
                'aria-label': field.label,
                value: String((props.model as Record<string, unknown>)[field.key] ?? ''),
                onInput: (event: Event) => {
                  ;(props.model as Record<string, unknown>)[field.key] = (event.target as HTMLInputElement).value
                },
              }),
            ]),
          ) ?? [],
        ) ?? [],
      ) ?? [])
    },
  })

  return {
    NgbButton: StubButton,
    NgbDrawer: StubDrawer,
    NgbEntityAuditSidebar: StubAuditSidebar,
    NgbEntityForm: StubEntityForm,
    NgbIcon: StubIcon,
    NgbPageHeader: StubPageHeader,
    buildFieldsPayload: (_form: unknown, model: Record<string, unknown>) => ({ ...model }),
    clonePlainData: <T>(value: T) => JSON.parse(JSON.stringify(value)) as T,
    copyAppLink: mocks.copyAppLink,
    ensureModelKeys: (form: { sections?: Array<{ rows?: Array<{ fields?: Array<{ key: string }> }> }> }, model: Record<string, unknown>) => {
      for (const section of form.sections ?? []) {
        for (const row of section.rows ?? []) {
          for (const field of row.fields ?? []) {
            if (!(field.key in model)) model[field.key] = null
          }
        }
      }
    },
    getCatalogPage: mocks.getCatalogPage,
    httpPost: mocks.httpPost,
    stableStringify: (value: unknown) => JSON.stringify(value),
    toErrorMessage: (cause: unknown, fallback: string) => cause instanceof Error ? cause.message : fallback,
    updateCatalog: mocks.updateCatalog,
    useMetadataStore: () => ({
      ensureCatalogType: mocks.ensureCatalogType,
    }),
    useToasts: () => ({
      push: mocks.toastPush,
    }),
  }
})

import AccountingPolicySettingsPage from '../../../src/pages/AccountingPolicySettingsPage.vue'

function flushUi() {
  return Promise.resolve()
    .then(() => nextTick())
    .then(() => Promise.resolve())
}

function metadataForm() {
  return {
    sections: [
      {
        title: 'Main',
        rows: [
          {
            fields: [
              { key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
              { key: 'project_time_ledger_register_id', label: 'Project Time Ledger', dataType: 'Guid', uiControl: 1, isRequired: false, isReadOnly: false },
              { key: 'unbilled_time_register_id', label: 'Unbilled Time Register', dataType: 'Guid', uiControl: 1, isRequired: false, isReadOnly: false },
              { key: 'project_billing_status_register_id', label: 'Project Billing Status', dataType: 'Guid', uiControl: 1, isRequired: false, isReadOnly: false },
              { key: 'cash_account_id', label: 'Cash Account', dataType: 'Guid', uiControl: 1, isRequired: true, isReadOnly: false },
              { key: 'ar_account_id', label: 'AR Account', dataType: 'Guid', uiControl: 1, isRequired: true, isReadOnly: false },
              { key: 'service_revenue_account_id', label: 'Revenue Account', dataType: 'Guid', uiControl: 1, isRequired: true, isReadOnly: false },
              { key: 'default_currency', label: 'Currency', dataType: 'String', uiControl: 1, isRequired: true, isReadOnly: false },
            ],
          },
        ],
      },
    ],
  }
}

beforeEach(() => {
  mocks.routerBack.mockReset()
  mocks.toastPush.mockReset()
  mocks.ensureCatalogType.mockReset()
  mocks.getCatalogPage.mockReset()
  mocks.updateCatalog.mockReset()
  mocks.httpPost.mockReset()
  mocks.copyAppLink.mockReset()

  mocks.ensureCatalogType.mockResolvedValue({
    form: metadataForm(),
  })
})

test('renders the trimmed agency billing policy form and saves edited values', async () => {
  mocks.getCatalogPage.mockResolvedValue({
    items: [
      {
        id: 'policy-1',
        display: 'Accounting Policy',
        payload: {
          fields: reactive({
            display: '',
            cash_account_id: 'cash-100',
            ar_account_id: 'ar-100',
            service_revenue_account_id: 'rev-100',
            default_currency: 'USD',
          }),
        },
      },
    ],
  })
  mocks.updateCatalog.mockResolvedValue(undefined)

  const view = await render(AccountingPolicySettingsPage)
  await flushUi()

  await expect.element(view.getByTestId('agency-billing-accounting-policy-form')).toBeVisible()
  await view.getByRole('button', { name: 'Header back' }).click()
  expect(mocks.routerBack).toHaveBeenCalledOnce()

  await view.getByTitle('Share link').click()
  expect(mocks.copyAppLink).toHaveBeenCalledWith(
    expect.any(Object),
    expect.any(Object),
    { path: '/catalogs/ab.accounting_policy' },
  )

  await view.getByRole('button', { name: 'Audit log' }).click()
  await expect.element(view.getByTestId('audit-sidebar')).toBeVisible()
  await view.getByRole('button', { name: 'Audit back' }).click()
  await view.getByRole('button', { name: 'Audit log' }).click()
  await view.getByRole('button', { name: 'Audit close' }).click()
  await view.getByRole('button', { name: 'Audit log' }).click()
  await view.getByRole('button', { name: 'Drawer close' }).click()
  await expect.element(view.getByText('Default Cash / Bank Account')).toBeVisible()
  await expect.element(view.getByText('Accounts Receivable Account')).toBeVisible()
  await expect.element(view.getByText('Service Revenue Account')).toBeVisible()
  await expect.element(view.getByText('Default Currency')).toBeVisible()
  expect(document.body.textContent ?? '').not.toContain('Project Time Ledger')
  expect(document.body.textContent ?? '').not.toContain('Unbilled Time Register')
  expect(document.body.textContent ?? '').not.toContain('Project Billing Status')

  const cashInput = view.getByLabelText('Default Cash / Bank Account').element() as HTMLInputElement
  cashInput.value = 'cash-200'
  cashInput.dispatchEvent(new Event('input', { bubbles: true }))
  await flushUi()

  ;(document.querySelector('button[title="Save"]') as HTMLButtonElement).click()
  await flushUi()

  expect(mocks.updateCatalog).toHaveBeenCalledWith('ab.accounting_policy', 'policy-1', {
    fields: expect.objectContaining({
      display: 'Default Agency Billing Policy',
      cash_account_id: 'cash-200',
      ar_account_id: 'ar-100',
      service_revenue_account_id: 'rev-100',
      default_currency: 'USD',
    }),
  })
  expect(mocks.toastPush).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Saved',
    message: 'Agency Billing accounting policy updated.',
    tone: 'success',
  }))
})

test('shows the empty state and applies defaults before reloading the policy', async () => {
  mocks.getCatalogPage
    .mockResolvedValueOnce({ items: [] })
    .mockResolvedValueOnce({
      items: [
        {
          id: 'policy-1',
          display: 'Accounting Policy',
          payload: {
            fields: reactive({
              display: 'Default Agency Billing Policy',
              cash_account_id: 'cash-100',
              ar_account_id: 'ar-100',
              service_revenue_account_id: 'rev-100',
              default_currency: 'USD',
            }),
          },
        },
      ],
    })
  mocks.httpPost.mockResolvedValue(undefined)

  const view = await render(AccountingPolicySettingsPage)
  await flushUi()

  await expect.element(view.getByTestId('agency-billing-accounting-policy-empty-state')).toBeVisible()

  document.querySelector<HTMLButtonElement>('button[title="Save"]')!
    .dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(mocks.updateCatalog).not.toHaveBeenCalled()

  await view.getByTestId('agency-billing-accounting-policy-empty-state').getByRole('button', { name: 'Apply defaults' }).click()
  await flushUi()
  await flushUi()

  expect(mocks.httpPost).toHaveBeenCalledWith('/api/admin/setup/apply-defaults')
  expect(mocks.toastPush).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Defaults applied',
    message: 'Agency Billing default configuration has been created or refreshed.',
    tone: 'success',
  }))
  await expect.element(view.getByTestId('agency-billing-accounting-policy-form')).toBeVisible()
})

test('handles sparse form metadata, an absent payload, and guarded share/audit actions without an id', async () => {
  mocks.ensureCatalogType.mockResolvedValue({
    form: {
      sections: [
        { title: 'Empty rows' },
        { title: 'Empty fields', rows: [{}] },
        { title: 'Other', rows: [{ fields: [
          { key: 'display', label: 'Hidden display' },
          { key: 'custom_setting', label: 'Custom Setting' },
        ] }] },
      ],
    },
  })
  mocks.getCatalogPage.mockResolvedValue({
    items: [{ id: '', display: null, payload: null }],
  })

  const view = await render(AccountingPolicySettingsPage)
  await flushUi()

  await expect.element(view.getByText('Custom Setting')).toBeVisible()
  expect(document.body.textContent).not.toContain('Hidden display')

  const share = view.getByTitle('Share link').element()
  share.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  const audit = view.getByRole('button', { name: 'Audit log' }).element()
  audit.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(mocks.copyAppLink).not.toHaveBeenCalled()
  expect(document.querySelector('[data-testid="drawer"]')).toBeNull()
})

test('renders no-form metadata and reports load, save, and defaults failures', async () => {
  mocks.ensureCatalogType.mockResolvedValue({ form: null })
  mocks.getCatalogPage.mockResolvedValue({
    items: [{ id: 'policy-no-form', display: 'Policy', payload: { fields: {} } }],
  })

  const noForm = await render(AccountingPolicySettingsPage)
  await flushUi()
  await expect.element(noForm.getByText('No form metadata available.')).toBeVisible()
  noForm.getByTitle('Save').element()
    .dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(mocks.updateCatalog).not.toHaveBeenCalled()
  noForm.unmount()

  mocks.ensureCatalogType.mockRejectedValueOnce(new Error('Metadata unavailable'))
  const loadFailure = await render(AccountingPolicySettingsPage)
  await flushUi()
  await expect.element(loadFailure.getByText('Metadata unavailable')).toBeVisible()
  loadFailure.unmount()

  mocks.ensureCatalogType.mockResolvedValue({ form: metadataForm() })
  mocks.getCatalogPage.mockResolvedValue({
    items: [{
      id: 'policy-1',
      display: 'Policy',
      payload: { fields: { display: 'Policy', cash_account_id: 'cash-1' } },
    }],
  })
  mocks.updateCatalog.mockRejectedValueOnce(new Error('Save rejected'))
  const saveFailure = await render(AccountingPolicySettingsPage)
  await flushUi()
  const cash = saveFailure.getByLabelText('Default Cash / Bank Account').element() as HTMLInputElement
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
  await defaultsFailure.getByTitle('Apply defaults').click()
  await flushUi()
  await expect.element(defaultsFailure.getByText('Defaults rejected')).toBeVisible()
})

test('normalizes a form with absent sections and a retained section without a title', async () => {
  mocks.ensureCatalogType
    .mockResolvedValueOnce({ form: {} })
    .mockResolvedValueOnce({
      form: {
        sections: [{
          rows: [{ fields: [{ key: 'custom_setting', label: 'Custom Setting' }] }],
        }],
      },
    })
  mocks.getCatalogPage.mockResolvedValue({
    items: [{ id: 'policy-1', payload: { fields: { custom_setting: 'enabled' } } }],
  })

  const emptyForm = await render(AccountingPolicySettingsPage)
  await flushUi()
  expect(emptyForm.getByTestId('agency-billing-accounting-policy-form').element()).not.toBeNull()
  emptyForm.unmount()

  const untitledForm = await render(AccountingPolicySettingsPage)
  await flushUi()
  await expect.element(untitledForm.getByText('Custom Setting')).toBeVisible()
})

test('ignores a second defaults request while initialization is in progress', async () => {
  let resolveDefaults!: () => void
  mocks.getCatalogPage.mockResolvedValue({ items: [] })
  mocks.httpPost.mockReturnValue(new Promise<void>((resolve) => {
    resolveDefaults = resolve
  }))

  const view = await render(AccountingPolicySettingsPage)
  await flushUi()
  const apply = view.getByTitle('Apply defaults').element()
  apply.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  apply.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(mocks.httpPost).toHaveBeenCalledTimes(1)

  resolveDefaults()
  await flushUi()
  await flushUi()
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
