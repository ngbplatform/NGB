import { describe, expect, it } from 'vitest'

import { EMPTY_GUID } from '../../../../src/ngb/utils/guid'
import {
  collectAccountingEntryAccountIds,
  finalizeEffectDimensionSummary,
  resolveEffectAccountLabel,
} from '../../../../src/ngb/editor/documentEffects'

describe('document effects helpers', () => {
  it('prefers explicit account metadata and falls back to a coa label resolver when needed', () => {
    expect(resolveEffectAccountLabel({
      accountId: 'cash',
      code: '1100',
      name: 'Cash',
    }, null)).toBe('1100 — Cash')
    expect(resolveEffectAccountLabel({
      accountId: 'cash',
      code: '1100',
      name: '',
    }, null)).toBe('1100')
    expect(resolveEffectAccountLabel({
      accountId: 'cash',
      code: '',
      name: 'Cash',
    }, null)).toBe('Cash')
    expect(resolveEffectAccountLabel(
      null,
      '11111111-1111-1111-1111-111111111111',
      (id) => `COA ${String(id)}`,
    )).toBe('COA 11111111-1111-1111-1111-111111111111')
    expect(resolveEffectAccountLabel(null, EMPTY_GUID, () => 'ignored')).toBe('—')
  })

  it('returns visible dimension items or a shortened dimension set fallback', () => {
    expect(finalizeEffectDimensionSummary(['Riverfront Tower', '', '—', 'North Portfolio'])).toEqual([
      'Riverfront Tower',
      'North Portfolio',
    ])
    expect(finalizeEffectDimensionSummary([], '22222222-2222-2222-2222-222222222222')).toBe('22222222…2222')
    expect(finalizeEffectDimensionSummary(['', '—'], EMPTY_GUID)).toBe('—')
  })

  it('collects unresolved debit and credit account ids only once', () => {
    expect(collectAccountingEntryAccountIds({
      accountingEntries: [
        {
          entryId: 'entry-1',
          occurredAtUtc: '2026-04-08T12:00:00Z',
          debitAccount: null,
          debitAccountId: '11111111-1111-1111-1111-111111111111',
          creditAccount: null,
          creditAccountId: '22222222-2222-2222-2222-222222222222',
          amount: 1250,
        },
        {
          entryId: 'entry-2',
          occurredAtUtc: '2026-04-08T13:00:00Z',
          debitAccount: {
            accountId: 'cash',
            code: '1100',
            name: 'Cash',
          },
          debitAccountId: '33333333-3333-3333-3333-333333333333',
          creditAccount: null,
          creditAccountId: '22222222-2222-2222-2222-222222222222',
          amount: 450,
        },
        {
          entryId: 'entry-3',
          occurredAtUtc: '2026-04-08T14:00:00Z',
          debitAccount: null,
          debitAccountId: EMPTY_GUID,
          creditAccount: null,
          creditAccountId: 'not-a-guid',
          amount: 99,
        },
      ],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [],
    })).toEqual([
      '11111111-1111-1111-1111-111111111111',
      '22222222-2222-2222-2222-222222222222',
    ])
  })
})
