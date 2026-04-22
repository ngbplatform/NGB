import { page } from 'vitest/browser'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { reactive } from 'vue'

import { encodeBackTarget, withBackTarget } from '../../../../src/ngb/router/backNavigation'
import { shortGuid } from '../../../../src/ngb/utils/guid'
import {
  StubBadge,
  StubIcon,
  StubPageHeader,
  StubRegisterGrid,
  StubTabs,
} from './stubs'

const mocks = vi.hoisted(() => ({
  route: null as unknown as {
    params: {
      documentType: string
      id: string
    }
    query: Record<string, unknown>
    fullPath: string
  },
  router: {
    push: vi.fn(),
    replace: vi.fn(),
    back: vi.fn(),
  },
  metadataStore: {
    ensureDocumentType: vi.fn(),
  },
  toasts: {
    push: vi.fn(),
  },
  editorConfig: {
    lookupStore: null,
    loadDocumentById: vi.fn(),
    loadDocumentEffects: vi.fn(),
  },
  effectsBehavior: {},
  copyAppLink: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => mocks.route,
  useRouter: () => mocks.router,
}))

vi.mock('../../../../src/ngb/metadata/store', () => ({
  useMetadataStore: () => mocks.metadataStore,
}))

vi.mock('../../../../src/ngb/primitives/toast', () => ({
  useToasts: () => mocks.toasts,
}))

vi.mock('../../../../src/ngb/editor/config', async () => {
  const actual = await vi.importActual('../../../../src/ngb/editor/config')
  return {
    ...actual,
    getConfiguredNgbEditor: () => mocks.editorConfig,
    resolveNgbEditorEffectsBehavior: () => mocks.effectsBehavior,
  }
})

vi.mock('../../../../src/ngb/router/shareLink', () => ({
  copyAppLink: mocks.copyAppLink,
}))

vi.mock('../../../../src/ngb/site/NgbPageHeader.vue', () => ({
  default: StubPageHeader,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/primitives/NgbTabs.vue', () => ({
  default: StubTabs,
}))

vi.mock('../../../../src/ngb/components/register/NgbRegisterGrid.vue', () => ({
  default: StubRegisterGrid,
}))

import NgbDocumentEffectsPage from '../../../../src/ngb/editor/NgbDocumentEffectsPage.vue'

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
  await Promise.resolve()
  await Promise.resolve()
}

describe('NgbDocumentEffectsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    mocks.route = reactive({
      params: {
        documentType: 'pm.invoice',
        id: 'doc-1',
      },
      query: {},
      fullPath: '/documents/pm.invoice/doc-1/effects',
    })

    mocks.metadataStore.ensureDocumentType.mockResolvedValue({
      documentType: 'pm.invoice',
      displayName: 'Customer Invoice',
      kind: 2,
    })
    mocks.editorConfig.lookupStore = null
    mocks.effectsBehavior = {}
    mocks.editorConfig.loadDocumentById.mockImplementation(async (_documentType: string, id: string) => ({
      id,
      display: id === 'doc-2' ? 'Invoice INV-002' : 'Invoice INV-001',
      status: 2,
      payload: {
        fields: {},
        parts: null,
      },
    }))
    mocks.editorConfig.loadDocumentEffects.mockResolvedValue({
      accountingEntries: [
        {
          entryId: 'entry-1',
          occurredAtUtc: '2026-04-08T12:30:00.000Z',
          debitAccount: {
            accountId: 'cash',
            code: '1100',
            name: 'Cash',
          },
          creditAccount: {
            accountId: 'rent',
            code: '4100',
            name: 'Rent Revenue',
          },
          amount: 1250,
          debitDimensions: [{ dimensionId: 'property', valueId: 'p-1', display: 'Riverfront Tower' }],
          creditDimensions: [{ dimensionId: 'property', valueId: 'p-1', display: 'Riverfront Tower' }],
        },
      ],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [],
    })
  })

  it('renders accounting effects and routes header actions back to the source document', async () => {
    await page.viewport(1280, 900)

    const view = await render(NgbDocumentEffectsPage)
    const debitRow = view.getByTestId('register-row-entry-1:debit')

    await expect.element(view.getByText('Invoice INV-001')).toBeVisible()
    await expect.element(view.getByText('Accounting Entries (1)')).toBeVisible()
    await expect.element(debitRow).toBeVisible()
    expect(debitRow.element().textContent ?? '').toMatch(/Cash/)
    expect(debitRow.element().textContent ?? '').toMatch(/1,250.00/)

    await view.getByRole('button', { name: 'Open document' }).click()
    expect(mocks.router.push).toHaveBeenCalledWith(
      withBackTarget('/documents/pm.invoice/doc-1', mocks.route.fullPath),
    )

    await view.getByRole('button', { name: 'Share link' }).click()
    expect(mocks.copyAppLink).toHaveBeenCalledWith(
      mocks.router,
      mocks.toasts,
      '/documents/pm.invoice/doc-1/effects',
      {
        title: 'Effects link copied',
        message: 'Shareable effects page link copied to clipboard.',
      },
    )

    await debitRow.click()
    expect(mocks.router.push).toHaveBeenCalledTimes(2)
  })

  it('renders UTC-midnight occurred timestamps as the same UTC calendar date without a previous-day local shift', async () => {
    await page.viewport(1280, 900)

    const occurredAtUtc = '2026-04-07T00:00:00.000Z'
    const expectedRenderedDate = new Date(occurredAtUtc).toLocaleDateString(undefined, {
      timeZone: 'UTC',
    })

    mocks.editorConfig.loadDocumentEffects.mockResolvedValueOnce({
      accountingEntries: [
        {
          entryId: 'entry-midnight',
          occurredAtUtc,
          debitAccount: {
            accountId: 'ar',
            code: '1100',
            name: 'Accounts Receivable',
          },
          creditAccount: {
            accountId: 'misc',
            code: '4050',
            name: 'Miscellaneous Income',
          },
          amount: 333,
          debitDimensions: [],
          creditDimensions: [],
        },
      ],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [],
    })

    const view = await render(NgbDocumentEffectsPage)
    const debitRow = view.getByTestId('register-row-entry-midnight:debit')

    await expect.element(debitRow).toBeVisible()

    const rowText = debitRow.element().textContent ?? ''
    expect(rowText).toContain(expectedRenderedDate)
    expect(rowText).not.toMatch(/8:00(?::00)?\s*PM/i)
  })

  it('shows an error state when the effects snapshot cannot be loaded', async () => {
    mocks.editorConfig.loadDocumentEffects.mockRejectedValueOnce(new Error('boom'))

    const view = await render(NgbDocumentEffectsPage)

    await expect.element(view.getByText('boom')).toBeVisible()
  })

  it('keeps document effects visible when ancillary label prefetch fails and falls back to short labels', async () => {
    await page.viewport(1280, 900)

    const debitAccountId = '11111111-1111-1111-1111-111111111111'
    const creditAccountId = '22222222-2222-2222-2222-222222222222'
    const prefetchRelatedLabels = vi.fn().mockRejectedValue(new Error('Related labels offline'))

    mocks.editorConfig.lookupStore = {
      ensureCoaLabels: vi.fn().mockRejectedValue(new Error('COA labels offline')),
      labelForCoa: vi.fn((id: unknown) => shortGuid(String(id ?? ''))),
    }
    mocks.effectsBehavior = {
      prefetchRelatedLabels,
    }
    mocks.editorConfig.loadDocumentEffects.mockResolvedValueOnce({
      accountingEntries: [
        {
          entryId: 'entry-prefetch',
          occurredAtUtc: '2026-04-08T12:30:00.000Z',
          debitAccount: null,
          debitAccountId,
          creditAccount: null,
          creditAccountId,
          amount: 1250,
          debitDimensions: [],
          creditDimensions: [],
        },
      ],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [],
    })

    const view = await render(NgbDocumentEffectsPage)
    const debitRow = view.getByTestId('register-row-entry-prefetch:debit')
    const creditRow = view.getByTestId('register-row-entry-prefetch:credit')

    await expect.element(view.getByText('Invoice INV-001')).toBeVisible()
    await expect.element(debitRow).toBeVisible()
    await expect.element(creditRow).toBeVisible()
    expect(debitRow.element().textContent ?? '').toContain(shortGuid(debitAccountId))
    expect(creditRow.element().textContent ?? '').toContain(shortGuid(creditAccountId))
    expect(document.body.textContent).not.toContain('COA labels offline')
    expect(document.body.textContent).not.toContain('Related labels offline')
    expect(mocks.editorConfig.lookupStore.ensureCoaLabels).toHaveBeenCalledWith([debitAccountId, creditAccountId])
    expect(prefetchRelatedLabels).toHaveBeenCalledTimes(1)
  })

  it('returns to the source document route with its outer report back trail intact', async () => {
    await page.viewport(1280, 900)

    const reportBackTarget = '/reports/pm.occupancy.summary?variant=audit-view'
    const nestedDocumentRoute = withBackTarget('/documents/pm.invoice/doc-1', reportBackTarget)
    const encodedBack = encodeBackTarget(nestedDocumentRoute)

    mocks.route.query = {
      back: encodedBack,
    }
    mocks.route.fullPath = `/documents/pm.invoice/doc-1/effects?back=${encodedBack}`

    const view = await render(NgbDocumentEffectsPage)

    await expect.element(view.getByText('Invoice INV-001')).toBeVisible()
    await view.getByRole('button', { name: 'Open document' }).click()

    expect(mocks.router.push).toHaveBeenCalledWith(nestedDocumentRoute)
  })

  it('unwraps a compact-source target from nested full-page back trails for both back and edit actions', async () => {
    await page.viewport(1280, 900)

    const compactSource = '/documents/pm.invoice?search=late&panel=edit&id=doc-1&trash=deleted'
    const nestedDocumentRoute = withBackTarget('/documents/pm.invoice/doc-1', compactSource)
    const encodedBack = encodeBackTarget(nestedDocumentRoute)

    mocks.route.query = {
      back: encodedBack,
    }
    mocks.route.fullPath = `/documents/pm.invoice/doc-1/effects?back=${encodedBack}`

    const view = await render(NgbDocumentEffectsPage)

    await expect.element(view.getByText('Invoice INV-001')).toBeVisible()
    await view.getByRole('button', { name: 'Back' }).click()
    await view.getByRole('button', { name: 'Open document' }).click()

    expect(mocks.router.replace).toHaveBeenCalledWith(compactSource)
    expect(mocks.router.push).toHaveBeenCalledWith(compactSource)
  })

  it('renders operational and reference tabs, keeps partial empty states visible, and prefetches related labels once per snapshot', async () => {
    await page.viewport(1280, 900)

    const prefetchRelatedLabels = vi.fn().mockResolvedValue(undefined)
    mocks.effectsBehavior = {
      prefetchRelatedLabels,
    }
    mocks.editorConfig.loadDocumentEffects.mockResolvedValueOnce({
      accountingEntries: [],
      operationalRegisterMovements: [
        {
          movementId: 'move-1',
          occurredAtUtc: '2026-04-09T08:00:00.000Z',
          registerName: 'Rent Roll',
          registerCode: 'rent_roll',
          dimensionSetId: null,
          dimensions: [{ dimensionId: 'property_id', valueId: 'property-1', display: 'Riverfront Tower' }],
          resources: [
            { code: 'units', value: 12 },
            { code: 'amount', value: 450 },
          ],
        },
      ],
      referenceRegisterWrites: [
        {
          recordId: 'ref-1',
          recordedAtUtc: '2026-04-09T09:00:00.000Z',
          registerName: 'Lease Attributes',
          registerCode: 'lease_attributes',
          isTombstone: true,
          dimensionSetId: 'dimension-set-1',
          dimensions: [],
          fields: {
            lease_status: 'terminated',
          },
        },
      ],
    })

    const view = await render(NgbDocumentEffectsPage)

    await expect.element(view.getByText('Accounting Entries (0)')).toBeVisible()
    await expect.element(view.getByText('Operational Registers (1)')).toBeVisible()
    await expect.element(view.getByText('Reference Registers (1)')).toBeVisible()
    await expect.element(view.getByText('No accounting entries were returned for this document.')).toBeVisible()
    expect(prefetchRelatedLabels).toHaveBeenCalledTimes(1)

    await view.getByRole('button', { name: 'Operational Registers (1)' }).click()
    await expect.element(view.getByTestId('register-row-move-1')).toBeVisible()

    await view.getByRole('button', { name: 'Reference Registers (1)' }).click()
    await expect.element(view.getByTestId('register-row-ref-1')).toBeVisible()

    await view.getByTestId('register-row-ref-1').click()
    expect(mocks.router.push).toHaveBeenCalledWith(
      withBackTarget('/documents/pm.invoice/doc-1', mocks.route.fullPath),
    )
  })

  it('renders resolved reference-register document fields via the effects behavior hook', async () => {
    await page.viewport(1280, 900)

    mocks.effectsBehavior = {
      resolveFieldValue: ({ fieldKey, value, documentId, document }) => {
        if (fieldKey !== 'source_document_id') return null
        if (value === documentId) return document?.display ?? '—'
        return `Resolved ${String(value ?? '')}`
      },
    }
    mocks.editorConfig.loadDocumentEffects.mockResolvedValueOnce({
      accountingEntries: [],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [
        {
          recordId: 'ref-doc',
          recordedAtUtc: '2026-04-09T09:00:00.000Z',
          registerName: 'Lease Attributes',
          registerCode: 'lease_attributes',
          isTombstone: false,
          dimensionSetId: null,
          dimensions: [],
          fields: {
            source_document_id: 'doc-1',
            updated_at_utc: '2026-04-09T09:00:00.000Z',
          },
        },
      ],
    })

    const view = await render(NgbDocumentEffectsPage)

    await view.getByRole('button', { name: 'Reference Registers (1)' }).click()
    const row = view.getByTestId('register-row-ref-doc')

    await expect.element(row).toBeVisible()
    expect(row.element().textContent ?? '').toContain('Source Document: Invoice INV-001')
    expect(row.element().textContent ?? '').not.toContain('Source Document Id: doc-1')
  })

  it('ignores stale effects responses when the route changes to another document before the first snapshot resolves', async () => {
    await page.viewport(1280, 900)

    const first = createDeferred<{
      accountingEntries: Array<Record<string, unknown>>
      operationalRegisterMovements: unknown[]
      referenceRegisterWrites: unknown[]
    }>()
    const second = createDeferred<{
      accountingEntries: Array<Record<string, unknown>>
      operationalRegisterMovements: unknown[]
      referenceRegisterWrites: unknown[]
    }>()

    mocks.editorConfig.loadDocumentEffects.mockImplementation(async (_documentType: string, id: string) => {
      if (id === 'doc-1') return await first.promise
      return await second.promise
    })

    const view = await render(NgbDocumentEffectsPage)

    mocks.route.params.id = 'doc-2'
    mocks.route.fullPath = '/documents/pm.invoice/doc-2/effects'
    await flushUi()

    second.resolve({
      accountingEntries: [
        {
          entryId: 'entry-2',
          occurredAtUtc: '2026-04-09T12:30:00.000Z',
          debitAccount: {
            accountId: 'cash-2',
            code: '1100',
            name: 'Cash',
          },
          creditAccount: {
            accountId: 'rent-2',
            code: '4100',
            name: 'Rent Revenue',
          },
          amount: 1600,
          debitDimensions: [],
          creditDimensions: [],
        },
      ],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [],
    })
    await flushUi()

    await expect.element(view.getByText('Invoice INV-002')).toBeVisible()
    await expect.element(view.getByTestId('register-row-entry-2:debit')).toBeVisible()

    first.resolve({
      accountingEntries: [
        {
          entryId: 'entry-1',
          occurredAtUtc: '2026-04-08T12:30:00.000Z',
          debitAccount: {
            accountId: 'cash-1',
            code: '1100',
            name: 'Cash',
          },
          creditAccount: {
            accountId: 'rent-1',
            code: '4100',
            name: 'Rent Revenue',
          },
          amount: 1250,
          debitDimensions: [],
          creditDimensions: [],
        },
      ],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [],
    })
    await flushUi()

    await expect.element(view.getByText('Invoice INV-002')).toBeVisible()
    expect(document.body.textContent).toContain('1,600.00')
    expect(document.body.textContent).not.toContain('1,250.00')
    expect(document.querySelector('[data-testid="register-row-entry-1:debit"]')).toBeNull()
  })
})
