import { beforeEach, describe, expect, it, vi } from 'vitest'

const platform = vi.hoisted(() => ({
  migration: null as null | {
    sources: () => readonly unknown[]
    migrate: (values: readonly unknown[]) => unknown
  },
}))

vi.mock('@ngbplatform/ui', () => ({
  isEmptyGuid: (value: unknown) => !value || value === '00000000-0000-0000-0000-000000000000',
  shortGuid: (value: unknown) => `short:${value}`,
  useRouteQueryMigration: (options: never) => { platform.migration = options },
}))

import {
  createReconciliationPageDefinition,
  displayOrGuid,
  formatAbsoluteMoney,
} from '../../../src/features/reconciliation/definitionFactory'
import {
  encodeReconciliationStatusFilter,
  normalizeReconciliationMode,
  normalizeReconciliationStatusFilter,
  useReconciliationLegacyQueryCompat,
} from '../../../src/features/reconciliation/queryState'

describe('reconciliation helpers', () => {
  beforeEach(() => { platform.migration = null })

  it('normalizes every supported and unsupported query representation', () => {
    expect(normalizeReconciliationMode([' Movement ', 'ignored'])).toBe('Movement')
    expect(normalizeReconciliationMode('balance')).toBe('Balance')
    expect(normalizeReconciliationMode(null)).toBe('Balance')

    const cases: Array<[unknown, string]> = [
      ['matched', 'matched'], ['mismatch', 'mismatch'], ['gl-only', 'glOnly'], ['glonly', 'glOnly'],
      ['open-items-only', 'openItemsOnly'], ['openitemsonly', 'openItemsOnly'], [['MATCHED'], 'matched'],
      [undefined, 'all'], ['unknown', 'all'],
    ]
    for (const [input, expected] of cases) expect(normalizeReconciliationStatusFilter(input)).toBe(expected)
    expect(encodeReconciliationStatusFilter('matched')).toBe('matched')
    expect(encodeReconciliationStatusFilter('mismatch')).toBe('mismatch')
    expect(encodeReconciliationStatusFilter('glOnly')).toBe('gl-only')
    expect(encodeReconciliationStatusFilter('openItemsOnly')).toBe('open-items-only')
    expect(encodeReconciliationStatusFilter('all')).toBeUndefined()
  })

  it('migrates only meaningful legacy rows filters', () => {
    const route = { query: { status: 'matched', rows: 'mismatches' } } as never
    useReconciliationLegacyQueryCompat(route, {} as never)
    const migrate = platform.migration!.migrate

    expect(platform.migration!.sources()).toEqual(['matched', 'mismatches'])
    expect(migrate(['matched', null])).toBeNull()
    expect(migrate(['matched', [undefined]])).toEqual({ rows: undefined })
    expect(migrate(['matched', 'all'])).toEqual({ rows: undefined })
    expect(migrate(['mismatch', 'mismatches'])).toEqual({ rows: undefined })
    expect(migrate(['matched', ['MISMATCHES']])).toEqual({ status: 'mismatch', rows: undefined })
  })

  it('builds a complete definition and covers every explanation and mode', async () => {
    expect(displayOrGuid(' Display ', 'id')).toBe('Display')
    expect(displayOrGuid('', null)).toBe('—')
    expect(displayOrGuid(null, '00000000-0000-0000-0000-000000000000')).toBe('—')
    expect(displayOrGuid(undefined, 'id')).toBe('short:id')
    expect(formatAbsoluteMoney(-12.345)).toBe((12.34).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))
    expect(formatAbsoluteMoney(null as never)).toBe((0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))

    const loadReport = vi.fn().mockResolvedValue({ rows: [{ id: '1' }], ledger: 10, open: 8, diff: 2, mismatches: 1 })
    const definition = createReconciliationPageDefinition({
      title: 'Title', ledgerNetLabel: 'Ledger', ledgerEntityName: 'receivables', diffSummaryDescription: 'diff',
      groupedByDescription: 'grouped', rowsDescription: 'rows', noRowsMessage: 'none', primaryColumnTitle: 'one',
      secondaryColumnTitle: 'two', balanceNotes: ['balance'], movementNotes: ['movement'],
      describeBalance: (to) => `balance:${to}`, describeMovement: (from, to) => `movement:${from}:${to}`,
      matchedExplanation: 'matched', glOnlyExplanation: 'gl', openItemsOnlyExplanation: 'open',
      toRow: (row: { id: string }) => ({ ...row, rowKind: 'Matched', diff: 0 }) as never,
      loadReport,
      getRows: (report) => report.rows,
      getTotalLedgerNet: (report) => report.ledger,
      getTotalOpenItemsNet: (report) => report.open,
      getTotalDiff: (report) => report.diff,
      getRowCount: (report) => report.rows.length,
      getMismatchRowCount: (report) => report.mismatches,
    })

    expect(definition.tertiaryColumnTitle).toBeNull()
    expect(definition.ledgerNetSummaryDescription('Balance')).toBe('GL receivables balance')
    expect(definition.ledgerNetSummaryDescription('Movement')).toBe('GL receivables movement')
    expect(definition.describeMode({ mode: 'Balance', fromMonth: 'from', toMonth: 'to' })).toBe('balance:to')
    expect(definition.describeMode({ mode: 'Movement', fromMonth: 'from', toMonth: 'to' })).toBe('movement:from:to')
    expect(definition.explainRow({ rowKind: 'Matched' } as never)).toBe('matched')
    expect(definition.explainRow({ rowKind: 'Mismatch', diff: -2 } as never)).toContain('2.00')
    expect(definition.explainRow({ rowKind: 'GlOnly' } as never)).toBe('gl')
    expect(definition.explainRow({ rowKind: 'OpenItemsOnly' } as never)).toBe('open')
    expect(definition.explainRow({ rowKind: 'future' } as never)).toBe('Investigate the row.')
    await expect(definition.load({ mode: 'Balance' } as never)).resolves.toEqual({
      totalLedgerNet: 10, totalOpenItemsNet: 8, totalDiff: 2, rowCount: 1, mismatchRowCount: 1,
      filteredRowCount: 1, glOnlyRowCount: 0, openItemsOnlyRowCount: 0,
      rows: [{ id: '1', rowKind: 'Matched', diff: 0 }],
      offset: 0, limit: 100, hasMore: false, nextCursor: null,
    })

    const explicitTertiary = createReconciliationPageDefinition({
      ...({} as never),
      tertiaryColumnTitle: 'three',
    } as never)
    expect(explicitTertiary.tertiaryColumnTitle).toBe('three')
  })
})
