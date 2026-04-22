import { describe, expect, it } from 'vitest'

import { createAgencyBillingHomeData } from '../../../src/home/homeData'

describe('agency billing home data', () => {
  it('publishes the hero summary and launch actions', () => {
    const data = createAgencyBillingHomeData()

    expect(data.headerSummary).toBe('Agency delivery, client billing, receivables control')
    expect(data.actions).toEqual([
      expect.objectContaining({
        title: 'Capture Timesheet',
        route: '/documents/ab.timesheet/new',
        badge: 'Time',
        tone: 'success',
      }),
      expect.objectContaining({
        title: 'Draft Sales Invoice',
        route: '/documents/ab.sales_invoice/new',
        badge: 'Billing',
        tone: 'neutral',
      }),
      expect.objectContaining({
        title: 'Record Customer Payment',
        route: '/documents/ab.customer_payment/new',
        badge: 'Cash',
        tone: 'warn',
      }),
    ])
  })

  it('defines the core operating model lanes with stable routes', () => {
    const data = createAgencyBillingHomeData()

    expect(data.focusAreas).toEqual([
      expect.objectContaining({
        title: 'Master Data',
        route: '/catalogs/ab.client',
      }),
      expect.objectContaining({
        title: 'Delivery To Billing',
        route: '/documents/ab.timesheet',
      }),
      expect.objectContaining({
        title: 'Revenue & Receivables',
        route: '/reports/ab.ar_aging',
      }),
    ])
    expect(data.focusAreas.every((area) => area.points.length === 3)).toBe(true)
  })

  it('keeps the pulse cards aligned with commercial, operations, and finance themes', () => {
    const data = createAgencyBillingHomeData()

    expect(data.pulses.map((pulse) => pulse.badge)).toEqual(['Commercial', 'Operations', 'Finance'])
    expect(data.pulses.map((pulse) => pulse.title)).toEqual(['Client Contracts', 'Time Capture', 'Receivables'])
  })
})
