import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import {
  formatOpenItemsDateCell,
  formatOpenItemsMoneyCell,
  useOpenItemsPagePresentation,
} from '../../../src/features/open-items/pagePresentation'
import { buildApplyResultSubtitle, buildApplyResultTitle, buildOpenItemsTabs } from '../../../src/features/open-items/presentation'
import { applyDocumentLabel, docLabel, fmtDateOnly, fmtMoney, formatApplyCount } from '../../../src/features/open-items/shared'

describe('open-items presentation', () => {
  it('formats money, dates, labels, counts, and tab summaries at their boundaries', () => {
    expect(fmtMoney(12.345)).toBe((12.35).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))
    expect(fmtMoney(null as never)).toBe((0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))
    expect(fmtDateOnly(null)).toBe('—')
    expect(fmtDateOnly('   ')).toBe('—')
    expect(fmtDateOnly('not-a-date')).toBe('not-a-date')
    expect(fmtDateOnly('2026-08-23')).toBe(new Date('2026-08-23T00:00:00Z').toLocaleDateString(undefined, {
      year: 'numeric', month: 'short', day: '2-digit',
    }))
    expect(formatOpenItemsDateCell(42)).toBe('—')
    expect(formatOpenItemsDateCell('2026-08-23')).toBe(fmtDateOnly('2026-08-23'))
    expect(formatOpenItemsMoneyCell(null)).toBe(fmtMoney(0))
    expect(formatOpenItemsMoneyCell('12.5')).toBe(fmtMoney(12.5))

    expect(docLabel(' INV-1 ', 'Display', 'fallback')).toBe('INV-1')
    expect(docLabel('', ' Display ', 'fallback')).toBe('Display')
    expect(docLabel(null, null, ' fallback ')).toBe('fallback')
    expect(docLabel()).toBe('—')
    expect(applyDocumentLabel('', 'Apply display', 'id')).toBe('Apply display')
    expect(formatApplyCount(1)).toBe('1 apply')
    expect(formatApplyCount(0)).toBe('0 applies')

    const summary = { totalOutstanding: 10, totalCredit: 2, chargesCount: 1, creditsCount: 2, allocationsCount: 3 }
    expect(buildOpenItemsTabs(summary)).toEqual([
      { key: 'charges', label: 'Charges (1)' },
      { key: 'credits', label: 'Credits (2)' },
      { key: 'applied', label: 'Applied (3)' },
    ])
    expect(buildApplyResultTitle(1)).toBe('Created 1 apply')
    expect(buildApplyResultTitle(2)).toBe('Created 2 applies')
    expect(buildApplyResultSubtitle(2, 12.5, (value) => `$${value}`)).toBe('Created 2 applies totaling $12.5.')
  })

  it('derives empty, charge, credit, and route-preferred contexts reactively', () => {
    const data = ref<{
      totalOutstanding?: number | null
      totalCredit?: number | null
      charges?: Array<{ chargeDocumentId: string }> | null
      credits?: Array<{ creditDocumentId: string }> | null
      allocations?: unknown[] | null
    } | null>(null)
    const focus = ref<string | null>(null)
    const sourceType = ref<string | null>(null)
    const chargeBadge = vi.fn((item: { chargeDocumentId: string }) => `charge:${item.chargeDocumentId}`)
    const creditBadge = vi.fn((item: { creditDocumentId: string }) => `credit:${item.creditDocumentId}`)
    const resolveTab = vi.fn((value: string | null) => value === 'credit-source' ? 'applied' as const : null)
    const state = useOpenItemsPagePresentation({
      data,
      focusItemId: computed(() => focus.value),
      sourceDocumentType: computed(() => sourceType.value),
      resolveTabFromSourceType: resolveTab,
      buildFocusedChargeBadge: chargeBadge,
      buildFocusedCreditBadge: creditBadge,
    })

    expect(state.summary.value).toEqual({ totalOutstanding: 0, totalCredit: 0, chargesCount: 0, creditsCount: 0, allocationsCount: 0 })
    expect(state.focusedCharge.value).toBeNull()
    expect(state.focusedCredit.value).toBeNull()
    expect(state.focusedContextBadge.value).toBeNull()
    expect(state.preferredTabFromRoute.value).toBeNull()

    data.value = {
      totalOutstanding: null,
      totalCredit: 5,
      charges: [{ chargeDocumentId: 'charge-1' }],
      credits: [{ creditDocumentId: 'credit-1' }],
      allocations: [1, 2],
    }
    focus.value = 'missing'
    expect(state.focusedCharge.value).toBeNull()
    expect(state.focusedCredit.value).toBeNull()

    data.value.charges = null
    data.value.credits = null
    expect(state.focusedCharge.value).toBeNull()
    expect(state.focusedCredit.value).toBeNull()
    data.value.charges = [{ chargeDocumentId: 'charge-1' }]
    data.value.credits = [{ creditDocumentId: 'credit-1' }]

    focus.value = 'charge-1'
    expect(state.focusedContextBadge.value).toBe('charge:charge-1')
    expect(state.preferredTabFromRoute.value).toBe('charges')
    focus.value = 'credit-1'
    expect(state.focusedContextBadge.value).toBe('credit:credit-1')
    expect(state.preferredTabFromRoute.value).toBe('credits')
    sourceType.value = 'credit-source'
    expect(state.preferredTabFromRoute.value).toBe('applied')
    expect(state.summary.value).toEqual({ totalOutstanding: 0, totalCredit: 5, chargesCount: 1, creditsCount: 1, allocationsCount: 2 })
  })
})
