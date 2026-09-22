import { defineComponent, h } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'

import type { PayablesSuggestFifoApplyResponseDto } from '../../../src/api/types/pmContracts'

const mocks = vi.hoisted(() => ({
  details: vi.fn(),
  suggest: vi.fn(),
  apply: vi.fn(),
  catalogById: vi.fn(),
}))

vi.mock('../../../src/api/clients/payables', () => ({
  getPayablesOpenItemsDetails: mocks.details,
  suggestPayablesFifoApply: mocks.suggest,
  applyPayablesBatch: mocks.apply,
  unapplyPayablesApply: vi.fn(),
}))

vi.mock('@ngbplatform/ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@ngbplatform/ui')>()),
  getCatalogById: mocks.catalogById,
  useToasts: () => ({ push: vi.fn() }),
}))

import PayablesOpenItemsPage from '../../../src/pages/PayablesOpenItemsPage.vue'

const partyId = '11111111-1111-4111-8111-111111111111'
const propertyId = '22222222-2222-4222-8222-222222222222'
const chargeId = '33333333-3333-4333-8333-333333333333'
const creditId = '44444444-4444-4444-8444-444444444444'

function suggestion(): PayablesSuggestFifoApplyResponseDto {
  return {
    registerId: 'pm.payables', vendorId: partyId, vendorDisplay: 'Vendor One', propertyId, propertyDisplay: 'Property One',
    totalOutstanding: 200, totalCredit: 687, totalApplied: 200, remainingOutstanding: 0, remainingCredit: 487,
    warnings: [],
    suggestedApplies: [{
      applyId: null, creditDocumentId: creditId, creditDocumentType: 'pm.payable_payment',
      creditDocumentDisplay: 'Payable Payment PP-001', creditDocumentDateUtc: '2026-09-19',
      creditAmountBefore: 687, creditAmountAfter: 487, chargeDocumentId: chargeId, chargeDisplay: 'Payable Charge PC-001',
      chargeDueOnUtc: '2026-09-19', chargeOutstandingBefore: 200, chargeOutstandingAfter: 0,
      amount: 200, applyPayload: { fields: {} },
    }],
  }
}

beforeEach(() => {
  vi.resetAllMocks()
  sessionStorage.clear()
  mocks.catalogById.mockResolvedValue({ display: 'Selected context' })
  mocks.details.mockResolvedValue({
    registerId: 'pm.payables', vendorId: partyId, vendorDisplay: 'Vendor One', propertyId, propertyDisplay: 'Property One',
    totalOutstanding: 200, totalCredit: 687,
    charges: [{
      chargeDocumentId: chargeId, documentType: 'pm.payable_charge', number: 'PC-001', chargeDisplay: 'Payable Charge PC-001',
      dueOnUtc: '2026-09-19', chargeTypeDisplay: 'Cleaning', originalAmount: 200, outstandingAmount: 200,
    }],
    credits: [{
      creditDocumentId: creditId, documentType: 'pm.payable_payment', number: 'PP-001', creditDocumentDisplay: 'Payable Payment PP-001',
      creditDocumentDateUtc: '2026-09-19', originalAmount: 687, availableCredit: 687,
    }],
    allocations: [],
  })
})

test.each(['pm.payable_charge', 'pm.payable_payment', 'pm.payable_credit_memo'])(
  'loads suggestions on the first Apply navigation from %s without Refresh or cancelling the request',
  async (source) => {
    let resolveSuggestion!: (value: PayablesSuggestFifoApplyResponseDto) => void
    mocks.suggest.mockImplementation(() => new Promise<PayablesSuggestFifoApplyResponseDto>((resolve) => { resolveSuggestion = resolve }))
    const focusItemId = source === 'pm.payable_charge' ? chargeId : creditId
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/payables/open-items', component: PayablesOpenItemsPage },
        {
          path: '/documents/:type/:id',
          component: defineComponent({ setup: () => () => h('button', {
            onClick: () => router.push({ path: '/payables/open-items', query: {
              partyId, propertyId, focusItemId, source, openApply: '1', refresh: '1',
            } }),
          }, 'Apply') }),
        },
      ],
    })
    await router.push(`/documents/${source}/${focusItemId}`)
    await router.isReady()
    const view = await render(defineComponent({ setup: () => () => h(RouterView) }), { global: { plugins: [router] } })
    await view.getByRole('button', { name: 'Apply', exact: true }).click()

    await expect.poll(() => mocks.suggest.mock.calls.length).toBe(1)
    await expect.poll(() => router.currentRoute.value.query).toEqual({ partyId, propertyId, focusItemId })
    const signal = mocks.suggest.mock.calls[0]![1].signal as AbortSignal
    expect(signal.aborted).toBe(false)
    await expect.element(view.getByText('Building FIFO suggestion…')).toBeVisible()

    resolveSuggestion(suggestion())
    await expect.element(view.getByText('1 apply totaling 200.00', { exact: true })).toBeVisible()
    await expect.element(view.getByRole('button', { name: 'Execute Apply', exact: true })).toBeEnabled()
    expect(mocks.suggest).toHaveBeenCalledTimes(1)
    expect(mocks.details).toHaveBeenCalledTimes(1)
    expect(mocks.apply).not.toHaveBeenCalled()
  },
)
