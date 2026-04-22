import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { ApiError } from '../../../../src/ngb/api/http'
import {
  StubBadge,
  StubCheckbox,
  StubDatePicker,
  StubDrawer,
  StubEntityAuditSidebar,
  StubFormLayout,
  StubFormRow,
  StubFormSection,
  StubGeneralJournalEntryLinesEditor,
  StubIcon,
  StubInput,
  StubPageHeader,
  StubSelect,
  StubTabs,
  StubValidationSummary,
} from './stubs'

const gjeEditMocks = vi.hoisted(() => ({
  approve: vi.fn(),
  auth: {
    userName: 'QA Owner',
  },
  copyAppLink: vi.fn(),
  createDraft: vi.fn(),
  getAccountContext: vi.fn(),
  getEntry: vi.fn(),
  lookupState: {
    coaLabels: {
      'cash-id': '1100 Cash',
      'revenue-id': '4100 Revenue',
      'posted-cash-id': '1110 Posted Cash',
      'posted-accrual-id': '2100 Accrued Liability',
    } as Record<string, string>,
    documentLabels: {
      'general_journal_entry': {
        'source-doc-1': 'JE-0005',
        'gje-posted-1': 'JE-POSTED-1',
      },
    } as Record<string, Record<string, string>>,
  },
  lookupStore: {
    ensureAnyDocumentLabels: vi.fn(),
    ensureCatalogLabels: vi.fn(),
    ensureCoaLabels: vi.fn(),
    ensureDocumentLabels: vi.fn(),
    labelForAnyDocument: vi.fn(),
    labelForCatalog: vi.fn(),
    labelForCoa: vi.fn(),
    labelForDocument: vi.fn(),
  },
  markForDeletion: vi.fn(),
  navigateBack: vi.fn(),
  post: vi.fn(),
  reject: vi.fn(),
  replaceLines: vi.fn(),
  reverse: vi.fn(),
  submit: vi.fn(),
  toasts: {
    push: vi.fn(),
  },
  unmarkForDeletion: vi.fn(),
  updateHeader: vi.fn(),
}))

vi.mock('../../../../src/ngb/auth', () => ({
  useAuthStore: () => gjeEditMocks.auth,
}))

vi.mock('../../../../src/ngb/lookup/store', () => ({
  useLookupStore: () => gjeEditMocks.lookupStore,
}))

vi.mock('../../../../src/ngb/primitives/toast', () => ({
  useToasts: () => gjeEditMocks.toasts,
}))

vi.mock('../../../../src/ngb/router/backNavigation', () => ({
  navigateBack: gjeEditMocks.navigateBack,
}))

vi.mock('../../../../src/ngb/router/shareLink', () => ({
  copyAppLink: gjeEditMocks.copyAppLink,
}))

vi.mock('../../../../src/ngb/accounting/generalJournalEntryApi', () => ({
  approveGeneralJournalEntry: gjeEditMocks.approve,
  createGeneralJournalEntryDraft: gjeEditMocks.createDraft,
  getGeneralJournalEntry: gjeEditMocks.getEntry,
  getGeneralJournalEntryAccountContext: gjeEditMocks.getAccountContext,
  markGeneralJournalEntryForDeletion: gjeEditMocks.markForDeletion,
  postGeneralJournalEntry: gjeEditMocks.post,
  rejectGeneralJournalEntry: gjeEditMocks.reject,
  replaceGeneralJournalEntryLines: gjeEditMocks.replaceLines,
  reverseGeneralJournalEntry: gjeEditMocks.reverse,
  submitGeneralJournalEntry: gjeEditMocks.submit,
  unmarkGeneralJournalEntryForDeletion: gjeEditMocks.unmarkForDeletion,
  updateGeneralJournalEntryHeader: gjeEditMocks.updateHeader,
}))

vi.mock('../../../../src/ngb/components/NgbDrawer.vue', () => ({
  default: StubDrawer,
}))

vi.mock('../../../../src/ngb/components/forms/NgbFormLayout.vue', () => ({
  default: StubFormLayout,
}))

vi.mock('../../../../src/ngb/components/forms/NgbFormRow.vue', () => ({
  default: StubFormRow,
}))

vi.mock('../../../../src/ngb/components/forms/NgbFormSection.vue', () => ({
  default: StubFormSection,
}))

vi.mock('../../../../src/ngb/components/forms/NgbValidationSummary.vue', () => ({
  default: StubValidationSummary,
}))

vi.mock('../../../../src/ngb/editor/NgbEntityAuditSidebar.vue', () => ({
  default: StubEntityAuditSidebar,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/primitives/NgbCheckbox.vue', () => ({
  default: StubCheckbox,
}))

vi.mock('../../../../src/ngb/primitives/NgbDatePicker.vue', () => ({
  default: StubDatePicker,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/primitives/NgbInput.vue', () => ({
  default: StubInput,
}))

vi.mock('../../../../src/ngb/primitives/NgbTabs.vue', () => ({
  default: StubTabs,
}))

vi.mock('../../../../src/ngb/primitives/NgbSelect.vue', () => ({
  default: StubSelect,
}))

vi.mock('../../../../src/ngb/site/NgbPageHeader.vue', () => ({
  default: StubPageHeader,
}))

vi.mock('../../../../src/ngb/accounting/NgbGeneralJournalEntryLinesEditor.vue', () => ({
  default: StubGeneralJournalEntryLinesEditor,
}))

import NgbGeneralJournalEntryEditPage from '../../../../src/ngb/accounting/NgbGeneralJournalEntryEditPage.vue'

type DetailsOverrides = {
  id?: string
  number?: string
  display?: string | null
  status?: number
  source?: number
  approvalState?: number
  isMarkedForDeletion?: boolean
  reversalOfDocumentId?: string | null
  reversalOfDocumentDisplay?: string | null
  rejectReason?: string | null
  dateUtc?: string
  lines?: Array<{
    lineNo: number
    side: number
    accountId: string
    accountDisplay?: string | null
    amount: number
    memo?: string | null
    dimensionSetId?: string
    dimensions?: Array<{
      dimensionId: string
      valueId: string
      display?: string | null
    }>
  }>
  accountContexts?: Array<{
    accountId: string
    code: string
    name: string
    dimensionRules: Array<{
      dimensionId: string
      dimensionCode: string
      ordinal: number
      isRequired: boolean
      lookup?: {
        kind: 'catalog'
        catalogType: string
      } | {
        kind: 'document'
        documentTypes: string[]
      } | {
        kind: 'coa'
      } | null
    }>
  }>
  allocations?: Array<{
    entryNo: number
    debitLineNo: number
    creditLineNo: number
    amount: number
  }>
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

function makeDetails(overrides: DetailsOverrides = {}) {
  const id = overrides.id ?? 'gje-1'

  return {
    document: {
      id,
      display: overrides.display ?? null,
      status: overrides.status ?? 1,
      isMarkedForDeletion: overrides.isMarkedForDeletion ?? false,
      number: overrides.number ?? 'JE-001',
    },
    dateUtc: overrides.dateUtc ?? '2026-04-15T12:00:00Z',
    header: {
      journalType: 1,
      source: overrides.source ?? 1,
      approvalState: overrides.approvalState ?? 1,
      reasonCode: 'MONTH_CLOSE',
      memo: 'Default memo',
      externalReference: 'EXT-0',
      autoReverse: false,
      autoReverseOnUtc: null,
      reversalOfDocumentId: overrides.reversalOfDocumentId ?? null,
      reversalOfDocumentDisplay: overrides.reversalOfDocumentDisplay ?? null,
      initiatedBy: 'Olivia Initiator',
      initiatedAtUtc: '2026-04-15T12:00:00Z',
      submittedBy: overrides.approvalState && overrides.approvalState >= 2 ? 'Sam Submitter' : null,
      submittedAtUtc: overrides.approvalState && overrides.approvalState >= 2 ? '2026-04-15T12:05:00Z' : null,
      approvedBy: overrides.approvalState && overrides.approvalState >= 3 ? 'Ava Approver' : null,
      approvedAtUtc: overrides.approvalState && overrides.approvalState >= 3 ? '2026-04-15T12:10:00Z' : null,
      rejectedBy: overrides.approvalState === 4 ? 'Riley Reviewer' : null,
      rejectedAtUtc: overrides.approvalState === 4 ? '2026-04-15T12:10:00Z' : null,
      rejectReason: overrides.rejectReason ?? null,
      postedBy: overrides.status === 2 ? 'Paula Poster' : null,
      postedAtUtc: overrides.status === 2 ? '2026-04-15T12:20:00Z' : null,
      createdAtUtc: '2026-04-15T12:00:00Z',
      updatedAtUtc: '2026-04-15T12:00:00Z',
    },
    lines: overrides.lines ?? [
      {
        lineNo: 1,
        side: 1,
        accountId: 'cash-id',
        accountDisplay: '1100 — Cash',
        amount: 1250.5,
        memo: 'Cash debit',
        dimensionSetId: 'set-1',
        dimensions: [],
      },
      {
        lineNo: 2,
        side: 2,
        accountId: 'revenue-id',
        accountDisplay: '4100 — Revenue',
        amount: 1250.5,
        memo: 'Revenue credit',
        dimensionSetId: 'set-2',
        dimensions: [],
      },
    ],
    accountContexts: overrides.accountContexts ?? [
      {
        accountId: 'cash-id',
        code: '1100',
        name: 'Cash',
        dimensionRules: [],
      },
      {
        accountId: 'revenue-id',
        code: '4100',
        name: 'Revenue',
        dimensionRules: [],
      },
    ],
    allocations: overrides.allocations ?? [],
  }
}

function makeApiError(options: {
  errorCode?: string
  context?: Record<string, unknown>
  issues?: Array<{ message?: string }>
  errors?: Record<string, string[]>
  message?: string
}) {
  return new ApiError({
    message: options.message ?? 'Request failed.',
    status: 400,
    url: '/api/accounting/general-journal-entry',
    body: {
      errorCode: options.errorCode,
      context: options.context,
      issues: options.issues,
      errors: options.errors,
    },
  })
}

async function flushUi() {
  await new Promise((resolve) => window.setTimeout(resolve, 60))
}

function queryButtonByTitle(title: string): HTMLButtonElement | null {
  const node = document.querySelector(`button[title="${title}"]`)
  return node instanceof HTMLButtonElement ? node : null
}

function clickButtonByTitle(title: string) {
  const button = queryButtonByTitle(title)
  if (!button) throw new Error(`Button with title "${title}" not found.`)
  button.click()
}

function setDateInput(index: number, value: string) {
  const input = document.querySelectorAll('input[type="date"]')[index]
  if (!(input instanceof HTMLInputElement)) throw new Error(`Date input ${index} not found.`)
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  input.dispatchEvent(new Event('change', { bubbles: true }))
}

function setCheckbox(index: number, checked: boolean) {
  const input = document.querySelectorAll('input[type="checkbox"]')[index]
  if (!(input instanceof HTMLInputElement)) throw new Error(`Checkbox ${index} not found.`)
  input.checked = checked
  input.dispatchEvent(new Event('input', { bubbles: true }))
  input.dispatchEvent(new Event('change', { bubbles: true }))
}

async function renderPage(initialUrl: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/accounting/general-journal-entries/new',
        component: NgbGeneralJournalEntryEditPage,
      },
      {
        path: '/accounting/general-journal-entries/:id',
        component: NgbGeneralJournalEntryEditPage,
      },
      {
        path: '/dashboard',
        component: {
          template: '<div>Dashboard</div>',
        },
      },
    ],
  })

  await router.push(initialUrl)
  await router.isReady()

  const view = await render(NgbGeneralJournalEntryEditPage, {
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

  gjeEditMocks.lookupStore.ensureCoaLabels.mockResolvedValue(undefined)
  gjeEditMocks.lookupStore.ensureDocumentLabels.mockResolvedValue(undefined)
  gjeEditMocks.lookupStore.ensureAnyDocumentLabels.mockResolvedValue(undefined)
  gjeEditMocks.lookupStore.ensureCatalogLabels.mockResolvedValue(undefined)
  gjeEditMocks.lookupStore.labelForCatalog.mockImplementation((catalogType: string, id: string) => `${catalogType}:${id}`)
  gjeEditMocks.lookupStore.labelForAnyDocument.mockImplementation((documentTypes: string[], id: string) =>
    gjeEditMocks.lookupState.documentLabels[documentTypes[0] ?? '']?.[id] ?? id,
  )
  gjeEditMocks.lookupStore.labelForCoa.mockImplementation((id: string) =>
    gjeEditMocks.lookupState.coaLabels[id] ?? id,
  )
  gjeEditMocks.lookupStore.labelForDocument.mockImplementation((documentType: string, id: string) =>
    gjeEditMocks.lookupState.documentLabels[documentType]?.[id] ?? id,
  )

  gjeEditMocks.copyAppLink.mockResolvedValue(true)

  gjeEditMocks.getAccountContext.mockImplementation(async (accountId: string) => ({
    accountId,
    code: accountId,
    name: gjeEditMocks.lookupState.coaLabels[accountId] ?? accountId,
    dimensionRules: [],
  }))

  gjeEditMocks.getEntry.mockResolvedValue(clone(makeDetails()))
  gjeEditMocks.createDraft.mockResolvedValue(clone(makeDetails({
    id: 'gje-created',
    number: 'JE-100',
  })))
  gjeEditMocks.updateHeader.mockResolvedValue(clone(makeDetails({
    id: 'gje-created',
    number: 'JE-100',
  })))
  gjeEditMocks.replaceLines.mockResolvedValue(clone(makeDetails({
    id: 'gje-created',
    number: 'JE-100',
    lines: [
      {
        lineNo: 1,
        side: 1,
        accountId: 'cash-id',
        amount: 1250.5,
        memo: 'Debit leg',
        dimensionSetId: 'set-1',
        dimensions: [],
      },
      {
        lineNo: 2,
        side: 2,
        accountId: 'revenue-id',
        amount: 1250.5,
        memo: 'Credit leg',
        dimensionSetId: 'set-2',
        dimensions: [],
      },
    ],
  })))
})

test('creates a new draft, saves header and lines, and replaces the route with the new entry id', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderPage('/accounting/general-journal-entries/new')

  setDateInput(0, '2026-04-10')
  await view.getByPlaceholder('Optional business reason code').fill('APR_CLOSE')
  await view.getByPlaceholder('Explain the journal entry').fill('April close adjustments')
  await view.getByPlaceholder('External ticket, import id, or source ref').fill('EXT-42')
  setCheckbox(0, true)
  setDateInput(1, '2026-04-30')
  await view.getByRole('button', { name: 'Set sample lines' }).click()
  clickButtonByTitle('Save')
  await flushUi()

  expect(gjeEditMocks.createDraft).toHaveBeenCalledWith({
    dateUtc: '2026-04-10T12:00:00Z',
  })
  expect(gjeEditMocks.updateHeader).toHaveBeenCalledWith('gje-created', {
    updatedBy: 'QA Owner',
    journalType: 1,
    reasonCode: 'APR_CLOSE',
    memo: 'April close adjustments',
    externalReference: 'EXT-42',
    autoReverse: true,
    autoReverseOnUtc: '2026-04-30',
  })
  expect(gjeEditMocks.replaceLines).toHaveBeenCalledWith('gje-created', {
    updatedBy: 'QA Owner',
    lines: [
      {
        side: 1,
        accountId: 'cash-id',
        amount: 1250.5,
        memo: 'Debit leg',
        dimensions: [],
      },
      {
        side: 2,
        accountId: 'revenue-id',
        amount: 1250.5,
        memo: 'Credit leg',
        dimensions: [],
      },
    ],
  })
  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/gje-created')
  expect(gjeEditMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Saved',
    tone: 'success',
  }))
  await expect.element(view.getByText('General Journal Entry JE-100')).toBeVisible()
})

test('loads an existing entry, hydrates lookup labels, and wires share, audit, back, and deletion actions', async () => {
  await page.viewport(1280, 900)

  gjeEditMocks.getEntry.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-10',
    number: 'JE-010',
    reversalOfDocumentId: 'source-doc-1',
    reversalOfDocumentDisplay: 'General Journal Entry JE-0005 4/14/2026',
    allocations: [
      {
        entryNo: 100,
        debitLineNo: 1,
        creditLineNo: 2,
        amount: 1250.5,
      },
    ],
  })))
  gjeEditMocks.markForDeletion.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-10',
    number: 'JE-010',
    reversalOfDocumentId: 'source-doc-1',
    reversalOfDocumentDisplay: 'General Journal Entry JE-0005 4/14/2026',
    isMarkedForDeletion: true,
  })))
  gjeEditMocks.unmarkForDeletion.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-10',
    number: 'JE-010',
    reversalOfDocumentId: 'source-doc-1',
    reversalOfDocumentDisplay: 'General Journal Entry JE-0005 4/14/2026',
    isMarkedForDeletion: false,
  })))

  const { router, view } = await renderPage('/accounting/general-journal-entries/gje-10')

  expect(gjeEditMocks.getEntry).toHaveBeenCalledTimes(1)
  expect(gjeEditMocks.getEntry).toHaveBeenCalledWith('gje-10')
  expect(gjeEditMocks.lookupStore.ensureCoaLabels).not.toHaveBeenCalled()
  expect(gjeEditMocks.lookupStore.ensureDocumentLabels).not.toHaveBeenCalled()
  expect(gjeEditMocks.getAccountContext).not.toHaveBeenCalled()

  await expect.element(view.getByText('Reversal Of: General Journal Entry JE-0005 4/14/2026')).toBeVisible()
  expect(view.getByTestId('gje-line-0').element().textContent ?? '').toContain('1100 — Cash')
  await expect.element(view.getByText('Allocations')).toBeVisible()

  clickButtonByTitle('Share link')
  expect(gjeEditMocks.copyAppLink).toHaveBeenCalledWith(
    router,
    gjeEditMocks.toasts,
    '/accounting/general-journal-entries/gje-10',
  )

  clickButtonByTitle('Audit log')
  await expect.element(view.getByText('Audit entity: General Journal Entry JE-010')).toBeVisible()
  await view.getByRole('button', { name: 'Audit close' }).click()
  await flushUi()

  clickButtonByTitle('Mark for deletion')
  await flushUi()
  await expect.element(view.getByText('This document is marked for deletion. Restore it to edit or continue the workflow.')).toBeVisible()
  expect(gjeEditMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Marked for deletion',
    tone: 'warn',
  }))

  clickButtonByTitle('Unmark for deletion')
  await flushUi()
  expect(gjeEditMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Restored',
    tone: 'success',
  }))

  await view.getByRole('button', { name: 'Header back' }).click()
  expect(gjeEditMocks.navigateBack).toHaveBeenCalledTimes(1)
  expect(gjeEditMocks.navigateBack.mock.calls[0]?.[2]).toBe('/accounting/general-journal-entries')
})

test('saves draft changes before submit, then advances through approve and post workflow states', async () => {
  await page.viewport(1280, 900)

  gjeEditMocks.getEntry.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-20',
    number: 'JE-020',
    status: 1,
    approvalState: 1,
  })))
  gjeEditMocks.updateHeader.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-20',
    number: 'JE-020',
    status: 1,
    approvalState: 1,
  })))
  gjeEditMocks.replaceLines.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-20',
    number: 'JE-020',
    status: 1,
    approvalState: 1,
  })))
  gjeEditMocks.submit.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-20',
    number: 'JE-020',
    status: 1,
    approvalState: 2,
  })))
  gjeEditMocks.approve.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-20',
    number: 'JE-020',
    status: 1,
    approvalState: 3,
  })))
  gjeEditMocks.post.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-20',
    number: 'JE-020',
    status: 2,
    approvalState: 3,
  })))

  const { view } = await renderPage('/accounting/general-journal-entries/gje-20')

  await view.getByPlaceholder('Explain the journal entry').fill('Ready for workflow')
  clickButtonByTitle('Submit')
  await flushUi()

  expect(gjeEditMocks.updateHeader).toHaveBeenCalledWith('gje-20', expect.objectContaining({
    memo: 'Ready for workflow',
    updatedBy: 'QA Owner',
  }))
  expect(gjeEditMocks.replaceLines).toHaveBeenCalledWith('gje-20', expect.objectContaining({
    updatedBy: 'QA Owner',
    lines: [
      {
        side: 1,
        accountId: 'cash-id',
        amount: 1250.5,
        memo: 'Cash debit',
        dimensions: [],
      },
      {
        side: 2,
        accountId: 'revenue-id',
        amount: 1250.5,
        memo: 'Revenue credit',
        dimensions: [],
      },
    ],
  }))
  expect(gjeEditMocks.submit).toHaveBeenCalledWith('gje-20', {})
  expect(gjeEditMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Submitted',
    tone: 'success',
  }))
  expect(queryButtonByTitle('Approve')).not.toBeNull()

  clickButtonByTitle('Approve')
  await flushUi()
  expect(gjeEditMocks.approve).toHaveBeenCalledWith('gje-20', {})
  expect(gjeEditMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Approved',
    tone: 'success',
  }))
  expect(queryButtonByTitle('Post')).not.toBeNull()

  clickButtonByTitle('Post')
  await flushUi()
  expect(gjeEditMocks.post).toHaveBeenCalledWith('gje-20', {})
  expect(gjeEditMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Posted',
    tone: 'success',
  }))
  expect(queryButtonByTitle('Reverse')).not.toBeNull()
})

test('reverses a posted journal entry with workflow inputs and replaces the route to the reversal entry', async () => {
  await page.viewport(1280, 900)

  gjeEditMocks.getEntry.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-posted-1',
    number: 'JE-POSTED-1',
    status: 2,
    approvalState: 3,
    lines: [
      {
        lineNo: 1,
        side: 1,
        accountId: 'posted-cash-id',
        amount: 500,
        memo: 'Posted cash',
        dimensionSetId: 'set-1',
        dimensions: [],
      },
      {
        lineNo: 2,
        side: 2,
        accountId: 'posted-accrual-id',
        amount: 500,
        memo: 'Posted accrual',
        dimensionSetId: 'set-2',
        dimensions: [],
      },
    ],
  })))
  gjeEditMocks.reverse.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-reversal-1',
    number: 'JE-REV-1',
    status: 1,
    approvalState: 1,
    reversalOfDocumentId: 'gje-posted-1',
    reversalOfDocumentDisplay: 'General Journal Entry JE-POSTED-1 4/15/2026',
  })))

  const { router, view } = await renderPage('/accounting/general-journal-entries/gje-posted-1')

  await view.getByRole('button', { name: 'Workflow' }).click()
  setDateInput(2, '2026-05-20')
  setCheckbox(1, false)
  clickButtonByTitle('Reverse')
  await flushUi()

  expect(gjeEditMocks.reverse).toHaveBeenCalledWith('gje-posted-1', {
    reversalDateUtc: '2026-05-20T12:00:00Z',
    postImmediately: false,
  })
  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/gje-reversal-1')
  expect(gjeEditMocks.getEntry).toHaveBeenCalledTimes(1)
  expect(gjeEditMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Reversed',
    tone: 'success',
  }))
  await expect.element(view.getByText('Reversal Of: General Journal Entry JE-POSTED-1 4/15/2026')).toBeVisible()
})

test('surfaces validation summary errors and keeps the route stable when submit cannot save the draft first', async () => {
  await page.viewport(1280, 900)

  gjeEditMocks.getEntry.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-30',
    number: 'JE-030',
    status: 1,
    approvalState: 1,
  })))
  gjeEditMocks.updateHeader.mockRejectedValueOnce(makeApiError({
    errorCode: 'gje.business_field.required',
    context: {
      field: 'Memo',
    },
    issues: [{ message: 'Provide memo before submit.' }],
  }))

  const { router, view } = await renderPage('/accounting/general-journal-entries/gje-30')

  clickButtonByTitle('Submit')
  await flushUi()

  expect(gjeEditMocks.submit).not.toHaveBeenCalled()
  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/gje-30')
  await expect.element(view.getByText('Memo is required.')).toBeVisible()
  await expect.element(view.getByText('Provide memo before submit.')).toBeVisible()
  expect(queryButtonByTitle('Submit')?.disabled).toBe(false)
})

test('maps submit failures into friendly validation text and re-enables the workflow action', async () => {
  await page.viewport(1280, 900)

  gjeEditMocks.getEntry.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-31',
    number: 'JE-031',
    status: 1,
    approvalState: 1,
  })))
  gjeEditMocks.updateHeader.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-31',
    number: 'JE-031',
    status: 1,
    approvalState: 1,
  })))
  gjeEditMocks.replaceLines.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-31',
    number: 'JE-031',
    status: 1,
    approvalState: 1,
  })))
  gjeEditMocks.submit.mockRejectedValueOnce(makeApiError({
    errorCode: 'gje.lines.unbalanced',
    context: {
      debit: 1250.5,
      credit: 500,
    },
  }))

  const { router, view } = await renderPage('/accounting/general-journal-entries/gje-31')

  clickButtonByTitle('Submit')
  await flushUi()

  expect(gjeEditMocks.submit).toHaveBeenCalledWith('gje-31', {})
  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/gje-31')
  await expect.element(view.getByText('Journal is unbalanced. Debit 1,250.50 vs Credit 500.00.')).toBeVisible()
  expect(queryButtonByTitle('Submit')?.disabled).toBe(false)
})

test('keeps approve and reject actions usable after workflow failures', async () => {
  await page.viewport(1280, 900)

  gjeEditMocks.getEntry.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-32',
    number: 'JE-032',
    status: 1,
    approvalState: 2,
  })))
  gjeEditMocks.approve.mockRejectedValueOnce(makeApiError({
    issues: [{ message: 'Approval quorum is missing.' }],
  }))
  gjeEditMocks.reject.mockRejectedValueOnce(makeApiError({
    errors: {
      rejectReason: ['Reject reason is too long.'],
    },
  }))

  const { router, view } = await renderPage('/accounting/general-journal-entries/gje-32')

  clickButtonByTitle('Approve')
  await flushUi()

  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/gje-32')
  await expect.element(view.getByText('Approval quorum is missing.')).toBeVisible()
  expect(queryButtonByTitle('Approve')?.disabled).toBe(false)

  await view.getByRole('button', { name: 'Workflow' }).click()
  await view.getByPlaceholder('Required when rejecting').fill('Needs more detail before approval')
  clickButtonByTitle('Reject')
  await flushUi()

  await expect.element(view.getByText('Reject reason is too long.')).toBeVisible()
  expect(queryButtonByTitle('Reject')?.disabled).toBe(false)
})

test('shows posting failures in the validation summary and restores the post action state', async () => {
  await page.viewport(1280, 900)

  gjeEditMocks.getEntry.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-33',
    number: 'JE-033',
    status: 1,
    approvalState: 3,
  })))
  gjeEditMocks.post.mockRejectedValueOnce(makeApiError({
    errors: {
      posting: ['Posting period is locked.'],
    },
  }))

  const { router, view } = await renderPage('/accounting/general-journal-entries/gje-33')

  clickButtonByTitle('Post')
  await flushUi()

  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/gje-33')
  await expect.element(view.getByText('Posting period is locked.')).toBeVisible()
  expect(queryButtonByTitle('Post')?.disabled).toBe(false)
})

test('keeps the current route and reverse action available when reversal fails', async () => {
  await page.viewport(1280, 900)

  gjeEditMocks.getEntry.mockResolvedValueOnce(clone(makeDetails({
    id: 'gje-posted-2',
    number: 'JE-POSTED-2',
    status: 2,
    approvalState: 3,
  })))
  gjeEditMocks.reverse.mockRejectedValueOnce(makeApiError({
    errorCode: 'gje.line.dimensions.invalid',
    context: {
      lineNo: 2,
      accountCode: '2100',
      reason: 'missing_required_dimension',
    },
  }))

  const { router, view } = await renderPage('/accounting/general-journal-entries/gje-posted-2')

  await view.getByRole('button', { name: 'Workflow' }).click()
  clickButtonByTitle('Reverse')
  await flushUi()

  expect(router.currentRoute.value.fullPath).toBe('/accounting/general-journal-entries/gje-posted-2')
  await expect.element(view.getByText('Line 2 has invalid dimensions. Account: 2100. Reason: missing required dimension.')).toBeVisible()
  expect(queryButtonByTitle('Reverse')?.disabled).toBe(false)
})
