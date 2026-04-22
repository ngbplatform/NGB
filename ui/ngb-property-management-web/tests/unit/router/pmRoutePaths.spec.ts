import { describe, expect, it } from 'vitest'

import { buildPmOpenItemsPath, buildPmReconciliationPath } from '../../../src/router/pmRoutePaths'

describe('pm route paths', () => {
  it('builds open items routes', () => {
    expect(buildPmOpenItemsPath('receivables')).toBe('/receivables/open-items')
    expect(buildPmOpenItemsPath('payables')).toBe('/payables/open-items')
  })

  it('adds only populated reconciliation query params', () => {
    expect(buildPmReconciliationPath('receivables')).toBe('/receivables/reconciliation')
    expect(buildPmReconciliationPath('payables', {
      fromMonth: '2026-04-01',
      toMonth: '2026-04-01',
      mode: 'Balance',
    })).toBe('/payables/reconciliation?fromMonth=2026-04-01&toMonth=2026-04-01&mode=Balance')
  })
})
