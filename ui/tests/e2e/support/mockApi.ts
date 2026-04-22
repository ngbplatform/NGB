import type { Page, Route } from '@playwright/test'
import type {
  ReportExecutionRequestDto,
  ReportVariantDto,
} from '../../../ngb-ui-framework/src/ngb/reporting/types'
import type { ChartOfAccountsUpsertRequestDto } from '../../../ngb-ui-framework/src/ngb/accounting/types'
import type {
  CatalogItemDto,
  DocumentDto,
} from '../../../ngb-ui-framework/src/ngb/api/contracts'
import type {
  PayablesApplyBatchRequestDto,
  PayablesApplyBatchResponseDto,
  PayablesOpenItemsDetailsResponseDto,
  PayablesSuggestFifoApplyResponseDto,
  ReceivablesApplyBatchRequestDto,
  ReceivablesApplyBatchResponseDto,
  ReceivablesOpenItemsDetailsResponseDto,
  ReceivablesSuggestFifoApplyResponseDto,
} from '../../../ngb-property-management-web/src/api/types/pmContracts'

import {
  chartOfAccountsMetadataFixture,
  createChartOfAccountsPageFixture,
  createFiscalYearCloseStatusFixture,
  createPeriodClosingCalendarFixture,
  createSavedGeneralJournalEntryDraftFixture,
  createdChartOfAccountFixture,
  createdGeneralJournalEntryDraftFixture,
  generalJournalEntriesPageFixture,
  retainedEarningsAccountsFixture,
} from '../fixtures/pmAccounting'
import {
  accountingPolicyMetadataFixture,
  accountingPolicyPageFixture,
} from '../fixtures/pmAccountingPolicy'
import {
  payablesReconciliationFixture,
  receivablesReconciliationFixture,
} from '../fixtures/pmReconciliation'
import {
  occupancySummaryReportDefinitionFixture,
  occupancySummaryReportExecutionFixture,
  occupancySummaryReportPagedSecondExecutionFixture,
  occupancySummaryReportVariantsFixture,
} from '../fixtures/pmReports'
import {
  propertyBuildingsFixture,
  propertyBuildingSummaryFixture,
  propertyUnitsFixture,
} from '../fixtures/pmProperties'
import {
  homeLateFeeChargeDocumentsFixture,
  homeLeaseDocumentsFixture,
  homeMaintenanceQueueFixture,
  homeMaintenanceQueueOverdueFixture,
  homeOccupancySummaryFixture,
  homePeriodClosingCalendarFixture,
  homeReceivableChargeDocumentsFixture,
  homeReceivablePaymentDocumentsFixture,
  homeReceivablesReconciliationFixture,
  homeRentChargeDocumentsFixture,
  homeReturnedPaymentDocumentsFixture,
} from '../fixtures/pmHome'
import {
  mainMenuFixture,
  payablesOpenItemsFixture,
  payablesPropertyFixture,
  payablesVendorFixture,
  receivablesLeaseFixture,
  receivablesOpenItemsFixture,
} from '../fixtures/pmWeb'
import {
  createPartyCatalogPageFixture,
  createReceivablePaymentPageFixture,
  createdPartyCatalogFixture,
  createdReceivablePaymentFixture,
  partyCatalogBaseItemsFixture,
  partyCatalogMetadataFixture,
  receivablePaymentAuditFixture,
  receivablePaymentBaseDocumentsFixture,
  receivablePaymentEffectsFixture,
  receivablePaymentGraphFixture,
  receivablePaymentMetadataFixture,
} from '../fixtures/pmMetadataRoutes'

async function fulfillJson(route: Route, body: unknown, status = 200): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}

async function fulfillEmpty(route: Route, status = 204): Promise<void> {
  await route.fulfill({
    status,
    body: '',
  })
}

function parseRequestJson<T>(route: Route): T | null {
  const raw = route.request().postData()
  if (!raw) return null

  try {
    return JSON.parse(raw) as T
  } catch {
    return null
  }
}

type MockApiFailure = {
  detail?: string
  status?: number
  title?: string
  body?: Record<string, unknown>
}

type MockGenericMetadataCatalogApisOptions = {
  initialItems?: CatalogItemDto[]
  pageFailure?: MockApiFailure | null
  detailsFailure?: MockApiFailure | null
  createFailure?: MockApiFailure | null
  updateFailure?: MockApiFailure | null
  pageDelayMs?: number
  detailsDelayMs?: number
}

type MockGenericMetadataDocumentApisOptions = {
  initialDocuments?: DocumentDto[]
  pageFailure?: MockApiFailure | null
  detailsFailure?: MockApiFailure | null
  createFailure?: MockApiFailure | null
  updateFailure?: MockApiFailure | null
  pageDelayMs?: number
  detailsDelayMs?: number
}

type MockReceivablesOpenItemsWorkflowApisOptions = {
  initialDetails?: ReceivablesOpenItemsDetailsResponseDto
  suggestFailure?: MockApiFailure | null
  executeFailure?: MockApiFailure | null
  unapplyFailure?: MockApiFailure | null
}

type MockPayablesOpenItemsWorkflowApisOptions = {
  initialDetails?: PayablesOpenItemsDetailsResponseDto
  suggestFailure?: MockApiFailure | null
  executeFailure?: MockApiFailure | null
  unapplyFailure?: MockApiFailure | null
}

async function waitForMockDelay(delayMs?: number): Promise<void> {
  const normalized = Number(delayMs ?? 0)
  if (!Number.isFinite(normalized) || normalized <= 0) return
  await new Promise((resolve) => setTimeout(resolve, normalized))
}

async function fulfillApiFailure(
  route: Route,
  failure: MockApiFailure,
  fallbackTitle: string,
  fallbackStatus = 500,
): Promise<void> {
  const status = failure.status ?? fallbackStatus
  await fulfillJson(route, {
    title: failure.title ?? fallbackTitle,
    detail: failure.detail,
    status,
    ...(failure.body ?? {}),
  }, status)
}

function sumAmounts(values: number[]): number {
  return values.reduce((sum, value) => sum + Number(value ?? 0), 0)
}

function recalcReceivablesOpenItemsTotals(details: ReceivablesOpenItemsDetailsResponseDto): void {
  details.totalOutstanding = sumAmounts(details.charges.map((item) => Number(item.outstandingAmount ?? 0)))
  details.totalCredit = sumAmounts(details.credits.map((item) => Number(item.availableCredit ?? 0)))
}

function recalcPayablesOpenItemsTotals(details: PayablesOpenItemsDetailsResponseDto): void {
  details.totalOutstanding = sumAmounts(details.charges.map((item) => Number(item.outstandingAmount ?? 0)))
  details.totalCredit = sumAmounts(details.credits.map((item) => Number(item.availableCredit ?? 0)))
}

function buildReceivablesSuggestResponse(
  details: ReceivablesOpenItemsDetailsResponseDto,
): ReceivablesSuggestFifoApplyResponseDto {
  recalcReceivablesOpenItemsTotals(details)

  const charge = details.charges.find((item) => Number(item.outstandingAmount ?? 0) > 0) ?? null
  const credit = details.credits.find((item) => Number(item.availableCredit ?? 0) > 0) ?? null

  if (!charge || !credit) {
    const warnings = []
    if (!charge) warnings.push({ code: 'no_charges', message: 'No open charges remain.' })
    if (!credit) warnings.push({ code: 'no_credits', message: 'No unapplied credits remain.' })

    return {
      registerId: details.registerId,
      partyId: details.partyId,
      partyDisplay: details.partyDisplay,
      propertyId: details.propertyId,
      propertyDisplay: details.propertyDisplay,
      leaseId: details.leaseId,
      leaseDisplay: details.leaseDisplay,
      totalOutstanding: details.totalOutstanding,
      totalCredit: details.totalCredit,
      totalApplied: 0,
      remainingOutstanding: details.totalOutstanding,
      remainingCredit: details.totalCredit,
      suggestedApplies: [],
      warnings,
    }
  }

  const amount = Math.min(
    Number(charge.outstandingAmount ?? 0),
    Number(credit.availableCredit ?? 0),
  )
  const remainingOutstanding = Math.max(0, details.totalOutstanding - amount)
  const remainingCredit = Math.max(0, details.totalCredit - amount)
  const warnings = []

  if (remainingOutstanding > 0) {
    warnings.push({
      code: 'outstanding_remaining',
      message: 'Outstanding charges remain after the suggested apply.',
    })
  }
  if (remainingCredit > 0) {
    warnings.push({
      code: 'credit_remaining',
      message: 'Unapplied credits remain after the suggested apply.',
    })
  }

  return {
    registerId: details.registerId,
    partyId: details.partyId,
    partyDisplay: details.partyDisplay,
    propertyId: details.propertyId,
    propertyDisplay: details.propertyDisplay,
    leaseId: details.leaseId,
    leaseDisplay: details.leaseDisplay,
    totalOutstanding: details.totalOutstanding,
    totalCredit: details.totalCredit,
    totalApplied: amount,
    remainingOutstanding,
    remainingCredit,
    suggestedApplies: [
      {
        applyId: null,
        creditDocumentId: credit.creditDocumentId,
        creditDocumentType: credit.documentType,
        creditDocumentDisplay: credit.creditDocumentDisplay,
        creditDocumentDateUtc: credit.receivedOnUtc,
        creditAmountBefore: Number(credit.availableCredit ?? 0),
        creditAmountAfter: Math.max(0, Number(credit.availableCredit ?? 0) - amount),
        chargeDocumentId: charge.chargeDocumentId,
        chargeDisplay: charge.chargeDisplay,
        chargeDueOnUtc: charge.dueOnUtc,
        chargeOutstandingBefore: Number(charge.outstandingAmount ?? 0),
        chargeOutstandingAfter: Math.max(0, Number(charge.outstandingAmount ?? 0) - amount),
        amount,
        applyPayload: {
          fields: {
            credit_document_id: credit.creditDocumentId,
            charge_document_id: charge.chargeDocumentId,
            amount,
          },
        },
      },
    ],
    warnings,
  }
}

function buildPayablesSuggestResponse(
  details: PayablesOpenItemsDetailsResponseDto,
): PayablesSuggestFifoApplyResponseDto {
  recalcPayablesOpenItemsTotals(details)

  const charge = details.charges.find((item) => Number(item.outstandingAmount ?? 0) > 0) ?? null
  const credit = details.credits.find((item) => Number(item.availableCredit ?? 0) > 0) ?? null

  if (!charge || !credit) {
    const warnings = []
    if (!charge) warnings.push({ code: 'no_charges', message: 'No open charges remain.' })
    if (!credit) warnings.push({ code: 'no_credits', message: 'No unapplied credits remain.' })

    return {
      registerId: details.registerId,
      vendorId: details.vendorId,
      vendorDisplay: details.vendorDisplay,
      propertyId: details.propertyId,
      propertyDisplay: details.propertyDisplay,
      totalOutstanding: details.totalOutstanding,
      totalCredit: details.totalCredit,
      totalApplied: 0,
      remainingOutstanding: details.totalOutstanding,
      remainingCredit: details.totalCredit,
      suggestedApplies: [],
      warnings,
    }
  }

  const amount = Math.min(
    Number(charge.outstandingAmount ?? 0),
    Number(credit.availableCredit ?? 0),
  )
  const remainingOutstanding = Math.max(0, details.totalOutstanding - amount)
  const remainingCredit = Math.max(0, details.totalCredit - amount)
  const warnings = []

  if (remainingOutstanding > 0) {
    warnings.push({
      code: 'outstanding_remaining',
      message: 'Outstanding charges remain after the suggested apply.',
    })
  }
  if (remainingCredit > 0) {
    warnings.push({
      code: 'credit_remaining',
      message: 'Unapplied credits remain after the suggested apply.',
    })
  }

  return {
    registerId: details.registerId,
    vendorId: details.vendorId,
    vendorDisplay: details.vendorDisplay,
    propertyId: details.propertyId,
    propertyDisplay: details.propertyDisplay,
    totalOutstanding: details.totalOutstanding,
    totalCredit: details.totalCredit,
    totalApplied: amount,
    remainingOutstanding,
    remainingCredit,
    suggestedApplies: [
      {
        applyId: null,
        creditDocumentId: credit.creditDocumentId,
        creditDocumentType: credit.documentType,
        creditDocumentDisplay: credit.creditDocumentDisplay,
        creditDocumentDateUtc: credit.creditDocumentDateUtc,
        creditAmountBefore: Number(credit.availableCredit ?? 0),
        creditAmountAfter: Math.max(0, Number(credit.availableCredit ?? 0) - amount),
        chargeDocumentId: charge.chargeDocumentId,
        chargeDisplay: charge.chargeDisplay,
        chargeDueOnUtc: charge.dueOnUtc,
        chargeOutstandingBefore: Number(charge.outstandingAmount ?? 0),
        chargeOutstandingAfter: Math.max(0, Number(charge.outstandingAmount ?? 0) - amount),
        amount,
        applyPayload: {
          fields: {
            credit_document_id: credit.creditDocumentId,
            charge_document_id: charge.chargeDocumentId,
            amount,
          },
        },
      },
    ],
    warnings,
  }
}

function nextReceivableApplyId(sequence: number): string {
  return `aaaaaaaa-aaaa-4aaa-8aaa-${String(sequence).padStart(12, '0')}`
}

function nextPayableApplyId(sequence: number): string {
  return `eeeeeeee-eeee-4eee-8eee-${String(sequence).padStart(12, '0')}`
}

function applyReceivablesSuggestion(
  details: ReceivablesOpenItemsDetailsResponseDto,
  applyId: string,
): ReceivablesApplyBatchResponseDto['executedApplies'][number] | null {
  const suggestion = buildReceivablesSuggestResponse(details).suggestedApplies[0] ?? null
  if (!suggestion) return null

  const charge = details.charges.find((item) => item.chargeDocumentId === suggestion.chargeDocumentId) ?? null
  const credit = details.credits.find((item) => item.creditDocumentId === suggestion.creditDocumentId) ?? null
  if (!charge || !credit) return null

  const amount = Math.min(
    Number(suggestion.amount ?? 0),
    Number(charge.outstandingAmount ?? 0),
    Number(credit.availableCredit ?? 0),
  )
  if (amount <= 0) return null

  charge.outstandingAmount = Math.max(0, Number(charge.outstandingAmount ?? 0) - amount)
  credit.availableCredit = Math.max(0, Number(credit.availableCredit ?? 0) - amount)
  details.allocations = [
    ...details.allocations,
    {
      applyId,
      applyDisplay: `Apply ${String(details.allocations.length + 1).padStart(4, '0')}`,
      applyNumber: `AP-${String(details.allocations.length + 1).padStart(4, '0')}`,
      creditDocumentId: credit.creditDocumentId,
      creditDocumentType: credit.documentType,
      creditDocumentDisplay: credit.creditDocumentDisplay,
      creditDocumentNumber: credit.number ?? null,
      chargeDocumentId: charge.chargeDocumentId,
      chargeDocumentType: charge.documentType,
      chargeDisplay: charge.chargeDisplay,
      chargeNumber: charge.number ?? null,
      appliedOnUtc: '2026-04-07',
      amount,
      isPosted: true,
    },
  ]
  recalcReceivablesOpenItemsTotals(details)

  return {
    applyId,
    creditDocumentId: credit.creditDocumentId,
    chargeDocumentId: charge.chargeDocumentId,
    appliedOnUtc: '2026-04-07',
    amount,
    createdDraft: false,
  }
}

function applyPayablesSuggestion(
  details: PayablesOpenItemsDetailsResponseDto,
  applyId: string,
): PayablesApplyBatchResponseDto['executedApplies'][number] | null {
  const suggestion = buildPayablesSuggestResponse(details).suggestedApplies[0] ?? null
  if (!suggestion) return null

  const charge = details.charges.find((item) => item.chargeDocumentId === suggestion.chargeDocumentId) ?? null
  const credit = details.credits.find((item) => item.creditDocumentId === suggestion.creditDocumentId) ?? null
  if (!charge || !credit) return null

  const amount = Math.min(
    Number(suggestion.amount ?? 0),
    Number(charge.outstandingAmount ?? 0),
    Number(credit.availableCredit ?? 0),
  )
  if (amount <= 0) return null

  charge.outstandingAmount = Math.max(0, Number(charge.outstandingAmount ?? 0) - amount)
  credit.availableCredit = Math.max(0, Number(credit.availableCredit ?? 0) - amount)
  details.allocations = [
    ...details.allocations,
    {
      applyId,
      applyDisplay: `Apply ${String(details.allocations.length + 1).padStart(4, '0')}`,
      applyNumber: `AP-${String(details.allocations.length + 1).padStart(4, '0')}`,
      creditDocumentId: credit.creditDocumentId,
      creditDocumentType: credit.documentType,
      creditDocumentDisplay: credit.creditDocumentDisplay,
      creditDocumentNumber: credit.number ?? null,
      chargeDocumentId: charge.chargeDocumentId,
      chargeDocumentType: charge.documentType,
      chargeDisplay: charge.chargeDisplay,
      chargeNumber: charge.number ?? null,
      appliedOnUtc: '2026-04-07',
      amount,
      isPosted: true,
    },
  ]
  recalcPayablesOpenItemsTotals(details)

  return {
    applyId,
    creditDocumentId: credit.creditDocumentId,
    chargeDocumentId: charge.chargeDocumentId,
    appliedOnUtc: '2026-04-07',
    amount,
    createdDraft: false,
  }
}

function unapplyReceivablesAllocation(details: ReceivablesOpenItemsDetailsResponseDto, applyId: string): boolean {
  const allocation = details.allocations.find((item) => item.applyId === applyId) ?? null
  if (!allocation) return false

  details.allocations = details.allocations.filter((item) => item.applyId !== applyId)

  const charge = details.charges.find((item) => item.chargeDocumentId === allocation.chargeDocumentId) ?? null
  if (charge) {
    charge.outstandingAmount = Math.min(
      Number(charge.originalAmount ?? 0),
      Number(charge.outstandingAmount ?? 0) + Number(allocation.amount ?? 0),
    )
  }

  const credit = details.credits.find((item) => item.creditDocumentId === allocation.creditDocumentId) ?? null
  if (credit) {
    credit.availableCredit = Math.min(
      Number(credit.originalAmount ?? 0),
      Number(credit.availableCredit ?? 0) + Number(allocation.amount ?? 0),
    )
  }

  recalcReceivablesOpenItemsTotals(details)
  return true
}

function unapplyPayablesAllocation(details: PayablesOpenItemsDetailsResponseDto, applyId: string): boolean {
  const allocation = details.allocations.find((item) => item.applyId === applyId) ?? null
  if (!allocation) return false

  details.allocations = details.allocations.filter((item) => item.applyId !== applyId)

  const charge = details.charges.find((item) => item.chargeDocumentId === allocation.chargeDocumentId) ?? null
  if (charge) {
    charge.outstandingAmount = Math.min(
      Number(charge.originalAmount ?? 0),
      Number(charge.outstandingAmount ?? 0) + Number(allocation.amount ?? 0),
    )
  }

  const credit = details.credits.find((item) => item.creditDocumentId === allocation.creditDocumentId) ?? null
  if (credit) {
    credit.availableCredit = Math.min(
      Number(credit.originalAmount ?? 0),
      Number(credit.availableCredit ?? 0) + Number(allocation.amount ?? 0),
    )
  }

  recalcPayablesOpenItemsTotals(details)
  return true
}

export async function mockCommonPmApis(page: Page): Promise<void> {
  await page.route('**/api/main-menu', async (route) => {
    await fulfillJson(route, mainMenuFixture)
  })
}

export async function mockCommandPaletteApis(page: Page): Promise<void> {
  await page.route('**/api/report-definitions', async (route) => {
    const { pathname } = new URL(route.request().url())

    if (pathname !== '/api/report-definitions') {
      await route.fallback()
      return
    }

    await fulfillJson(route, [occupancySummaryReportDefinitionFixture])
  })
}

export async function mockHomeDashboardApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  await page.route('**/api/reports/pm.occupancy.summary/execute', async (route) => {
    await fulfillJson(route, homeOccupancySummaryFixture)
  })

  await page.route('**/api/reports/pm.maintenance.queue/execute', async (route) => {
    const body = parseRequestJson<{ filters?: Record<string, { value?: string | null } | null> }>(route)
    const queueState = String(body?.filters?.queue_state?.value ?? '').trim()
    await fulfillJson(route, queueState === 'Overdue' ? homeMaintenanceQueueOverdueFixture : homeMaintenanceQueueFixture)
  })

  await page.route('**/api/documents/pm.lease**', async (route) => {
    await fulfillJson(route, homeLeaseDocumentsFixture)
  })

  await page.route('**/api/documents/pm.rent_charge**', async (route) => {
    await fulfillJson(route, homeRentChargeDocumentsFixture)
  })

  await page.route('**/api/documents/pm.receivable_charge**', async (route) => {
    await fulfillJson(route, homeReceivableChargeDocumentsFixture)
  })

  await page.route('**/api/documents/pm.late_fee_charge**', async (route) => {
    await fulfillJson(route, homeLateFeeChargeDocumentsFixture)
  })

  await page.route('**/api/documents/pm.receivable_payment**', async (route) => {
    await fulfillJson(route, homeReceivablePaymentDocumentsFixture)
  })

  await page.route('**/api/documents/pm.receivable_returned_payment**', async (route) => {
    await fulfillJson(route, homeReturnedPaymentDocumentsFixture)
  })

  await page.route('**/api/receivables/reconciliation**', async (route) => {
    await fulfillJson(route, homeReceivablesReconciliationFixture)
  })

  await page.route('**/api/accounting/period-closing/calendar**', async (route) => {
    await fulfillJson(route, homePeriodClosingCalendarFixture)
  })
}

export async function mockReceivablesOpenItemsApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  await page.route('**/api/documents/pm.lease/*', async (route) => {
    await fulfillJson(route, receivablesLeaseFixture)
  })

  await page.route('**/api/receivables/open-items/details**', async (route) => {
    await fulfillJson(route, receivablesOpenItemsFixture)
  })
}

export async function mockPayablesOpenItemsApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  await page.route('**/api/catalogs/pm.party/*', async (route) => {
    await fulfillJson(route, payablesVendorFixture)
  })

  await page.route('**/api/catalogs/pm.property/*', async (route) => {
    await fulfillJson(route, payablesPropertyFixture)
  })

  await page.route('**/api/payables/open-items/details**', async (route) => {
    await fulfillJson(route, payablesOpenItemsFixture)
  })
}

export type MockReceivablesOpenItemsWorkflowApisResult = {
  getDetails: () => ReceivablesOpenItemsDetailsResponseDto
  getExecuteRequests: () => ReceivablesApplyBatchRequestDto[]
  getUnapplyIds: () => string[]
}

export async function mockReceivablesOpenItemsWorkflowApis(
  page: Page,
  options: MockReceivablesOpenItemsWorkflowApisOptions = {},
): Promise<MockReceivablesOpenItemsWorkflowApisResult> {
  await mockCommonPmApis(page)

  let details = structuredClone(options.initialDetails ?? receivablesOpenItemsFixture)
  let nextApplySequence = 2
  const executeRequests: ReceivablesApplyBatchRequestDto[] = []
  const unapplyIds: string[] = []

  await page.route('**/api/documents/pm.lease/*', async (route) => {
    await fulfillJson(route, receivablesLeaseFixture)
  })

  await page.route('**/api/receivables/open-items/details**', async (route) => {
    await fulfillJson(route, details)
  })

  await page.route('**/api/receivables/apply/fifo/suggest/lease', async (route) => {
    if (options.suggestFailure) {
      await fulfillApiFailure(route, options.suggestFailure, 'Could not build receivables apply suggestion')
      return
    }

    await fulfillJson(route, buildReceivablesSuggestResponse(details))
  })

  await page.route('**/api/receivables/apply/batch', async (route) => {
    const request = parseRequestJson<ReceivablesApplyBatchRequestDto>(route) ?? { applies: [] }
    executeRequests.push(structuredClone(request))

    if (options.executeFailure) {
      await fulfillApiFailure(route, options.executeFailure, 'Could not execute receivables apply batch')
      return
    }

    const executedApplies = (request.applies ?? [])
      .map(() => applyReceivablesSuggestion(details, nextReceivableApplyId(nextApplySequence++)))
      .filter((item): item is NonNullable<typeof item> => item !== null)

    await fulfillJson(route, {
      registerId: details.registerId,
      totalApplied: sumAmounts(executedApplies.map((item) => item.amount)),
      executedApplies,
    } satisfies ReceivablesApplyBatchResponseDto)
  })

  await page.route('**/api/receivables/apply/*/unapply', async (route) => {
    const { pathname } = new URL(route.request().url())
    const match = pathname.match(/^\/api\/receivables\/apply\/([^/]+)\/unapply$/)
    const applyId = decodeURIComponent(match?.[1] ?? '')
    unapplyIds.push(applyId)

    if (options.unapplyFailure) {
      await fulfillApiFailure(route, options.unapplyFailure, 'Could not unapply receivables allocation')
      return
    }

    if (!unapplyReceivablesAllocation(details, applyId)) {
      await fulfillJson(route, {
        title: 'Receivables apply not found',
        detail: applyId,
        status: 404,
      }, 404)
      return
    }

    await fulfillEmpty(route)
  })

  return {
    getDetails: () => structuredClone(details),
    getExecuteRequests: () => executeRequests.map((request) => structuredClone(request)),
    getUnapplyIds: () => [...unapplyIds],
  }
}

export type MockPayablesOpenItemsWorkflowApisResult = {
  getDetails: () => PayablesOpenItemsDetailsResponseDto
  getExecuteRequests: () => PayablesApplyBatchRequestDto[]
  getUnapplyIds: () => string[]
}

export async function mockPayablesOpenItemsWorkflowApis(
  page: Page,
  options: MockPayablesOpenItemsWorkflowApisOptions = {},
): Promise<MockPayablesOpenItemsWorkflowApisResult> {
  await mockCommonPmApis(page)

  let details = structuredClone(options.initialDetails ?? payablesOpenItemsFixture)
  let nextApplySequence = 2
  const executeRequests: PayablesApplyBatchRequestDto[] = []
  const unapplyIds: string[] = []

  await page.route('**/api/catalogs/pm.party/*', async (route) => {
    await fulfillJson(route, payablesVendorFixture)
  })

  await page.route('**/api/catalogs/pm.property/*', async (route) => {
    await fulfillJson(route, payablesPropertyFixture)
  })

  await page.route('**/api/payables/open-items/details**', async (route) => {
    await fulfillJson(route, details)
  })

  await page.route('**/api/payables/apply/fifo/suggest', async (route) => {
    if (options.suggestFailure) {
      await fulfillApiFailure(route, options.suggestFailure, 'Could not build payables apply suggestion')
      return
    }

    await fulfillJson(route, buildPayablesSuggestResponse(details))
  })

  await page.route('**/api/payables/apply/batch', async (route) => {
    const request = parseRequestJson<PayablesApplyBatchRequestDto>(route) ?? { applies: [] }
    executeRequests.push(structuredClone(request))

    if (options.executeFailure) {
      await fulfillApiFailure(route, options.executeFailure, 'Could not execute payables apply batch')
      return
    }

    const executedApplies = (request.applies ?? [])
      .map(() => applyPayablesSuggestion(details, nextPayableApplyId(nextApplySequence++)))
      .filter((item): item is NonNullable<typeof item> => item !== null)

    await fulfillJson(route, {
      registerId: details.registerId,
      totalApplied: sumAmounts(executedApplies.map((item) => item.amount)),
      executedApplies,
    } satisfies PayablesApplyBatchResponseDto)
  })

  await page.route('**/api/payables/apply/*/unapply', async (route) => {
    const { pathname } = new URL(route.request().url())
    const match = pathname.match(/^\/api\/payables\/apply\/([^/]+)\/unapply$/)
    const applyId = decodeURIComponent(match?.[1] ?? '')
    unapplyIds.push(applyId)

    if (options.unapplyFailure) {
      await fulfillApiFailure(route, options.unapplyFailure, 'Could not unapply payables allocation')
      return
    }

    if (!unapplyPayablesAllocation(details, applyId)) {
      await fulfillJson(route, {
        title: 'Payables apply not found',
        detail: applyId,
        status: 404,
      }, 404)
      return
    }

    await fulfillEmpty(route)
  })

  return {
    getDetails: () => structuredClone(details),
    getExecuteRequests: () => executeRequests.map((request) => structuredClone(request)),
    getUnapplyIds: () => [...unapplyIds],
  }
}

export async function mockReceivablesReconciliationApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  await page.route('**/api/receivables/reconciliation**', async (route) => {
    await fulfillJson(route, receivablesReconciliationFixture)
  })
}

export async function mockPayablesReconciliationApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  await page.route('**/api/payables/reconciliation**', async (route) => {
    await fulfillJson(route, payablesReconciliationFixture)
  })
}

export async function mockPropertiesApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  await page.route('**/api/catalogs/pm.property**', async (route) => {
    const { pathname, searchParams } = new URL(route.request().url())

    if (pathname !== '/api/catalogs/pm.property') {
      await route.fallback()
      return
    }

    const kind = String(searchParams.get('kind') ?? '')
    const parentPropertyId = String(searchParams.get('parent_property_id') ?? '')

    if (kind === 'Unit' && parentPropertyId) {
      await fulfillJson(route, propertyUnitsFixture)
      return
    }

    await fulfillJson(route, propertyBuildingsFixture)
  })

  await page.route('**/api/reports/pm.building.summary/execute', async (route) => {
    await fulfillJson(route, propertyBuildingSummaryFixture)
  })
}

export async function mockAccountingPolicyApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  await page.route('**/api/catalogs/pm.accounting_policy/metadata', async (route) => {
    await fulfillJson(route, accountingPolicyMetadataFixture)
  })

  await page.route('**/api/catalogs/pm.accounting_policy**', async (route) => {
    const { pathname } = new URL(route.request().url())

    if (pathname !== '/api/catalogs/pm.accounting_policy') {
      await route.fallback()
      return
    }

    await fulfillJson(route, accountingPolicyPageFixture)
  })
}

export async function mockGenericMetadataCatalogApis(
  page: Page,
  options: MockGenericMetadataCatalogApisOptions = {},
): Promise<void> {
  await mockCommonPmApis(page)

  let items = (options.initialItems ?? partyCatalogBaseItemsFixture).map((item) => structuredClone(item))

  await page.route('**/api/catalogs/pm.party/metadata', async (route) => {
    await fulfillJson(route, partyCatalogMetadataFixture)
  })

  await page.route('**/api/catalogs/pm.party**', async (route) => {
    const request = route.request()
    const { pathname } = new URL(request.url())

    if (pathname === '/api/catalogs/pm.party/metadata') {
      await route.fallback()
      return
    }

    if (pathname === '/api/catalogs/pm.party') {
      if (request.method() === 'GET') {
        await waitForMockDelay(options.pageDelayMs)

        if (options.pageFailure) {
          await fulfillApiFailure(route, options.pageFailure, 'Party register unavailable')
          return
        }

        await fulfillJson(route, createPartyCatalogPageFixture(items))
        return
      }

      if (request.method() === 'POST') {
        if (options.createFailure) {
          await fulfillApiFailure(route, options.createFailure, 'Could not create party')
          return
        }

        const payload = parseRequestJson<{ fields?: Record<string, unknown> | null }>(route)
        const fields = payload?.fields ?? {}
        const created = {
          ...createdPartyCatalogFixture,
          display: String(fields.display ?? createdPartyCatalogFixture.display ?? ''),
          payload: {
            fields: {
              display: String(fields.display ?? createdPartyCatalogFixture.payload.fields?.display ?? ''),
              party_type: String(fields.party_type ?? createdPartyCatalogFixture.payload.fields?.party_type ?? ''),
              email: String(fields.email ?? createdPartyCatalogFixture.payload.fields?.email ?? ''),
            },
          },
        }

        items = [...items, created]
        await fulfillJson(route, created)
        return
      }
    }

    const markMatch = pathname.match(/^\/api\/catalogs\/pm\.party\/([^/]+)\/mark-for-deletion$/)
    if (markMatch && request.method() === 'POST') {
      const itemId = markMatch[1] ?? ''
      items = items.map((entry) => (entry.id === itemId ? { ...entry, isMarkedForDeletion: true } : entry))
      await fulfillEmpty(route)
      return
    }

    const unmarkMatch = pathname.match(/^\/api\/catalogs\/pm\.party\/([^/]+)\/unmark-for-deletion$/)
    if (unmarkMatch && request.method() === 'POST') {
      const itemId = unmarkMatch[1] ?? ''
      items = items.map((entry) => (entry.id === itemId ? { ...entry, isMarkedForDeletion: false } : entry))
      await fulfillEmpty(route)
      return
    }

    const detailsMatch = pathname.match(/^\/api\/catalogs\/pm\.party\/([^/]+)$/)
    if (detailsMatch) {
      const itemId = detailsMatch[1] ?? ''
      const current = items.find((entry) => entry.id === itemId) ?? createdPartyCatalogFixture

      if (request.method() === 'GET') {
        await waitForMockDelay(options.detailsDelayMs)
        if (options.detailsFailure) {
          await fulfillApiFailure(route, options.detailsFailure, 'Party details unavailable')
          return
        }
        await fulfillJson(route, current)
        return
      }

      if (request.method() === 'PUT') {
        if (options.updateFailure) {
          await fulfillApiFailure(route, options.updateFailure, 'Could not update party')
          return
        }

        const payload = parseRequestJson<{ fields?: Record<string, unknown> | null }>(route)
        const fields = payload?.fields ?? {}
        const updated = {
          ...current,
          display: String(fields.display ?? current.display ?? ''),
          payload: {
            fields: {
              ...(current.payload.fields ?? {}),
              display: String(fields.display ?? current.payload.fields?.display ?? ''),
              party_type: String(fields.party_type ?? current.payload.fields?.party_type ?? ''),
              email: String(fields.email ?? current.payload.fields?.email ?? ''),
            },
          },
        }

        items = items.map((entry) => (entry.id === itemId ? updated : entry))
        await fulfillJson(route, updated)
        return
      }
    }

    await route.fallback()
  })
}

export async function mockGenericMetadataDocumentApis(
  page: Page,
  options: MockGenericMetadataDocumentApisOptions = {},
): Promise<void> {
  await mockCommonPmApis(page)

  let documents = (options.initialDocuments ?? receivablePaymentBaseDocumentsFixture)
    .map((item) => structuredClone(item))

  await page.route('**/api/documents/pm.receivable_payment/metadata', async (route) => {
    await fulfillJson(route, receivablePaymentMetadataFixture)
  })

  await page.route('**/api/documents/pm.receivable_payment**', async (route) => {
    const request = route.request()
    const { pathname } = new URL(request.url())

    if (pathname === '/api/documents/pm.receivable_payment/metadata') {
      await route.fallback()
      return
    }

    if (pathname === '/api/documents/pm.receivable_payment') {
      if (request.method() === 'GET') {
        await waitForMockDelay(options.pageDelayMs)

        if (options.pageFailure) {
          await fulfillApiFailure(route, options.pageFailure, 'Receivable payments unavailable')
          return
        }

        await fulfillJson(route, createReceivablePaymentPageFixture(documents))
        return
      }

      if (request.method() === 'POST') {
        if (options.createFailure) {
          await fulfillApiFailure(route, options.createFailure, 'Could not create receivable payment')
          return
        }

        const payload = parseRequestJson<{ fields?: Record<string, unknown> | null }>(route)
        const fields = payload?.fields ?? {}
        const created = {
          ...createdReceivablePaymentFixture,
          display: String(fields.display ?? createdReceivablePaymentFixture.display ?? ''),
          payload: {
            fields: {
              display: String(fields.display ?? createdReceivablePaymentFixture.payload.fields?.display ?? ''),
              payment_reference: String(fields.payment_reference ?? createdReceivablePaymentFixture.payload.fields?.payment_reference ?? ''),
              memo: String(fields.memo ?? createdReceivablePaymentFixture.payload.fields?.memo ?? ''),
            },
          },
        }

        documents = [...documents, created]
        await fulfillJson(route, created)
        return
      }
    }

    const graphMatch = pathname.match(/^\/api\/documents\/pm\.receivable_payment\/([^/]+)\/graph$/)
    if (graphMatch && request.method() === 'GET') {
      await fulfillJson(route, {
        ...receivablePaymentGraphFixture,
        nodes: receivablePaymentGraphFixture.nodes.map((node) =>
          node.nodeId === 'payment-root'
            ? { ...node, entityId: graphMatch[1] ?? node.entityId }
            : node),
      })
      return
    }

    const effectsMatch = pathname.match(/^\/api\/documents\/pm\.receivable_payment\/([^/]+)\/effects$/)
    if (effectsMatch && request.method() === 'GET') {
      await fulfillJson(route, receivablePaymentEffectsFixture)
      return
    }

    const detailsMatch = pathname.match(/^\/api\/documents\/pm\.receivable_payment\/([^/]+)$/)
    if (detailsMatch) {
      const documentId = detailsMatch[1] ?? ''
      const current = documents.find((entry) => entry.id === documentId) ?? createdReceivablePaymentFixture

      if (request.method() === 'GET') {
        await waitForMockDelay(options.detailsDelayMs)
        if (options.detailsFailure) {
          await fulfillApiFailure(route, options.detailsFailure, 'Receivable payment details unavailable')
          return
        }
        await fulfillJson(route, current)
        return
      }

      if (request.method() === 'PUT') {
        if (options.updateFailure) {
          await fulfillApiFailure(route, options.updateFailure, 'Could not update receivable payment')
          return
        }

        const payload = parseRequestJson<{ fields?: Record<string, unknown> | null }>(route)
        const fields = payload?.fields ?? {}
        const updated = {
          ...current,
          display: String(fields.display ?? current.display ?? ''),
          payload: {
            fields: {
              ...(current.payload.fields ?? {}),
              display: String(fields.display ?? current.payload.fields?.display ?? ''),
              payment_reference: String(fields.payment_reference ?? current.payload.fields?.payment_reference ?? ''),
              memo: String(fields.memo ?? current.payload.fields?.memo ?? ''),
            },
          },
        }

        documents = documents.map((entry) => (entry.id === documentId ? updated : entry))
        await fulfillJson(route, updated)
        return
      }
    }

    await route.fallback()
  })

  await page.route('**/api/audit/entities/*/*', async (route) => {
    const { pathname } = new URL(route.request().url())
    const match = pathname.match(/^\/api\/audit\/entities\/([^/]+)\/([^/]+)$/)

    if (!match) {
      await route.fallback()
      return
    }

    const entityKind = String(match[1] ?? '')
    const entityId = String(match[2] ?? '')

    if (entityKind === '1' && (
      entityId === receivablePaymentBaseDocumentsFixture[0]?.id
      || entityId === createdReceivablePaymentFixture.id
    )) {
      await fulfillJson(route, {
        ...receivablePaymentAuditFixture,
        items: receivablePaymentAuditFixture.items.map((item) => ({
          ...item,
          entityId,
        })),
      })
      return
    }

    await fulfillJson(route, {
      items: [],
      nextCursor: null,
      limit: 100,
    })
  })
}

export type MockOccupancySummaryReportApisResult = {
  getExecuteRequests: () => ReportExecutionRequestDto[]
  getLastExecuteRequest: () => ReportExecutionRequestDto | null
  getVariants: () => ReportVariantDto[]
}

type MockOccupancySummaryReportApisOptions = {
  initialVariants?: ReportVariantDto[]
  executionResponse?: ReportExecutionResponseDto
  appendResponsesByCursor?: Record<string, ReportExecutionResponseDto>
  executeFailure?: MockApiFailure | null
  appendFailuresByCursor?: Record<string, MockApiFailure | null>
  saveVariantFailure?: MockApiFailure | null
  deleteVariantFailure?: MockApiFailure | null
}

export async function mockOccupancySummaryReportApis(
  page: Page,
  options: MockOccupancySummaryReportApisOptions = {},
): Promise<MockOccupancySummaryReportApisResult> {
  await mockCommonPmApis(page)

  let variants = (options.initialVariants ?? occupancySummaryReportVariantsFixture).map((variant) => ({ ...variant }))
  const executeRequests: ReportExecutionRequestDto[] = []
  let lastExecuteRequest: ReportExecutionRequestDto | null = null

  await page.route('**/api/report-definitions/pm.occupancy.summary', async (route) => {
    await fulfillJson(route, occupancySummaryReportDefinitionFixture)
  })

  await page.route('**/api/reports/pm.occupancy.summary/variants**', async (route) => {
    const request = route.request()
    const { pathname } = new URL(request.url())

    if (pathname === '/api/reports/pm.occupancy.summary/variants' && request.method() === 'GET') {
      await fulfillJson(route, variants)
      return
    }

    const variantMatch = pathname.match(/^\/api\/reports\/pm\.occupancy\.summary\/variants\/([^/]+)$/)
    if (!variantMatch) {
      await route.fallback()
      return
    }

    const variantCode = decodeURIComponent(variantMatch[1] ?? '').trim()
    if (!variantCode) {
      await route.fallback()
      return
    }

    if (request.method() === 'GET') {
      const variant = variants.find((entry) => entry.variantCode === variantCode)
      await fulfillJson(route, variant ?? {
        title: 'Missing test variant',
        detail: variantCode,
        status: 404,
      }, variant ? 200 : 404)
      return
    }

    if (request.method() === 'PUT') {
      if (options.saveVariantFailure) {
        await fulfillApiFailure(route, options.saveVariantFailure, 'Could not save report variant')
        return
      }

      const payload = parseRequestJson<ReportVariantDto>(route)
      const nextVariant: ReportVariantDto = {
        ...(payload ?? {
          reportCode: occupancySummaryReportDefinitionFixture.reportCode,
          name: variantCode,
          layout: null,
          filters: null,
          parameters: null,
          isDefault: false,
          isShared: true,
        }),
        reportCode: occupancySummaryReportDefinitionFixture.reportCode,
        variantCode,
      }

      variants = [
        ...variants.filter((entry) => entry.variantCode !== variantCode),
        nextVariant,
      ]

      if (nextVariant.isDefault) {
        variants = variants.map((entry) => ({
          ...entry,
          isDefault: entry.variantCode === variantCode,
        }))
      }

      await fulfillJson(route, nextVariant)
      return
    }

    if (request.method() === 'DELETE') {
      if (options.deleteVariantFailure) {
        await fulfillApiFailure(route, options.deleteVariantFailure, 'Could not delete report variant')
        return
      }

      variants = variants.filter((entry) => entry.variantCode !== variantCode)
      await fulfillEmpty(route)
      return
    }

    await route.fallback()
  })

  await page.route('**/api/reports/pm.occupancy.summary/execute', async (route) => {
    const payload = parseRequestJson<ReportExecutionRequestDto>(route)
    if (payload) {
      lastExecuteRequest = payload
      executeRequests.push(payload)
    }

    const cursor = String(payload?.cursor ?? '').trim()
    if (cursor.length > 0) {
      const failure = options.appendFailuresByCursor?.[cursor] ?? null
      if (failure) {
        await fulfillApiFailure(route, failure, 'Failed to load more rows.')
        return
      }

      const appendResponse = options.appendResponsesByCursor?.[cursor] ?? null
      if (appendResponse) {
        await fulfillJson(route, appendResponse)
        return
      }

      await fulfillJson(route, occupancySummaryReportPagedSecondExecutionFixture)
      return
    }

    if (options.executeFailure) {
      await fulfillApiFailure(route, options.executeFailure, 'Failed to execute the report.')
      return
    }

    await fulfillJson(route, options.executionResponse ?? occupancySummaryReportExecutionFixture)
  })

  return {
    getExecuteRequests: () => executeRequests.map((request) => structuredClone(request)),
    getLastExecuteRequest: () => (lastExecuteRequest ? structuredClone(lastExecuteRequest) : null),
    getVariants: () => variants.map((variant) => structuredClone(variant)),
  }
}

export async function mockAccountingPeriodClosingApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  let calendar = createPeriodClosingCalendarFixture()
  const fiscalStatus = createFiscalYearCloseStatusFixture()

  await page.route('**/api/accounting/period-closing/calendar**', async (route) => {
    await fulfillJson(route, calendar)
  })

  await page.route('**/api/accounting/period-closing/fiscal-year**', async (route) => {
    await fulfillJson(route, fiscalStatus)
  })

  await page.route('**/api/accounting/period-closing/retained-earnings-accounts**', async (route) => {
    await fulfillJson(route, retainedEarningsAccountsFixture)
  })

  await page.route('**/api/accounting/period-closing/month/close', async (route) => {
    calendar = createPeriodClosingCalendarFixture({ aprilClosed: true })
    await fulfillJson(route, calendar.months.find((month) => month.period === '2026-04-01') ?? calendar.months[3])
  })
}

export async function mockGeneralJournalEntryApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  let savedDraft = createSavedGeneralJournalEntryDraftFixture()

  await page.route('**/api/accounting/general-journal-entries**', async (route) => {
    const request = route.request()
    const { pathname } = new URL(request.url())

    if (pathname === '/api/accounting/general-journal-entries') {
      if (request.method() === 'GET') {
        await fulfillJson(route, generalJournalEntriesPageFixture)
        return
      }

      if (request.method() === 'POST') {
        await fulfillJson(route, createdGeneralJournalEntryDraftFixture)
        return
      }
    }

    const headerMatch = pathname.match(/^\/api\/accounting\/general-journal-entries\/([^/]+)\/header$/)
    if (headerMatch && request.method() === 'PUT') {
      const body = parseRequestJson<{
        memo?: string | null
        reasonCode?: string | null
        externalReference?: string | null
      }>(route)

      savedDraft = createSavedGeneralJournalEntryDraftFixture({
        memo: body?.memo ?? null,
        reasonCode: body?.reasonCode ?? null,
        externalReference: body?.externalReference ?? null,
      })

      await fulfillJson(route, savedDraft)
      return
    }

    const linesMatch = pathname.match(/^\/api\/accounting\/general-journal-entries\/([^/]+)\/lines$/)
    if (linesMatch && request.method() === 'PUT') {
      await fulfillJson(route, savedDraft)
      return
    }

    const detailsMatch = pathname.match(/^\/api\/accounting\/general-journal-entries\/([^/]+)$/)
    if (detailsMatch && request.method() === 'GET') {
      await fulfillJson(route, savedDraft)
      return
    }

    await route.fallback()
  })
}

export async function mockChartOfAccountsApis(page: Page): Promise<void> {
  await mockCommonPmApis(page)

  let accounts = createChartOfAccountsPageFixture().items.slice()

  await page.route('**/api/chart-of-accounts/metadata', async (route) => {
    await fulfillJson(route, chartOfAccountsMetadataFixture)
  })

  await page.route('**/api/chart-of-accounts**', async (route) => {
    const request = route.request()
    const { pathname } = new URL(request.url())

    if (pathname === '/api/chart-of-accounts') {
      if (request.method() === 'GET') {
        await fulfillJson(route, createChartOfAccountsPageFixture(accounts))
        return
      }

      if (request.method() === 'POST') {
        accounts = [...accounts, createdChartOfAccountFixture]
        await fulfillJson(route, createdChartOfAccountFixture)
        return
      }
    }

    const detailsMatch = pathname.match(/^\/api\/chart-of-accounts\/([^/]+)$/)
    if (detailsMatch && request.method() === 'GET') {
      const account = accounts.find((entry) => entry.accountId === detailsMatch[1]) ?? createdChartOfAccountFixture
      await fulfillJson(route, account)
      return
    }

    if (detailsMatch && request.method() === 'PUT') {
      const accountId = detailsMatch[1] ?? ''
      const current = accounts.find((entry) => entry.accountId === accountId)
      const payload = parseRequestJson<ChartOfAccountsUpsertRequestDto>(route)

      const nextAccount = {
        ...(current ?? createdChartOfAccountFixture),
        accountId,
        code: String(payload?.code ?? current?.code ?? createdChartOfAccountFixture.code),
        name: String(payload?.name ?? current?.name ?? createdChartOfAccountFixture.name),
        accountType: String(payload?.accountType ?? current?.accountType ?? createdChartOfAccountFixture.accountType),
        cashFlowRole: payload?.cashFlowRole ?? current?.cashFlowRole ?? null,
        cashFlowLineCode: payload?.cashFlowLineCode ?? current?.cashFlowLineCode ?? null,
        isActive: payload?.isActive ?? current?.isActive ?? true,
        isDeleted: current?.isDeleted ?? false,
        isMarkedForDeletion: current?.isMarkedForDeletion ?? false,
      }

      accounts = [
        ...accounts.filter((entry) => entry.accountId !== accountId),
        nextAccount,
      ]

      await fulfillJson(route, nextAccount)
      return
    }

    await route.fallback()
  })
}

export async function rejectUnhandledApiRequests(page: Page, allowedPathPrefixes: readonly string[]): Promise<void> {
  await page.route('**/*', async (route) => {
    const request = route.request()
    const { pathname } = new URL(request.url())

    if (!pathname.startsWith('/api/')) {
      await route.fallback()
      return
    }

    if (allowedPathPrefixes.some((prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`))) {
      await route.fallback()
      return
    }

    await fulfillJson(route, {
      title: 'Unhandled test API request',
      detail: `${request.method()} ${pathname}`,
      status: 501,
    }, 501)
  })
}
