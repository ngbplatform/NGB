import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

import type { ReconciliationPageDefinition } from '../../src/features/reconciliation/types'

const state = vi.hoisted(() => ({
  definitions: [] as ReconciliationPageDefinition[],
  getPayablesReconciliation: vi.fn(),
  getReceivablesReconciliation: vi.fn(),
}))

vi.mock('../../src/api/clients/payables', () => ({
  getPayablesReconciliation: state.getPayablesReconciliation,
}))

vi.mock('../../src/api/clients/receivables', () => ({
  getReceivablesReconciliation: state.getReceivablesReconciliation,
}))

vi.mock('../../src/features/reconciliation/ReconciliationPage.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      props: {
        definition: {
          type: Object,
          required: true,
        },
      },
      setup(props) {
        state.definitions.push(props.definition as ReconciliationPageDefinition)
        return () => h('div', { 'data-testid': 'reconciliation-stub' }, (props.definition as ReconciliationPageDefinition).title)
      },
    }),
  }
})

import PayablesReconciliationPage from '../../src/pages/PayablesReconciliationPage.vue'
import ReceivablesReconciliationPage from '../../src/pages/ReceivablesReconciliationPage.vue'

test('payables entry page maps the complete report contract into the shared reconciliation page', async () => {
  await page.viewport(1280, 900)
  state.definitions.length = 0
  state.getPayablesReconciliation.mockResolvedValueOnce({
    totalApNet: 120,
    totalOpenItemsNet: 100,
    totalDiff: 20,
    rowCount: 1,
    mismatchRowCount: 1,
    filteredRowCount: 1,
    glOnlyRowCount: 0,
    openItemsOnlyRowCount: 0,
    rows: [{
      vendorId: 'vendor-1',
      vendorDisplay: 'Acme Supplies',
      propertyId: 'property-1',
      propertyDisplay: 'Riverfront',
      apNet: 120,
      openItemsNet: 100,
      diff: 20,
      rowKind: 'Mismatch',
      hasDiff: true,
    }],
  })

  const view = await render(PayablesReconciliationPage)
  await expect.element(view.getByTestId('reconciliation-stub')).toHaveTextContent('Payables')

  const definition = state.definitions[0]!
  expect(definition.describeMode({ mode: 'Balance', fromMonth: '2026-01', toMonth: '2026-03' })).toContain('2026-03')
  expect(definition.describeMode({ mode: 'Movement', fromMonth: '2026-01', toMonth: '2026-03' })).toContain('2026-01 → 2026-03')
  await expect(definition.load({
    fromMonthInclusive: '2026-01',
    toMonthInclusive: '2026-03',
    mode: 'Balance',
  })).resolves.toEqual({
    totalLedgerNet: 120,
    totalOpenItemsNet: 100,
    totalDiff: 20,
    rowCount: 1,
    mismatchRowCount: 1,
    filteredRowCount: 1,
    glOnlyRowCount: 0,
    openItemsOnlyRowCount: 0,
    rows: [{
      key: 'vendor-1:property-1',
      rowKind: 'Mismatch',
      hasDiff: true,
      primaryLabel: 'Acme Supplies',
      secondaryLabel: 'Riverfront',
      tertiaryLabel: null,
      ledgerNet: 120,
      openItemsNet: 100,
      diff: 20,
      openTarget: {
        path: '/payables/open-items',
        query: { partyId: 'vendor-1', propertyId: 'property-1' },
      },
    }],
    offset: 0,
    limit: 100,
    hasMore: false,
    nextCursor: null,
  })
})

test('receivables entry page maps party, property, and lease drilldown dimensions', async () => {
  await page.viewport(1280, 900)
  state.definitions.length = 0
  state.getReceivablesReconciliation.mockResolvedValueOnce({
    totalArNet: 250,
    totalOpenItemsNet: 250,
    totalDiff: 0,
    rowCount: 1,
    mismatchRowCount: 0,
    rows: [{
      partyId: 'party-1',
      partyDisplay: 'Northwind',
      propertyId: 'property-1',
      propertyDisplay: 'Riverfront',
      leaseId: 'lease-1',
      leaseDisplay: 'Lease 101',
      arNet: 250,
      openItemsNet: 250,
      diff: 0,
      rowKind: 'Matched',
      hasDiff: false,
    }],
  })

  const view = await render(ReceivablesReconciliationPage)
  await expect.element(view.getByTestId('reconciliation-stub')).toHaveTextContent('Receivables')

  const definition = state.definitions[0]!
  expect(definition.describeMode({ mode: 'Balance', fromMonth: '2026-01', toMonth: '2026-03' })).toContain('2026-03')
  expect(definition.describeMode({ mode: 'Movement', fromMonth: '2026-01', toMonth: '2026-03' })).toContain('2026-01 → 2026-03')
  await expect(definition.load({
    fromMonthInclusive: '2026-01',
    toMonthInclusive: '2026-03',
    mode: 'Movement',
  })).resolves.toMatchObject({
    totalLedgerNet: 250,
    totalOpenItemsNet: 250,
    totalDiff: 0,
    rowCount: 1,
    mismatchRowCount: 0,
    rows: [{
      key: 'party-1:property-1:lease-1',
      primaryLabel: 'Northwind',
      secondaryLabel: 'Riverfront',
      tertiaryLabel: 'Lease 101',
      ledgerNet: 250,
      openItemsNet: 250,
      diff: 0,
      openTarget: {
        path: '/receivables/open-items',
        query: { leaseId: 'lease-1', partyId: 'party-1', propertyId: 'property-1' },
      },
    }],
  })
})
