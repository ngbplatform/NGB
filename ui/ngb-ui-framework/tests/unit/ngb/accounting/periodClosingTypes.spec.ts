import { describe, expect, it } from 'vitest'

import type {
  FiscalYearCloseStatusDto,
  PeriodClosingCalendarDto,
} from '../../../../src/ngb/accounting/periodClosingTypes'

describe('period closing types', () => {
  it('supports monthly calendar and fiscal year status payloads', () => {
    const calendar: PeriodClosingCalendarDto = {
      year: 2026,
      yearStartPeriod: '2026-01',
      yearEndPeriod: '2026-12',
      latestClosedPeriod: '2026-03',
      nextClosablePeriod: '2026-04',
      canCloseAnyMonth: true,
      hasBrokenChain: false,
      months: [
        {
          period: '2026-03',
          state: 'closed',
          isClosed: true,
          hasActivity: true,
          canClose: false,
          canReopen: true,
        },
      ],
    }
    const fiscalYear: FiscalYearCloseStatusDto = {
      fiscalYearEndPeriod: '2026-12',
      fiscalYearStartPeriod: '2026-01',
      state: 'open',
      documentId: 'fy-close-2026',
      endPeriodClosed: false,
      canClose: true,
      canReopen: false,
      reopenWillOpenEndPeriod: true,
      priorMonths: calendar.months,
    }

    expect(calendar.months[0]?.isClosed).toBe(true)
    expect(fiscalYear.priorMonths[0]?.period).toBe('2026-03')
  })
})
