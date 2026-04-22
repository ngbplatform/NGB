import type { NgbIconName } from 'ngb-ui-framework'

export type AgencyBillingHomeAction = {
  title: string
  subtitle: string
  route: string
  icon: NgbIconName
  badge: string
  tone: 'neutral' | 'success' | 'warn'
}

export type AgencyBillingHomeFocusArea = {
  title: string
  description: string
  route: string
  icon: NgbIconName
  points: string[]
}

export type AgencyBillingHomePulse = {
  title: string
  detail: string
  badge: string
}

export type AgencyBillingHomeData = {
  headerSummary: string
  actions: AgencyBillingHomeAction[]
  focusAreas: AgencyBillingHomeFocusArea[]
  pulses: AgencyBillingHomePulse[]
}

export function createAgencyBillingHomeData(): AgencyBillingHomeData {
  return {
    headerSummary: 'Agency delivery, client billing, receivables control',
    actions: [
      {
        title: 'Capture Timesheet',
        subtitle: 'Enter billable and non-billable hours with service-level detail.',
        route: '/documents/ab.timesheet/new',
        icon: 'calendar-check',
        badge: 'Time',
        tone: 'success',
      },
      {
        title: 'Draft Sales Invoice',
        subtitle: 'Prepare customer billing from approved hours and contract terms.',
        route: '/documents/ab.sales_invoice/new',
        icon: 'receipt',
        badge: 'Billing',
        tone: 'neutral',
      },
      {
        title: 'Record Customer Payment',
        subtitle: 'Apply incoming cash to open invoices and keep AR current.',
        route: '/documents/ab.customer_payment/new',
        icon: 'wallet',
        badge: 'Cash',
        tone: 'warn',
      },
    ],
    focusAreas: [
      {
        title: 'Master Data',
        description: 'Keep clients, projects, team members, rate cards, and service items aligned before operational activity starts.',
        route: '/catalogs/ab.client',
        icon: 'users',
        points: [
          'Client setup includes payment terms and default currency.',
          'Projects connect delivery ownership and billing model.',
          'Rate cards capture the canonical sell-side economics.',
        ],
      },
      {
        title: 'Delivery To Billing',
        description: 'The core operational path starts in timesheets, flows into invoices, and ends in receivables collection.',
        route: '/documents/ab.timesheet',
        icon: 'coins',
        points: [
          'Timesheets capture hours, billable status, and cost context.',
          'Sales invoices organize billable service lines for clients.',
          'Customer payments close the loop through invoice applies.',
        ],
      },
      {
        title: 'Revenue & Receivables',
        description: 'Stay on top of unbilled work, issued invoices, and overdue balances from the same vertical cockpit.',
        route: '/reports/ab.ar_aging',
        icon: 'bar-chart',
        points: [
          'Unbilled Time highlights delivery that still has to become billing.',
          'Invoice Register tracks issued, applied, and open client balances.',
          'AR Aging keeps overdue receivables visible before collections slip.',
        ],
      },
    ],
    pulses: [
      {
        title: 'Client Contracts',
        detail: 'Define active commercial terms, frequency, and line-level rate rules.',
        badge: 'Commercial',
      },
      {
        title: 'Time Capture',
        detail: 'Keep hours, billable flags, and cost visibility together in one document.',
        badge: 'Operations',
      },
      {
        title: 'Receivables',
        detail: 'Track invoices and collections without leaving the same vertical shell.',
        badge: 'Finance',
      },
    ],
  }
}
