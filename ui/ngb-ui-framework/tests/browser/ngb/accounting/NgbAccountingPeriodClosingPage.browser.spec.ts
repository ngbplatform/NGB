import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { ApiError } from '../../../../src/ngb/api/http'
import {
  StubBadge,
  StubButton,
  StubConfirmDialog,
  StubIcon,
  StubInput,
  StubLookup,
  StubMonthPicker,
  StubPageHeader,
  StubValidationSummary,
} from './stubs'

const periodMocks = vi.hoisted(() => ({
  closeFiscalYear: vi.fn(),
  closeMonth: vi.fn(),
  copyAppLink: vi.fn(),
  getFiscalYearCloseStatus: vi.fn(),
  getPeriodClosingCalendar: vi.fn(),
  navigateBack: vi.fn(),
  reopenFiscalYear: vi.fn(),
  reopenMonth: vi.fn(),
  searchRetainedEarningsAccounts: vi.fn(),
  toasts: {
    push: vi.fn(),
  },
  state: {
    closedMonths: [1, 2] as number[],
    fiscalClosed: false,
    fiscalRetainedEarningsAccountId: null as string | null,
    retainedOptions: [
      {
        accountId: 'retained-earnings',
        code: '3200',
        name: 'Retained Earnings',
        display: '3200 · Retained Earnings',
      },
      {
        accountId: 'owner-equity',
        code: '3300',
        name: 'Owner Equity',
        display: '3300 · Owner Equity',
      },
    ],
  },
}))

vi.mock('../../../../src/ngb/accounting/periodClosingApi', () => ({
  closeFiscalYear: periodMocks.closeFiscalYear,
  closeMonth: periodMocks.closeMonth,
  getFiscalYearCloseStatus: periodMocks.getFiscalYearCloseStatus,
  getPeriodClosingCalendar: periodMocks.getPeriodClosingCalendar,
  reopenFiscalYear: periodMocks.reopenFiscalYear,
  reopenMonth: periodMocks.reopenMonth,
  searchRetainedEarningsAccounts: periodMocks.searchRetainedEarningsAccounts,
}))

vi.mock('../../../../src/ngb/router/backNavigation', () => ({
  navigateBack: periodMocks.navigateBack,
}))

vi.mock('../../../../src/ngb/router/shareLink', () => ({
  copyAppLink: periodMocks.copyAppLink,
}))

vi.mock('../../../../src/ngb/primitives/toast', () => ({
  useToasts: () => periodMocks.toasts,
}))

vi.mock('../../../../src/ngb/components/NgbConfirmDialog.vue', () => ({
  default: StubConfirmDialog,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/primitives/NgbButton.vue', () => ({
  default: StubButton,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/primitives/NgbInput.vue', () => ({
  default: StubInput,
}))

vi.mock('../../../../src/ngb/primitives/NgbLookup.vue', () => ({
  default: StubLookup,
}))

vi.mock('../../../../src/ngb/primitives/NgbMonthPicker.vue', () => ({
  default: StubMonthPicker,
}))

vi.mock('../../../../src/ngb/layout/NgbPageHeader.vue', () => ({
  default: StubPageHeader,
}))

vi.mock('../../../../src/ngb/components/forms/NgbValidationSummary.vue', () => ({
  default: StubValidationSummary,
}))

import NgbAccountingPeriodClosingPage from '../../../../src/ngb/accounting/NgbAccountingPeriodClosingPage.vue'

type CalendarOptions = {
  year: number
  closedMonths: number[]
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

function monthValue(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`
}

function periodDate(year: number, month: number): string {
  return `${monthValue(year, month)}-01`
}

function parseMonthNumber(periodOrMonth: string): number {
  return Number(String(periodOrMonth).slice(5, 7))
}

function buildCalendar(options: CalendarOptions) {
  const closed = new Set(options.closedMonths)
  const latestClosed = options.closedMonths.length > 0
    ? Math.max(...options.closedMonths)
    : null

  let latestContiguous = 0
  while (closed.has(latestContiguous + 1)) latestContiguous += 1

  const nextClosable = latestContiguous < 12 ? latestContiguous + 1 : null
  const hasBrokenChain = latestClosed != null && latestClosed !== latestContiguous

  return {
    year: options.year,
    yearStartPeriod: periodDate(options.year, 1),
    yearEndPeriod: periodDate(options.year, 12),
    earliestActivityPeriod: periodDate(options.year, 1),
    latestContiguousClosedPeriod: latestContiguous > 0 ? periodDate(options.year, latestContiguous) : null,
    latestClosedPeriod: latestClosed != null ? periodDate(options.year, latestClosed) : null,
    nextClosablePeriod: nextClosable != null ? periodDate(options.year, nextClosable) : null,
    canCloseAnyMonth: latestContiguous === 0,
    hasBrokenChain,
    firstGapPeriod: hasBrokenChain ? periodDate(options.year, latestContiguous + 1) : null,
    months: Array.from({ length: 12 }, (_, index) => {
      const month = index + 1
      const isClosed = closed.has(month)
      const canClose = month === nextClosable
      const canReopen = isClosed && month === latestClosed

      return {
        period: periodDate(options.year, month),
        state: isClosed
          ? month > latestContiguous
            ? 'ClosedOutOfSequence'
            : 'Closed'
          : canClose
            ? 'ReadyToClose'
            : nextClosable != null && month > nextClosable
              ? 'BlockedByEarlierOpenMonth'
              : 'Open',
        isClosed,
        hasActivity: month <= 4,
        closedBy: isClosed ? `closer-${month}` : null,
        closedAtUtc: isClosed ? `2026-${String(month).padStart(2, '0')}-28T12:00:00Z` : null,
        canClose,
        canReopen,
        blockingPeriod: !isClosed && !canClose && nextClosable != null && month > nextClosable
          ? periodDate(options.year, nextClosable)
          : null,
        blockingReason: !isClosed && !canClose && nextClosable != null && month > nextClosable
          ? 'EarlierOpenMonth'
          : null,
      }
    }),
  }
}

function buildFiscalStatus(fiscalYearEndPeriod: string) {
  const year = Number(fiscalYearEndPeriod.slice(0, 4))
  const endMonth = parseMonthNumber(fiscalYearEndPeriod)
  const closed = new Set(periodMocks.state.closedMonths)
  const firstMissingPriorMonth = Array.from({ length: Math.max(endMonth - 1, 0) }, (_, index) => index + 1)
    .find((month) => !closed.has(month)) ?? null
  const completed = periodMocks.state.fiscalClosed

  return {
    fiscalYearEndPeriod,
    fiscalYearStartPeriod: periodDate(year, 1),
    state: completed
      ? 'Completed'
      : firstMissingPriorMonth == null
        ? 'Ready'
        : 'BlockedByEarlierOpenMonth',
    documentId: 'fy-close-1',
    startedAtUtc: completed ? '2026-12-31T18:00:00Z' : null,
    completedAtUtc: completed ? '2026-12-31T18:05:00Z' : null,
    endPeriodClosed: completed,
    endPeriodClosedBy: completed ? 'fy-closer' : null,
    endPeriodClosedAtUtc: completed ? '2026-12-31T18:05:00Z' : null,
    canClose: !completed && firstMissingPriorMonth == null,
    canReopen: completed,
    reopenWillOpenEndPeriod: completed,
    closedRetainedEarningsAccount: completed
      ? periodMocks.state.retainedOptions.find((item) => item.accountId === periodMocks.state.fiscalRetainedEarningsAccountId)
        ?? periodMocks.state.retainedOptions[0]
      : null,
    blockingPeriod: !completed && firstMissingPriorMonth != null
      ? periodDate(year, firstMissingPriorMonth)
      : null,
    blockingReason: !completed && firstMissingPriorMonth != null
      ? 'EarlierOpenMonth'
      : null,
    reopenBlockingPeriod: null,
    reopenBlockingReason: null,
    priorMonths: Array.from({ length: Math.max(endMonth - 1, 0) }, (_, index) => {
      const month = index + 1
      return {
        period: periodDate(year, month),
        state: closed.has(month) ? 'Closed' : 'Open',
        isClosed: closed.has(month),
        hasActivity: true,
        closedBy: closed.has(month) ? `closer-${month}` : null,
        closedAtUtc: closed.has(month) ? `2026-${String(month).padStart(2, '0')}-28T12:00:00Z` : null,
        canClose: false,
        canReopen: false,
        blockingPeriod: null,
        blockingReason: null,
      }
    }),
  }
}

function makeApiError(options: {
  errorCode?: string
  context?: Record<string, unknown>
  issues?: Array<{ message?: string }>
  errors?: Record<string, string[]>
  message?: string
}) {
  return new ApiError({
    message: options.message ?? 'Request failed.',
    status: 400,
    url: '/api/accounting/period-closing',
    body: {
      errorCode: options.errorCode,
      context: options.context,
      issues: options.issues,
      errors: options.errors,
    },
  })
}

async function flushUi() {
  await new Promise((resolve) => window.setTimeout(resolve, 60))
}

function clickButtonByTitle(title: string) {
  const button = document.querySelector(`button[title="${title}"]`)
  if (!(button instanceof HTMLButtonElement)) throw new Error(`Button with title "${title}" not found.`)
  button.click()
}

function clickActionButton(label: string) {
  const button = Array.from(document.querySelectorAll('button[data-variant]'))
    .find((candidate) => candidate.textContent?.trim() === label)
  if (!(button instanceof HTMLButtonElement)) throw new Error(`Action button "${label}" not found.`)
  button.click()
}

async function renderPage(initialUrl: string, backTarget: string | null = '/dashboard') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/admin/accounting/period-closing',
        component: NgbAccountingPeriodClosingPage,
      },
      {
        path: '/dashboard',
        component: {
          template: '<div>Dashboard</div>',
        },
      },
    ],
  })

  await router.push(initialUrl)
  await router.isReady()

  const view = await render(NgbAccountingPeriodClosingPage, {
    props: {
      backTarget,
    },
    global: {
      plugins: [router],
    },
  })

  await flushUi()

  return {
    router,
    view,
  }
}

beforeEach(() => {
  vi.clearAllMocks()

  periodMocks.state.closedMonths = [1, 2]
  periodMocks.state.fiscalClosed = false
  periodMocks.state.fiscalRetainedEarningsAccountId = null

  periodMocks.copyAppLink.mockResolvedValue(true)

  periodMocks.getPeriodClosingCalendar.mockImplementation(async (year: number) =>
    clone(buildCalendar({
      year,
      closedMonths: periodMocks.state.closedMonths,
    })))

  periodMocks.getFiscalYearCloseStatus.mockImplementation(async (fiscalYearEndPeriod: string) =>
    clone(buildFiscalStatus(fiscalYearEndPeriod)))

  periodMocks.searchRetainedEarningsAccounts.mockImplementation(async ({ query }: { query?: string }) => {
    const normalized = String(query ?? '').trim().toLowerCase()
    return clone(
      periodMocks.state.retainedOptions.filter((item) =>
        normalized.length === 0
        || item.display.toLowerCase().includes(normalized)
        || item.code.toLowerCase().includes(normalized),
      ),
    )
  })

  periodMocks.closeMonth.mockImplementation(async ({ period }: { period: string }) => {
    const targetMonth = parseMonthNumber(period)
    periodMocks.state.closedMonths = Array.from(new Set([
      ...periodMocks.state.closedMonths,
      targetMonth,
    ])).sort((left, right) => left - right)
  })

  periodMocks.reopenMonth.mockImplementation(async ({ period }: { period: string }) => {
    const targetMonth = parseMonthNumber(period)
    periodMocks.state.closedMonths = periodMocks.state.closedMonths.filter((month) => month !== targetMonth)
  })

  periodMocks.closeFiscalYear.mockImplementation(async ({
    retainedEarningsAccountId,
  }: {
    retainedEarningsAccountId: string
  }) => {
    periodMocks.state.fiscalClosed = true
    periodMocks.state.fiscalRetainedEarningsAccountId = retainedEarningsAccountId
  })

  periodMocks.reopenFiscalYear.mockImplementation(async () => {
    periodMocks.state.fiscalClosed = false
  })
})

test('syncs route query across year and month controls, and wires share/back actions', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-12')

  expect(periodMocks.getPeriodClosingCalendar).toHaveBeenCalledWith(2026)
  expect(periodMocks.getFiscalYearCloseStatus).toHaveBeenCalledWith('2026-12-01')
  await expect.element(view.getByText('Year: 2026')).toBeVisible()

  clickButtonByTitle('Previous year')
  await flushUi()

  expect(router.currentRoute.value.query.year).toBe('2025')
  expect(router.currentRoute.value.query.month).toBe('2025-03')
  expect(router.currentRoute.value.query.fy).toBe('2025-12')
  expect(periodMocks.getPeriodClosingCalendar).toHaveBeenLastCalledWith(2025)

  clickButtonByTitle('Next year')
  await flushUi()
  expect(router.currentRoute.value.query.year).toBe('2026')

  clickButtonByTitle('Previous year')
  await flushUi()
  expect(router.currentRoute.value.query.year).toBe('2025')

  await view.getByText('month-picker:2025-03').click()
  await flushUi()

  expect(router.currentRoute.value.query.year).toBe('2025')
  expect(router.currentRoute.value.query.month).toBe('2025-04')
  expect(router.currentRoute.value.query.fy).toBe('2025-12')

  await view.getByText('month-picker:2025-12').click()
  await flushUi()

  expect(router.currentRoute.value.query.year).toBe('2026')
  expect(router.currentRoute.value.query.month).toBe('2026-04')
  expect(router.currentRoute.value.query.fy).toBe('2026-01')
  expect(periodMocks.getFiscalYearCloseStatus).toHaveBeenLastCalledWith('2026-01-01')

  clickButtonByTitle('Share link')
  expect(periodMocks.copyAppLink).toHaveBeenCalledWith(
    router,
    periodMocks.toasts,
    '/admin/accounting/period-closing?year=2026&month=2026-04&fy=2026-01',
  )

  await view.getByRole('button', { name: 'Header back' }).click()
  expect(periodMocks.navigateBack).toHaveBeenCalledTimes(1)
  expect(periodMocks.navigateBack.mock.calls[0]?.[2]).toBe('/dashboard')
})

test('closes and reopens a month through confirmation dialogs and refreshes the workspace state', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

  await expect.element(view.getByText('lookup-value:3200 · Retained Earnings')).toBeVisible()

  clickActionButton('Close Month')
  await expect.element(view.getByText('Close month?')).toBeVisible()
  await view.getByRole('button', { name: 'Dialog cancel' }).click()
  clickActionButton('Close Month')
  await view.getByRole('button', { name: 'Dialog confirm:Close Month' }).click()
  await flushUi()

  expect(periodMocks.closeMonth).toHaveBeenCalledWith({
    period: '2026-03-01',
  })
  expect(periodMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Month closed',
    tone: 'success',
  }))
  await expect.element(view.getByPlaceholder('Explain why the chain needs to be reopened')).toBeVisible()

  await view.getByPlaceholder('Explain why the chain needs to be reopened').fill('Need to fix accruals')
  clickActionButton('Reopen Month')
  await view.getByRole('button', { name: 'Dialog cancel' }).click()
  clickActionButton('Reopen Month')
  await view.getByRole('button', { name: 'Dialog confirm:Reopen Month' }).click()
  await flushUi()

  expect(periodMocks.reopenMonth).toHaveBeenCalledWith({
    period: '2026-03-01',
    reason: 'Need to fix accruals',
  })
  expect(periodMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Month reopened',
    tone: 'success',
  }))
})

test('closes and reopens a fiscal year using the retained earnings selection', async () => {
  await page.viewport(1280, 900)

  periodMocks.state.closedMonths = [1, 2]

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

  await expect.element(view.getByText('lookup-value:3200 · Retained Earnings')).toBeVisible()
  await expect.element(view.getByText('All prior months from January through the month before the selected end period are already closed.')).toBeVisible()

  clickActionButton('Close Fiscal Year')
  await expect.element(view.getByText('Close fiscal year?')).toBeVisible()
  await view.getByRole('button', { name: 'Dialog cancel' }).click()
  clickActionButton('Close Fiscal Year')
  await view.getByRole('button', { name: 'Dialog confirm:Close Fiscal Year' }).click()
  await flushUi()

  expect(periodMocks.closeFiscalYear).toHaveBeenCalledWith({
    fiscalYearEndPeriod: '2026-03-01',
    retainedEarningsAccountId: 'retained-earnings',
  })
  expect(periodMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Fiscal year closed',
    tone: 'success',
  }))
  await expect.element(view.getByText('Closed using 3200 · Retained Earnings.')).toBeVisible()

  await view.getByPlaceholder('Explain why this fiscal year close must be reopened').fill('Need to rerun year close')
  clickActionButton('Reopen Fiscal Year')
  await view.getByRole('button', { name: 'Dialog cancel' }).click()
  clickActionButton('Reopen Fiscal Year')
  await view.getByRole('button', { name: 'Dialog confirm:Reopen Fiscal Year' }).click()
  await flushUi()

  expect(periodMocks.reopenFiscalYear).toHaveBeenCalledWith({
    fiscalYearEndPeriod: '2026-03-01',
    reason: 'Need to rerun year close',
  })
  expect(periodMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Fiscal year reopened',
    tone: 'success',
  }))
})

test('maps ApiError codes into friendly validation text when month reopen fails', async () => {
  await page.viewport(1280, 900)

  periodMocks.state.closedMonths = [1, 2, 3]
  periodMocks.reopenMonth.mockRejectedValueOnce(new ApiError({
    message: 'blocked',
    status: 400,
    url: '/api/accounting/period-closing/reopen-month',
    body: {
      errorCode: 'period.month.reopen.latest_closed_required',
      context: {
        latestClosedPeriod: '2026-04-01',
      },
    },
  }))

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

  await view.getByPlaceholder('Explain why the chain needs to be reopened').fill('Need to adjust')
  clickActionButton('Reopen Month')
  await view.getByRole('button', { name: 'Dialog confirm:Reopen Month' }).click()
  await flushUi()

  await expect.element(view.getByText('Only the latest closed month can be reopened right now. Current edge: April 2026.')).toBeVisible()
})

test('maps month close prerequisite failures into friendly validation text', async () => {
  await page.viewport(1280, 900)

  periodMocks.closeMonth.mockRejectedValueOnce(makeApiError({
    errorCode: 'period.month.prerequisite_not_met',
    context: {
      nextClosablePeriod: '2026-02-01',
    },
  }))

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

  clickActionButton('Close Month')
  await view.getByRole('button', { name: 'Dialog confirm:Close Month' }).click()
  await flushUi()

  await expect.element(view.getByText('Close February 2026 first. That is the next valid month in the sequence.')).toBeVisible()
})

test('maps month close edge-of-chain failures into friendly validation text', async () => {
  await page.viewport(1280, 900)

  periodMocks.closeMonth.mockRejectedValueOnce(makeApiError({
    errorCode: 'period.month.later_closed_exists',
    context: {
      latestClosedPeriod: '2026-05-01',
    },
  }))

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

  clickActionButton('Close Month')
  await view.getByRole('button', { name: 'Dialog confirm:Close Month' }).click()
  await flushUi()

  await expect.element(view.getByText('A later closed month already exists at May 2026. Reopen from the edge of the chain first.')).toBeVisible()
})

for (const scenario of [
  {
    name: 'already closed months',
    error: makeApiError({ errorCode: 'period.already_closed' }),
    expected: 'The selected period is already closed.',
  },
  {
    name: 'missing month prerequisites without period context',
    error: makeApiError({ errorCode: 'period.month.prerequisite_not_met' }),
    expected: 'Close the earlier open months first.',
  },
  {
    name: 'later closed months without period context',
    error: makeApiError({ errorCode: 'period.month.later_closed_exists' }),
    expected: 'A later month is already closed. Reopen from the edge of the chain first.',
  },
  {
    name: 'forbidden negative balances',
    error: makeApiError({ errorCode: 'accounting.negative_balance.forbidden' }),
    expected: 'Month close was blocked because it would produce a forbidden negative balance.',
  },
  {
    name: 'validation issues from unknown domain errors',
    error: makeApiError({ issues: [{ message: 'A domain validation issue occurred.' }] }),
    expected: 'A domain validation issue occurred.',
  },
  {
    name: 'unmapped API failures',
    error: makeApiError({ message: 'Unmapped API failure.' }),
    expected: 'Unmapped API failure.',
  },
  {
    name: 'ordinary errors',
    error: new Error('Network unavailable.'),
    expected: 'Network unavailable.',
  },
  {
    name: 'non-error failures',
    error: 'Unexpected failure value.',
    expected: 'Unexpected failure value.',
  },
]) {
  test(`maps ${scenario.name} into month close validation text`, async () => {
    periodMocks.closeMonth.mockRejectedValueOnce(scenario.error)
    const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

    clickActionButton('Close Month')
    await view.getByRole('button', { name: 'Dialog confirm:Close Month' }).click()
    await flushUi()

    await expect.element(view.getByText(scenario.expected)).toBeVisible()
  })
}

for (const scenario of [
  {
    name: 'months that are not closed',
    error: makeApiError({ errorCode: 'period.not_closed' }),
    expected: 'The selected period is not closed, so it cannot be reopened.',
  },
  {
    name: 'latest closed month requirements without period context',
    error: makeApiError({ errorCode: 'period.month.reopen.latest_closed_required' }),
    expected: 'Only the latest closed month can be reopened.',
  },
  {
    name: 'months protected by fiscal year close',
    error: makeApiError({ errorCode: 'period.month.reopen.fiscal_year_closed' }),
    expected: 'This month cannot be reopened because fiscal year closing already exists for it.',
  },
]) {
  test(`maps ${scenario.name} into month reopen validation text`, async () => {
    periodMocks.state.closedMonths = [1, 2, 3]
    periodMocks.reopenMonth.mockRejectedValueOnce(scenario.error)
    const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

    await view.getByPlaceholder('Explain why the chain needs to be reopened').fill('Required reason')
    clickActionButton('Reopen Month')
    await view.getByRole('button', { name: 'Dialog confirm:Reopen Month' }).click()
    await flushUi()

    await expect.element(view.getByText(scenario.expected)).toBeVisible()
  })
}

for (const scenario of [
  {
    name: 'already-closed fiscal years',
    error: makeApiError({ errorCode: 'period.fiscal_year.already_closed' }),
    expected: 'The selected fiscal year has already been closed.',
  },
  {
    name: 'retained earnings mismatch',
    error: makeApiError({
      errorCode: 'period.fiscal_year.retained_earnings_mismatch',
      context: {
        actualRetainedEarningsAccountDisplay: '3300 · Owner Equity',
      },
    }),
    expected: 'This fiscal year was already closed using 3300 · Owner Equity. Reuse that same account for retries.',
  },
  {
    name: 'retained earnings mismatch without account context',
    error: makeApiError({ errorCode: 'period.fiscal_year.retained_earnings_mismatch' }),
    expected: 'This fiscal year was already closed using a different retained earnings account.',
  },
  {
    name: 'in-progress fiscal closes',
    error: makeApiError({
      errorCode: 'period.fiscal_year.in_progress',
    }),
    expected: 'Fiscal year close is already running for the selected end period.',
  },
  {
    name: 'retained earnings accounts with required dimensions',
    error: makeApiError({ errorCode: 'period.fiscal_year.retained_earnings_dimensions_not_allowed' }),
    expected: 'Retained earnings account must not require dimensions.',
  },
  {
    name: 'missing fiscal prerequisites',
    error: makeApiError({
      errorCode: 'period.fiscal_year.prerequisite_not_met',
      context: {
        notClosedPeriod: '2026-02-01',
      },
    }),
    expected: 'Close all prior months first. The first open prerequisite month is February 2026.',
  },
  {
    name: 'missing fiscal prerequisites without period context',
    error: makeApiError({ errorCode: 'period.fiscal_year.prerequisite_not_met' }),
    expected: 'Close all prior months first before running fiscal year close.',
  },
  {
    name: 'later closed months before fiscal close',
    error: makeApiError({
      errorCode: 'period.fiscal_year.later_closed_exists',
      context: {
        latestClosedPeriod: '2026-06-01',
      },
    }),
    expected: 'A later closed month already exists at June 2026. Reopen it before running fiscal year close.',
  },
  {
    name: 'later closed months without period context before fiscal close',
    error: makeApiError({ errorCode: 'period.fiscal_year.later_closed_exists' }),
    expected: 'A later closed month already exists. Reopen it before running fiscal year close.',
  },
]) {
  test(`maps ${scenario.name} into fiscal close validation text`, async () => {
    await page.viewport(1280, 900)

    periodMocks.closeFiscalYear.mockRejectedValueOnce(scenario.error)

    const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

    clickActionButton('Close Fiscal Year')
    await view.getByRole('button', { name: 'Dialog confirm:Close Fiscal Year' }).click()
    await flushUi()

    await expect.element(view.getByText(scenario.expected)).toBeVisible()
  })
}

for (const scenario of [
  {
    name: 'fiscal years that are not closed',
    error: makeApiError({ errorCode: 'period.fiscal_year.not_closed' }),
    expected: 'The selected fiscal year is not currently closed.',
  },
  {
    name: 'reopen runs that are still in progress',
    error: makeApiError({
      errorCode: 'period.fiscal_year.reopen.in_progress',
    }),
    expected: 'Fiscal year reopen is blocked because the close run is still in progress.',
  },
  {
    name: 'later closed months without period context',
    error: makeApiError({ errorCode: 'period.fiscal_year.reopen.later_closed_exists' }),
    expected: 'Later closed months depend on this fiscal year close and must be reopened first.',
  },
  {
    name: 'later closed months that depend on the fiscal close',
    error: makeApiError({
      errorCode: 'period.fiscal_year.reopen.later_closed_exists',
      context: {
        latestClosedPeriod: '2026-04-01',
      },
    }),
    expected: 'Reopen April 2026 first. Later closed months depend on this fiscal year close.',
  },
]) {
  test(`maps ${scenario.name} into fiscal reopen validation text`, async () => {
    await page.viewport(1280, 900)

    periodMocks.state.closedMonths = [1, 2]
    periodMocks.state.fiscalClosed = true
    periodMocks.state.fiscalRetainedEarningsAccountId = 'retained-earnings'
    periodMocks.reopenFiscalYear.mockRejectedValueOnce(scenario.error)

    const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

    await view.getByPlaceholder('Explain why this fiscal year close must be reopened').fill('Need to rerun close')
    clickActionButton('Reopen Fiscal Year')
    await view.getByRole('button', { name: 'Dialog confirm:Reopen Fiscal Year' }).click()
    await flushUi()

    await expect.element(view.getByText(scenario.expected)).toBeVisible()
  })
}

test('shows retained earnings lookup failures in the fiscal validation summary', async () => {
  await page.viewport(1280, 900)

  periodMocks.searchRetainedEarningsAccounts
    .mockImplementationOnce(async ({ query }: { query?: string }) => {
      const normalized = String(query ?? '').trim().toLowerCase()
      return clone(
        periodMocks.state.retainedOptions.filter((item) =>
          normalized.length === 0
          || item.display.toLowerCase().includes(normalized)
          || item.code.toLowerCase().includes(normalized),
        ),
      )
    })
    .mockRejectedValueOnce(makeApiError({
      issues: [{ message: 'Retained earnings lookup is unavailable right now.' }],
    }))

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

  await view.getByPlaceholder('Type account code or name…').fill('owner')
  await flushUi()

  await expect.element(view.getByText('Retained earnings lookup is unavailable right now.')).toBeVisible()
  await expect.element(view.getByText('lookup-items:none')).toBeVisible()
})

test('renders broken month chains, every month state, and selected-month boundary summaries', async () => {
  const calendar = buildCalendar({ year: 2026, closedMonths: [1, 3] })
  Object.assign(calendar.months[0]!, {
    closedBy: '',
    closedAtUtc: 'not-a-date',
  })
  Object.assign(calendar.months[1]!, {
    hasActivity: false,
  })
  Object.assign(calendar.months[3]!, {
    blockingPeriod: null,
  })
  Object.assign(calendar.months[4]!, {
    state: 'BlockedByLaterClosedMonths',
    canClose: false,
    canReopen: false,
    blockingReason: 'LaterClosedMonths',
    blockingPeriod: '2026-03-01',
  })
  Object.assign(calendar.months[6]!, {
    state: 'BlockedByLaterClosedMonths',
    canClose: false,
    canReopen: false,
    blockingReason: 'LaterClosedMonths',
    blockingPeriod: null,
  })
  Object.assign(calendar.months[7]!, {
    state: 'Open',
    canClose: false,
    canReopen: false,
    blockingReason: 'UnknownReason',
    blockingPeriod: null,
  })
  Object.assign(calendar.months[5]!, {
    state: 'Open',
    hasActivity: false,
    canClose: false,
    canReopen: false,
    blockingReason: null,
    blockingPeriod: null,
  })
  periodMocks.getPeriodClosingCalendar.mockResolvedValue(clone(calendar))

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-01&fy=2026-03')

  await expect.element(view.getByText('Legacy out-of-sequence close detected')).toBeVisible()
  await expect.element(view.getByText('Closed by unknown on not-a-date.')).toBeVisible()
  expect(document.body.textContent).toContain('Legacy')
  expect(document.body.textContent).toContain('Ready')
  expect(document.body.textContent).toContain('Blocked')
  expect(document.body.textContent).toContain('Open')
  expect(document.body.textContent).toContain('A later closed month already exists at March 2026. Reopen from the edge of the chain first.')

  await view.getByRole('button', { name: 'March 2026' }).click()
  await flushUi()
  await view.getByTestId('input-null-Explain why the chain needs to be reopened').click()
  await view.getByPlaceholder('Explain why the chain needs to be reopened').fill('Keep this reason while selecting another month')

  await view.getByRole('button', { name: 'February 2026' }).click()
  await flushUi()
  await expect.element(view.getByText('This month is open and can be closed now. No accounting activity was detected for it.')).toBeVisible()

  await view.getByRole('button', { name: 'April 2026' }).click()
  await flushUi()
  await expect.element(view.getByText('This month has activity, but the close sequence must be resolved first.')).toBeVisible()

  await view.getByRole('button', { name: 'June 2026' }).click()
  await flushUi()
  await expect.element(view.getByText('This month is open but not currently eligible for closing.')).toBeVisible()

  clickButtonByTitle('Inspect month')
  await flushUi()
  clickButtonByTitle('Selected month')

  clickButtonByTitle('Close month')
  await expect.element(view.getByText('Close month?')).toBeVisible()
  await view.getByRole('button', { name: 'Dialog cancel' }).click()
})

test('renders unanchored calendars with one activity month and calendars without activity', async () => {
  const calendar = buildCalendar({ year: 2026, closedMonths: [] })
  calendar.months.forEach((month, index) => { month.hasActivity = index === 0 })
  periodMocks.getPeriodClosingCalendar.mockResolvedValueOnce(clone(calendar))

  const first = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-01&fy=2026-01')
  await expect.element(first.view.getByText('Activity detected in 1 month of 2026.')).toBeVisible()
  await expect.element(first.view.getByText('Unanchored')).toBeVisible()

  const noActivityCalendar = clone(calendar)
  noActivityCalendar.earliestActivityPeriod = null
  noActivityCalendar.months.forEach(month => { month.hasActivity = false })
  periodMocks.getPeriodClosingCalendar.mockResolvedValue(clone(noActivityCalendar))
  clickButtonByTitle('Next year')
  await flushUi()

  await expect.element(first.view.getByText('No accounting activity has been posted yet.')).toBeVisible()
})

test('renders every fiscal execution state and its blocking guidance while fiscal end changes', async () => {
  periodMocks.state.closedMonths = Array.from({ length: 12 }, (_, index) => index + 1)
  periodMocks.getFiscalYearCloseStatus.mockImplementation(async (period: string) => {
    const month = parseMonthNumber(period)
    const base = clone(buildFiscalStatus(period))
    const common = {
      ...base,
      canClose: false,
      canReopen: false,
      closedRetainedEarningsAccount: null,
      blockingPeriod: null,
      blockingReason: null,
      reopenBlockingPeriod: null,
      reopenBlockingReason: null,
    }

    switch (month) {
      case 3:
        return { ...common, state: 'Completed', completedAtUtc: null }
      case 4:
        return {
          ...common,
          state: 'Completed',
          canReopen: true,
          reopenWillOpenEndPeriod: false,
          reopenBlockingReason: 'LaterClosedMonths',
          reopenBlockingPeriod: null,
        }
      case 5:
        return { ...common, state: 'InProgress', startedAtUtc: 'invalid-start-time' }
      case 6:
        return { ...common, state: 'StaleInProgress', startedAtUtc: null }
      case 7:
        return { ...common, state: 'Ready', canClose: true }
      case 8:
        return {
          ...common,
          state: 'BlockedByLaterClosedMonths',
          blockingReason: 'LaterClosedMonths',
          blockingPeriod: '2026-09-01',
        }
      case 9:
        return { ...common, state: 'BlockedByClosedEndPeriod', blockingReason: 'ClosedEndPeriod' }
      case 10:
        return { ...common, state: 'UnknownState', blockingReason: 'FiscalYearClose' }
      case 11:
        return {
          ...common,
          state: 'Completed',
          completedAtUtc: '2026-11-30T12:00:00Z',
          endPeriodClosed: true,
          endPeriodClosedBy: '',
          endPeriodClosedAtUtc: null,
          closedRetainedEarningsAccount: periodMocks.state.retainedOptions[0],
          reopenBlockingReason: 'LaterClosedMonths',
          reopenBlockingPeriod: '2026-12-01',
        }
      case 12:
        return { ...common, state: 'UnknownState', reopenBlockingReason: 'UnknownReason' }
      default:
        return common
    }
  })

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-01&fy=2026-03')
  await expect.element(view.getByText('Completed at —.')).toBeVisible()

  const advanceFiscalEnd = async (from: string) => {
    await view.getByText(`month-picker:${from}`).click()
    await flushUi()
  }

  await advanceFiscalEnd('2026-03')
  await expect.element(view.getByText('Later closed months must be reopened first.')).toBeVisible()
  expect(document.body.textContent).toContain('keeps the end month available for another close run.')
  await view.getByTestId('input-null-Explain why this fiscal year close must be reopened').click()
  await view.getByPlaceholder('Explain why this fiscal year close must be reopened').fill('Redo without opening month')
  clickActionButton('Reopen Fiscal Year')
  await view.getByRole('button', { name: 'Dialog confirm:Reopen Fiscal Year' }).click()
  await flushUi()
  expect(periodMocks.toasts.push).toHaveBeenCalledWith(expect.objectContaining({
    message: 'Fiscal year close for April 2026 is ready to run again.',
  }))

  await advanceFiscalEnd('2026-04')
  await expect.element(view.getByText('Started at invalid-start-time.')).toBeVisible()

  await advanceFiscalEnd('2026-05')
  await expect.element(view.getByText('Started at —.')).toBeVisible()

  await advanceFiscalEnd('2026-06')
  await expect.element(view.getByText('All prerequisites are satisfied and the close can be executed.')).toBeVisible()

  await advanceFiscalEnd('2026-07')
  await expect.element(view.getByText('A later closed month already exists at September 2026. Reopen from the edge of the chain first.')).toBeVisible()

  await advanceFiscalEnd('2026-08')
  await expect.element(view.getByText('The selected fiscal year end period is already closed.')).toBeVisible()

  await advanceFiscalEnd('2026-09')
  await expect.element(view.getByText('Fiscal year closing already exists for this month, so reopen is intentionally blocked.')).toBeVisible()

  await advanceFiscalEnd('2026-10')
  expect(document.body.textContent).toContain('using 3200 · Retained Earnings.')
  expect(document.body.textContent).toContain('End period was closed by unknown')
  await expect.element(view.getByText('Reopen December 2026 first. Later closed months depend on this fiscal year close.')).toBeVisible()

  await advanceFiscalEnd('2026-11')
  await expect.element(view.getByText('Fiscal year close is currently blocked.')).toBeVisible()
})

test('normalizes invalid controls, null child emissions, and a missing retained earnings selection', async () => {
  const { router, view } = await renderPage('/admin/accounting/period-closing?year=9999&month=9999-03&fy=9999-04', null)

  clickButtonByTitle('Next year')
  await flushUi()
  expect(router.currentRoute.value.query.year).toBe(String(new Date().getFullYear()))

  const invalidMonthButtons = [...document.querySelectorAll('button[data-testid^="month-picker-invalid-"]')]
  expect(invalidMonthButtons).toHaveLength(2)
  ;(invalidMonthButtons[0] as HTMLButtonElement).click()
  await flushUi()
  const currentMonth = monthValue(new Date().getFullYear(), new Date().getMonth() + 1)
  expect(router.currentRoute.value.query.month).toBe(currentMonth)

  const refreshedInvalidMonthButtons = [...document.querySelectorAll('button[data-testid^="month-picker-invalid-"]')]
  ;(refreshedInvalidMonthButtons[1] as HTMLButtonElement).click()
  await flushUi()
  expect(router.currentRoute.value.query.fy).toBe(currentMonth)

  await view.getByRole('button', { name: 'Lookup select first' }).click()
})

test('ignores fiscal close confirmation if the retained earnings selection is cleared while the dialog is open', async () => {
  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

  clickActionButton('Close Fiscal Year')
  await expect.element(view.getByText('Close fiscal year?')).toBeVisible()
  await view.getByRole('button', { name: 'Lookup emit null' }).click()
  await view.getByRole('button', { name: 'Dialog confirm:Close Fiscal Year' }).click()
  expect(periodMocks.closeFiscalYear).not.toHaveBeenCalled()
})

test('ignores an invalid period supplied by the calendar and keeps the selected month for its close dialog', async () => {
  const calendar = buildCalendar({ year: 2026, closedMonths: [] })
  calendar.months = [{
    period: 'invalid-period',
    state: 'ReadyToClose',
    isClosed: false,
    hasActivity: false,
    closedBy: null,
    closedAtUtc: null,
    canClose: true,
    canReopen: false,
    blockingPeriod: null,
    blockingReason: null,
  }]
  periodMocks.getPeriodClosingCalendar.mockResolvedValue(clone(calendar))

  const { router, view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')
  ;(document.querySelector('tbody button') as HTMLButtonElement).click()
  await flushUi()
  expect(router.currentRoute.value.query.month).toBe('2026-03')

  clickButtonByTitle('Close month')
  await expect.element(view.getByText('This will close March 2026 and block future posting into it.')).toBeVisible()
  await view.getByRole('button', { name: 'Dialog cancel' }).click()
})

test('resynchronizes a completed fiscal close selection after delayed lookup options arrive', async () => {
  periodMocks.state.closedMonths = [1, 2]
  periodMocks.state.fiscalClosed = true
  periodMocks.state.fiscalRetainedEarningsAccountId = 'owner-equity'

  let resolveOptions!: (value: typeof periodMocks.state.retainedOptions) => void
  periodMocks.searchRetainedEarningsAccounts.mockImplementationOnce(() =>
    new Promise<typeof periodMocks.state.retainedOptions>((resolve) => { resolveOptions = resolve }))

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')
  resolveOptions(clone(periodMocks.state.retainedOptions))
  await flushUi()

  await expect.element(view.getByText('lookup-value:3300 · Owner Equity')).toBeVisible()
})

test('uses the first eligible account when no retained-earnings-labelled option exists', async () => {
  periodMocks.searchRetainedEarningsAccounts.mockResolvedValueOnce([
    periodMocks.state.retainedOptions[1]!,
  ])

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')
  await expect.element(view.getByText('lookup-value:3300 · Owner Equity')).toBeVisible()
})

test('shows initial calendar and fiscal status failures without leaving stale state behind', async () => {
  periodMocks.getPeriodClosingCalendar.mockRejectedValueOnce(new Error('Calendar service unavailable.'))
  periodMocks.getFiscalYearCloseStatus.mockRejectedValueOnce('Fiscal status unavailable.')
  periodMocks.searchRetainedEarningsAccounts.mockResolvedValueOnce([])

  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')

  await expect.element(view.getByText('Calendar service unavailable.')).toBeVisible()
  await expect.element(view.getByText('Fiscal status unavailable.')).toBeVisible()
  await expect.element(view.getByText('Status is unavailable for the selected month.')).toBeVisible()
  await expect.element(view.getByText('Fiscal year close status is unavailable.')).toBeVisible()
  await expect.element(view.getByText('lookup-items:none')).toBeVisible()
})

test('ignores stale retained earnings lookup successes and failures', async () => {
  const { view } = await renderPage('/admin/accounting/period-closing?year=2026&month=2026-03&fy=2026-03')
  const input = view.getByPlaceholder('Type account code or name…')

  let resolveSlowSuccess!: (value: typeof periodMocks.state.retainedOptions) => void
  const slowSuccess = new Promise<typeof periodMocks.state.retainedOptions>((resolve) => {
    resolveSlowSuccess = resolve
  })
  periodMocks.searchRetainedEarningsAccounts
    .mockImplementationOnce(() => slowSuccess)
    .mockResolvedValueOnce([periodMocks.state.retainedOptions[1]!])

  await input.fill('slow')
  await input.fill('owner')
  await flushUi()
  resolveSlowSuccess([periodMocks.state.retainedOptions[0]!])
  await flushUi()
  await expect.element(view.getByText('lookup-items:3300 · Owner Equity')).toBeVisible()

  let rejectSlow!: (reason: unknown) => void
  const slowFailure = new Promise<never>((_resolve, reject) => { rejectSlow = reject })
  periodMocks.searchRetainedEarningsAccounts
    .mockImplementationOnce(() => slowFailure)
    .mockResolvedValueOnce([periodMocks.state.retainedOptions[0]!])

  await input.fill('failure')
  await input.fill('retained')
  await flushUi()
  rejectSlow(new Error('Stale lookup failure.'))
  await flushUi()
  await expect.element(view.getByText('lookup-items:3200 · Retained Earnings')).toBeVisible()
  expect(document.body.textContent).not.toContain('Stale lookup failure.')
})
