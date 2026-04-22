<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { ApiError } from '../api/http'
import NgbConfirmDialog from '../components/NgbConfirmDialog.vue'
import type { LookupItem } from '../metadata/types'
import NgbBadge from '../primitives/NgbBadge.vue'
import NgbButton from '../primitives/NgbButton.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbLookup from '../primitives/NgbLookup.vue'
import NgbMonthPicker from '../primitives/NgbMonthPicker.vue'
import { useToasts } from '../primitives/toast'
import { navigateBack } from '../router/backNavigation'
import {
  normalizeMonthQueryValue,
  normalizeYearQueryValue,
  replaceCleanRouteQuery,
} from '../router/queryParams'
import { copyAppLink } from '../router/shareLink'
import NgbPageHeader from '../site/NgbPageHeader.vue'
import NgbValidationSummary from '../components/forms/NgbValidationSummary.vue'
import {
  closeFiscalYear,
  closeMonth,
  getFiscalYearCloseStatus,
  getPeriodClosingCalendar,
  reopenFiscalYear,
  reopenMonth,
  searchRetainedEarningsAccounts,
} from './periodClosingApi'
import {
  alignMonthValueToYear,
  currentCalendarYear,
  defaultMonthValueForYear,
  formatPeriodDateOnly as formatDateOnly,
  formatPeriodMonthValue as formatMonthValue,
  resolveSelectedMonthValue,
  resolveSelectedYear,
  selectMonthValue,
  toPeriodDateOnly,
} from './periodClosing'
import { buildAccountingPeriodClosingPath } from './navigation'
import type {
  FiscalYearCloseStatusDto,
  PeriodClosingCalendarDto,
  PeriodCloseStatusDto,
  RetainedEarningsAccountOptionDto,
} from './periodClosingTypes'

type BadgeTone = 'neutral' | 'success' | 'warn' | 'danger'

const props = withDefaults(defineProps<{
  title?: string
  backTarget?: string | null
}>(), {
  title: 'Period Close',
  backTarget: '/',
})

const route = useRoute()
const router = useRouter()
const toasts = useToasts()

const resolvedBackTarget = computed(() => String(props.backTarget ?? '').trim() || '/')

function formatUtc(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString()
}

function replaceRouteQuery(patch: Record<string, unknown>) {
  return replaceCleanRouteQuery(route, router, patch)
}

const selectedYear = computed(() => {
  return resolveSelectedYear({
    year: route.query.year,
    month: route.query.month,
    fy: route.query.fy,
  })
})

const selectedMonth = computed(() => {
  return resolveSelectedMonthValue(route.query.month, selectedYear.value)
})

const selectedFiscalYearEndMonth = computed(() => {
  return resolveSelectedMonthValue(route.query.fy, selectedYear.value)
})

function setSelectedYear(year: number) {
  const normalizedYear = normalizeYearQueryValue(year) ?? currentCalendarYear()
  void replaceRouteQuery({
    year: String(normalizedYear),
    month: alignMonthValueToYear(selectedMonth.value, normalizedYear),
    fy: alignMonthValueToYear(selectedFiscalYearEndMonth.value, normalizedYear),
  })
}

function setSelectedMonth(value: string) {
  const normalized = normalizeMonthQueryValue(value) ?? defaultMonthValueForYear(selectedYear.value)
  const year = resolveSelectedYear({ month: normalized }, new Date())
  void replaceRouteQuery({
    year: String(year),
    month: normalized,
    fy: alignMonthValueToYear(selectedFiscalYearEndMonth.value, year),
  })
}

function setSelectedFiscalYearEndMonth(value: string) {
  const normalized = normalizeMonthQueryValue(value) ?? defaultMonthValueForYear(selectedYear.value)
  const year = resolveSelectedYear({ month: normalized }, new Date())
  void replaceRouteQuery({
    year: String(year),
    month: alignMonthValueToYear(selectedMonth.value, year),
    fy: normalized,
  })
}

function shiftYear(delta: number) {
  setSelectedYear(selectedYear.value + delta)
}

function selectMonth(periodOrMonthValue: string) {
  const normalized = selectMonthValue(periodOrMonthValue)
  if (normalized) setSelectedMonth(normalized)
}

const reopenReason = ref('')
const fiscalReopenReason = ref('')

const calendar = ref<PeriodClosingCalendarDto | null>(null)
const calendarLoading = ref(false)
const calendarErrorMessages = ref<string[]>([])

const monthActionLoading = ref(false)
const monthReopenLoading = ref(false)
const monthErrorMessages = ref<string[]>([])
const monthConfirmOpen = ref(false)
const reopenConfirmOpen = ref(false)
const pendingCloseMonthValue = ref<string | null>(null)
const pendingReopenMonthValue = ref<string | null>(null)

const fiscalStatus = ref<FiscalYearCloseStatusDto | null>(null)
const fiscalLoading = ref(false)
const fiscalExecuting = ref(false)
const fiscalErrorMessages = ref<string[]>([])
const fiscalConfirmOpen = ref(false)
const fiscalReopenConfirmOpen = ref(false)

const retainedEarnings = ref<LookupItem | null>(null)
const retainedEarningsItems = ref<LookupItem[]>([])
const retainedEarningsLoading = ref(false)
const retainedEarningsSearchSeq = ref(0)

const reopenReasonTrimmed = computed(() => reopenReason.value.trim())
const fiscalReopenReasonTrimmed = computed(() => fiscalReopenReason.value.trim())
const canBack = computed(() => route.path !== resolvedBackTarget.value)
const selectedMonthPeriod = computed(() => toPeriodDateOnly(selectedMonth.value))
const selectedFiscalYearEndPeriod = computed(() => toPeriodDateOnly(selectedFiscalYearEndMonth.value))
const yearMonths = computed(() => calendar.value?.months ?? [])
const selectedMonthStatus = computed(() =>
  yearMonths.value.find((item) => item.period === selectedMonthPeriod.value) ?? null)
const openPriorMonths = computed(() => (fiscalStatus.value?.priorMonths ?? []).filter((item) => !item.isClosed))
const closedMonthsCount = computed(() => yearMonths.value.filter((item) => item.isClosed).length)
const activeMonthsCount = computed(() => yearMonths.value.filter((item) => item.hasActivity).length)
const monthBusy = computed(() => monthActionLoading.value || monthReopenLoading.value)

function chainHealthTone(): BadgeTone {
  if (calendar.value?.hasBrokenChain) return 'danger'
  if (calendar.value?.canCloseAnyMonth) return 'warn'
  return 'success'
}

function monthStateTone(status: PeriodCloseStatusDto | null | undefined): BadgeTone {
  switch (status?.state) {
    case 'Closed':
      return 'success'
    case 'ClosedOutOfSequence':
      return 'warn'
    case 'BlockedByEarlierOpenMonth':
    case 'BlockedByLaterClosedMonths':
      return 'danger'
    default:
      return 'neutral'
  }
}

function monthStateLabel(status: PeriodCloseStatusDto | null | undefined): string {
  switch (status?.state) {
    case 'Closed':
      return 'Closed'
    case 'ClosedOutOfSequence':
      return 'Legacy'
    case 'ReadyToClose':
      return 'Ready'
    case 'BlockedByEarlierOpenMonth':
    case 'BlockedByLaterClosedMonths':
      return 'Blocked'
    case 'Open':
      return 'Open'
    default:
      return 'Unknown'
  }
}

function fiscalBadgeTone(state: string | null | undefined): BadgeTone {
  switch (state) {
    case 'Completed':
      return 'success'
    case 'InProgress':
    case 'StaleInProgress':
      return 'warn'
    case 'BlockedByEarlierOpenMonth':
    case 'BlockedByLaterClosedMonths':
    case 'BlockedByClosedEndPeriod':
      return 'danger'
    default:
      return 'neutral'
  }
}

function fiscalStateLabel(state: string | null | undefined): string {
  switch (state) {
    case 'Completed':
      return 'Completed'
    case 'Ready':
      return 'Ready'
    case 'InProgress':
      return 'Running'
    case 'StaleInProgress':
      return 'Stale'
    case 'BlockedByEarlierOpenMonth':
    case 'BlockedByLaterClosedMonths':
    case 'BlockedByClosedEndPeriod':
      return 'Blocked'
    default:
      return 'Unknown'
  }
}

function formatBlockingHint(status: PeriodCloseStatusDto | FiscalYearCloseStatusDto | null | undefined): string {
  switch (status?.blockingReason) {
    case 'EarlierOpenMonth':
      return status.blockingPeriod
        ? `Close ${formatDateOnly(status.blockingPeriod)} first.`
        : 'Close the earlier open month first.'
    case 'LaterClosedMonths':
      return status.blockingPeriod
        ? `A later closed month already exists at ${formatDateOnly(status.blockingPeriod)}. Reopen from the edge of the chain first.`
        : 'Later closed months must be reopened first.'
    case 'FiscalYearClose':
      return 'Fiscal year closing already exists for this month, so reopen is intentionally blocked.'
    case 'ClosedEndPeriod':
      return 'The selected fiscal year end period is already closed.'
    default:
      return ''
  }
}

function formatFiscalReopenBlockingHint(status: FiscalYearCloseStatusDto | null | undefined): string {
  switch (status?.reopenBlockingReason) {
    case 'LaterClosedMonths':
      return status.reopenBlockingPeriod
        ? `Reopen ${formatDateOnly(status.reopenBlockingPeriod)} first. Later closed months depend on this fiscal year close.`
        : 'Later closed months must be reopened first.'
    default:
      return ''
  }
}

const selectedMonthSummary = computed(() => {
  const status = selectedMonthStatus.value
  if (!status) return 'Status is unavailable for the selected month.'

  if (status.isClosed) {
    return `Closed by ${status.closedBy || 'unknown'} on ${formatUtc(status.closedAtUtc)}.`
  }

  if (status.canClose) {
    return status.hasActivity
      ? 'This is a valid next step in the close sequence and it contains accounting activity.'
      : 'This month is open and can be closed now. No accounting activity was detected for it.'
  }

  return status.hasActivity
    ? 'This month has activity, but the close sequence must be resolved first.'
    : 'This month is open but not currently eligible for closing.'
})

const fiscalSummary = computed(() => {
  const status = fiscalStatus.value
  if (!status) return 'Fiscal year close status is unavailable.'

  switch (status.state) {
    case 'Completed':
      if (status.closedRetainedEarningsAccount?.display) {
        return status.canReopen
          ? `Completed at ${formatUtc(status.completedAtUtc)} using ${status.closedRetainedEarningsAccount.display}. You can reopen it for redo if the close needs to change.`
          : `Completed at ${formatUtc(status.completedAtUtc)} using ${status.closedRetainedEarningsAccount.display}.`
      }
      return status.canReopen
        ? `Completed at ${formatUtc(status.completedAtUtc)}. You can reopen it for redo if the close needs to change.`
        : `Completed at ${formatUtc(status.completedAtUtc)}.`
    case 'InProgress':
    case 'StaleInProgress':
      return `Started at ${formatUtc(status.startedAtUtc)}.`
    case 'Ready':
      return 'All prerequisites are satisfied and the close can be executed.'
    default:
      return formatBlockingHint(status) || 'Fiscal year close is currently blocked.'
  }
})

function mapRetainedEarningsItem(item: RetainedEarningsAccountOptionDto): LookupItem {
  return {
    id: item.accountId,
    label: item.display,
    meta: 'Eligible retained earnings account',
  }
}

function syncRetainedEarningsSelectionWithStatus() {
  const closed = fiscalStatus.value?.closedRetainedEarningsAccount
  if (!closed) return

  const existing = retainedEarningsItems.value.find((item) => item.id === closed.accountId)
  retainedEarnings.value = existing ?? {
    id: closed.accountId,
    label: closed.display,
    meta: 'Used by completed fiscal year close',
  }
}

function maybePickDefaultRetainedEarnings(items: LookupItem[]) {
  if (items.length === 0) return
  if (fiscalStatus.value?.closedRetainedEarningsAccount) {
    syncRetainedEarningsSelectionWithStatus()
    return
  }
  if (retainedEarnings.value && items.some((item) => item.id === retainedEarnings.value?.id)) return

  const preferred = items.find((item) => item.label.toLowerCase().includes('retained'))
  retainedEarnings.value = preferred ?? items[0] ?? null
}

async function loadRetainedEarningsOptions(query = ''): Promise<void> {
  const seq = retainedEarningsSearchSeq.value + 1
  retainedEarningsSearchSeq.value = seq
  retainedEarningsLoading.value = true

  try {
    const items = (await searchRetainedEarningsAccounts({ query, limit: 20 })).map(mapRetainedEarningsItem)
    if (retainedEarningsSearchSeq.value !== seq) return
    retainedEarningsItems.value = items
    if (fiscalStatus.value?.closedRetainedEarningsAccount) {
      syncRetainedEarningsSelectionWithStatus()
    } else if (!query.trim()) {
      maybePickDefaultRetainedEarnings(items)
    }
  } catch (error) {
    if (retainedEarningsSearchSeq.value !== seq) return
    retainedEarningsItems.value = []
    fiscalErrorMessages.value = extractErrorMessages(error)
  } finally {
    if (retainedEarningsSearchSeq.value === seq) retainedEarningsLoading.value = false
  }
}

async function loadCalendar(): Promise<void> {
  calendarLoading.value = true
  calendarErrorMessages.value = []

  try {
    calendar.value = await getPeriodClosingCalendar(selectedYear.value)
  } catch (error) {
    calendar.value = null
    calendarErrorMessages.value = extractErrorMessages(error)
  } finally {
    calendarLoading.value = false
  }
}

async function loadFiscalStatus(): Promise<void> {
  fiscalLoading.value = true
  fiscalErrorMessages.value = []

  try {
    fiscalStatus.value = await getFiscalYearCloseStatus(selectedFiscalYearEndPeriod.value)
    syncRetainedEarningsSelectionWithStatus()
  } catch (error) {
    fiscalStatus.value = null
    fiscalErrorMessages.value = extractErrorMessages(error)
  } finally {
    fiscalLoading.value = false
  }
}

async function refreshSurface(): Promise<void> {
  await Promise.all([loadCalendar(), loadFiscalStatus()])
}

watch(
  () => selectedYear.value,
  () => { void loadCalendar() },
  { immediate: true },
)

watch(
  () => selectedFiscalYearEndMonth.value,
  () => {
    void loadFiscalStatus()
    void loadRetainedEarningsOptions('')
  },
  { immediate: true },
)

const monthCloseDisabled = computed(() =>
  calendarLoading.value
  || monthBusy.value
  || !(selectedMonthStatus.value?.canClose ?? false))

const monthReopenDisabled = computed(() =>
  calendarLoading.value
  || monthBusy.value
  || !reopenReasonTrimmed.value
  || !(selectedMonthStatus.value?.canReopen ?? false))

const fiscalCloseDisabled = computed(() =>
  fiscalLoading.value
  || fiscalExecuting.value
  || retainedEarningsLoading.value
  || !retainedEarnings.value
  || !(fiscalStatus.value?.canClose ?? false))

const fiscalReopenDisabled = computed(() =>
  fiscalLoading.value
  || fiscalExecuting.value
  || !fiscalReopenReasonTrimmed.value
  || !(fiscalStatus.value?.canReopen ?? false))

function openCloseMonthConfirm(period?: string) {
  const monthValue = period ? (selectMonthValue(period) ?? selectedMonth.value) : selectedMonth.value
  pendingCloseMonthValue.value = monthValue
  if (period) selectMonth(period)
  monthErrorMessages.value = []
  monthConfirmOpen.value = true
}

function openReopenMonthConfirm(period?: string) {
  const monthValue = period ? (selectMonthValue(period) ?? selectedMonth.value) : selectedMonth.value
  pendingReopenMonthValue.value = monthValue
  if (period) selectMonth(period)
  monthErrorMessages.value = []
  reopenConfirmOpen.value = true
}

async function executeMonthClose(): Promise<void> {
  const targetMonthValue = pendingCloseMonthValue.value ?? selectedMonth.value

  monthConfirmOpen.value = false
  monthActionLoading.value = true
  monthErrorMessages.value = []

  try {
    await closeMonth({
      period: toPeriodDateOnly(targetMonthValue),
    })

    toasts.push({
      title: 'Month closed',
      message: `${formatMonthValue(targetMonthValue)} is now closed.`,
      tone: 'success',
    })

    await refreshSurface()
  } catch (error) {
    monthErrorMessages.value = extractErrorMessages(error)
  } finally {
    monthActionLoading.value = false
    pendingCloseMonthValue.value = null
  }
}

async function executeMonthReopen(): Promise<void> {
  const targetMonthValue = pendingReopenMonthValue.value ?? selectedMonth.value

  reopenConfirmOpen.value = false
  monthReopenLoading.value = true
  monthErrorMessages.value = []

  try {
    await reopenMonth({
      period: toPeriodDateOnly(targetMonthValue),
      reason: reopenReasonTrimmed.value,
    })

    toasts.push({
      title: 'Month reopened',
      message: `${formatMonthValue(targetMonthValue)} is open again.`,
      tone: 'success',
    })

    reopenReason.value = ''
    await refreshSurface()
  } catch (error) {
    monthErrorMessages.value = extractErrorMessages(error)
  } finally {
    monthReopenLoading.value = false
    pendingReopenMonthValue.value = null
  }
}

async function executeFiscalClose(): Promise<void> {
  if (!retainedEarnings.value) return

  fiscalConfirmOpen.value = false
  fiscalExecuting.value = true
  fiscalErrorMessages.value = []

  try {
    await closeFiscalYear({
      fiscalYearEndPeriod: selectedFiscalYearEndPeriod.value,
      retainedEarningsAccountId: retainedEarnings.value.id,
    })

    toasts.push({
      title: 'Fiscal year closed',
      message: `Closing entries were recorded for ${formatMonthValue(selectedFiscalYearEndMonth.value)}.`,
      tone: 'success',
    })

    await refreshSurface()
  } catch (error) {
    fiscalErrorMessages.value = extractErrorMessages(error)
  } finally {
    fiscalExecuting.value = false
  }
}

async function executeFiscalReopen(): Promise<void> {
  fiscalReopenConfirmOpen.value = false
  fiscalExecuting.value = true
  fiscalErrorMessages.value = []

  const willOpenMonth = fiscalStatus.value?.reopenWillOpenEndPeriod ?? false

  try {
    await reopenFiscalYear({
      fiscalYearEndPeriod: selectedFiscalYearEndPeriod.value,
      reason: fiscalReopenReasonTrimmed.value,
    })

    toasts.push({
      title: 'Fiscal year reopened',
      message: willOpenMonth
        ? `${formatMonthValue(selectedFiscalYearEndMonth.value)} is open again and ready for redo.`
        : `Fiscal year close for ${formatMonthValue(selectedFiscalYearEndMonth.value)} is ready to run again.`,
      tone: 'success',
    })

    fiscalReopenReason.value = ''
    await refreshSurface()
  } catch (error) {
    fiscalErrorMessages.value = extractErrorMessages(error)
  } finally {
    fiscalExecuting.value = false
  }
}

async function copyShareLink(): Promise<void> {
  await copyAppLink(
    router,
    toasts,
    buildAccountingPeriodClosingPath({
      basePath: route.path,
      year: selectedYear.value,
      month: selectedMonth.value,
      fy: selectedFiscalYearEndMonth.value,
    }),
  )
}

function extractErrorMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    const messages: string[] = []

    switch (error.errorCode) {
      case 'period.already_closed':
        messages.push('The selected period is already closed.')
        break
      case 'period.not_closed':
        messages.push('The selected period is not closed, so it cannot be reopened.')
        break
      case 'period.month.prerequisite_not_met': {
        const nextClosablePeriod = String(error.context?.nextClosablePeriod ?? '').trim()
        messages.push(nextClosablePeriod
          ? `Close ${formatDateOnly(nextClosablePeriod)} first. That is the next valid month in the sequence.`
          : 'Close the earlier open months first.')
        break
      }
      case 'period.month.later_closed_exists': {
        const latestClosedPeriod = String(error.context?.latestClosedPeriod ?? '').trim()
        messages.push(latestClosedPeriod
          ? `A later closed month already exists at ${formatDateOnly(latestClosedPeriod)}. Reopen from the edge of the chain first.`
          : 'A later month is already closed. Reopen from the edge of the chain first.')
        break
      }
      case 'period.month.reopen.latest_closed_required': {
        const latestClosedPeriod = String(error.context?.latestClosedPeriod ?? '').trim()
        messages.push(latestClosedPeriod
          ? `Only the latest closed month can be reopened right now. Current edge: ${formatDateOnly(latestClosedPeriod)}.`
          : 'Only the latest closed month can be reopened.')
        break
      }
      case 'period.month.reopen.fiscal_year_closed':
        messages.push('This month cannot be reopened because fiscal year closing already exists for it.')
        break
      case 'period.fiscal_year.already_closed':
        messages.push('The selected fiscal year has already been closed.')
        break
      case 'period.fiscal_year.not_closed':
        messages.push('The selected fiscal year is not currently closed.')
        break
      case 'period.fiscal_year.retained_earnings_mismatch': {
        const actualDisplay = String(error.context?.actualRetainedEarningsAccountDisplay ?? '').trim()
        messages.push(actualDisplay
          ? `This fiscal year was already closed using ${actualDisplay}. Reuse that same account for retries.`
          : 'This fiscal year was already closed using a different retained earnings account.')
        break
      }
      case 'period.fiscal_year.in_progress':
        messages.push('Fiscal year close is already running for the selected end period.')
        break
      case 'period.fiscal_year.reopen.in_progress':
        messages.push('Fiscal year reopen is blocked because the close run is still in progress.')
        break
      case 'period.fiscal_year.retained_earnings_dimensions_not_allowed':
        messages.push('Retained earnings account must not require dimensions.')
        break
      case 'period.fiscal_year.prerequisite_not_met': {
        const missing = String(error.context?.notClosedPeriod ?? '').trim()
        messages.push(missing
          ? `Close all prior months first. The first open prerequisite month is ${formatDateOnly(missing)}.`
          : 'Close all prior months first before running fiscal year close.')
        break
      }
      case 'period.fiscal_year.later_closed_exists': {
        const latestClosedPeriod = String(error.context?.latestClosedPeriod ?? '').trim()
        messages.push(latestClosedPeriod
          ? `A later closed month already exists at ${formatDateOnly(latestClosedPeriod)}. Reopen it before running fiscal year close.`
          : 'A later closed month already exists. Reopen it before running fiscal year close.')
        break
      }
      case 'period.fiscal_year.reopen.later_closed_exists': {
        const latestClosedPeriod = String(error.context?.latestClosedPeriod ?? '').trim()
        messages.push(latestClosedPeriod
          ? `Reopen ${formatDateOnly(latestClosedPeriod)} first. Later closed months depend on this fiscal year close.`
          : 'Later closed months depend on this fiscal year close and must be reopened first.')
        break
      }
      case 'accounting.negative_balance.forbidden':
        messages.push('Month close was blocked because it would produce a forbidden negative balance.')
        break
    }

    for (const issue of error.issues ?? []) {
      const message = String(issue.message ?? '').trim()
      if (message) messages.push(message)
    }

    if (messages.length === 0) {
      const fromErrors = Object.values(error.errors ?? {})
        .flat()
        .map((value) => String(value ?? '').trim())
        .filter(Boolean)
      messages.push(...fromErrors)
    }

    if (messages.length === 0 && error.message) messages.push(error.message)
    return Array.from(new Set(messages.filter(Boolean)))
  }

  return [error instanceof Error ? error.message : String(error)]
}
</script>

<template>
  <div data-testid="period-closing-page" class="h-full min-h-0 flex flex-col bg-ngb-bg">
    <NgbPageHeader :title="title" :can-back="canBack" @back="navigateBack(router, route, resolvedBackTarget)">
      <template #secondary>
        <div class="min-w-0 flex flex-nowrap items-center gap-2 overflow-x-auto">
          <NgbBadge class="shrink-0 whitespace-nowrap" tone="neutral">Year: {{ selectedYear }}</NgbBadge>
          <NgbBadge class="shrink-0 whitespace-nowrap" tone="neutral">Month: {{ formatMonthValue(selectedMonth) }}</NgbBadge>
          <NgbBadge class="shrink-0 whitespace-nowrap" tone="neutral">Fiscal end: {{ formatMonthValue(selectedFiscalYearEndMonth) }}</NgbBadge>
          <NgbBadge class="shrink-0 whitespace-nowrap" :tone="chainHealthTone()">Sequence: {{ calendar?.hasBrokenChain ? 'Repair required' : 'Healthy' }}</NgbBadge>
        </div>
      </template>

      <template #actions>
        <button class="ngb-iconbtn" title="Share link" @click="copyShareLink">
          <NgbIcon name="share" />
        </button>
      </template>
    </NgbPageHeader>

    <div class="flex-1 overflow-auto p-6 space-y-6">
      <section class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-5 shadow-card">
        <div class="flex flex-col gap-4 xl:flex-row xl:items-end">
          <div class="min-w-0 max-w-3xl">
            <div class="text-lg font-semibold text-ngb-text">Close in sequence, not by guesswork</div>
            <div class="mt-1 text-sm text-ngb-muted">
              Once a month is closed, everyday accounting changes stay locked for that month.
              If you need to fix something, reopen the most recent closed month, make the correction, and close it again.
            </div>
          </div>

          <div class="xl:ml-auto flex flex-col items-center">
            <label class="mb-1 block text-center text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Year</label>
            <div class="flex items-center gap-2">
              <button class="ngb-iconbtn" title="Previous year" @click="shiftYear(-1)">
                <NgbIcon name="arrow-left" />
              </button>
              <div class="min-w-[92px] rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-4 py-2 text-center text-sm font-semibold text-ngb-text">
                {{ selectedYear }}
              </div>
              <button class="ngb-iconbtn" title="Next year" @click="shiftYear(1)">
                <NgbIcon name="arrow-right" />
              </button>
            </div>
          </div>
        </div>
      </section>

      <section class="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Latest Contiguous Close</div>
          <div class="mt-2 text-lg font-semibold text-ngb-text">
            {{ formatDateOnly(calendar?.latestContiguousClosedPeriod) }}
          </div>
          <div class="mt-1 text-sm text-ngb-muted">
            The stable edge of the closed chain for reporting snapshots.
          </div>
        </div>

        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Next Closable Month</div>
          <div class="mt-2 text-lg font-semibold text-ngb-text">
            {{ formatDateOnly(calendar?.nextClosablePeriod) }}
          </div>
          <div class="mt-1 text-sm text-ngb-muted">
            <template v-if="calendar?.canCloseAnyMonth">
              No activity or prior close exists yet. The first close will establish the chain.
            </template>
            <template v-else>
              This is the next month the system will allow you to close.
            </template>
          </div>
        </div>

        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Year Coverage</div>
          <div class="mt-2 text-lg font-semibold text-ngb-text">
            {{ closedMonthsCount }}/12 closed
          </div>
          <div class="mt-1 text-sm text-ngb-muted">
            Activity detected in {{ activeMonthsCount }} month{{ activeMonthsCount === 1 ? '' : 's' }} of {{ selectedYear }}.
          </div>
        </div>

        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Sequence Health</div>
          <div class="mt-2 flex items-center gap-2">
            <NgbBadge class="whitespace-nowrap" :tone="chainHealthTone()">
              {{ calendar?.hasBrokenChain ? 'Repair required' : (calendar?.canCloseAnyMonth ? 'Unanchored' : 'Healthy') }}
            </NgbBadge>
          </div>
          <div class="mt-2 text-sm text-ngb-muted">
            <template v-if="calendar?.hasBrokenChain">
              First gap: {{ formatDateOnly(calendar?.firstGapPeriod) }}. Reopen from {{ formatDateOnly(calendar?.latestClosedPeriod) }} backward until the chain is contiguous.
            </template>
            <template v-else-if="calendar?.earliestActivityPeriod">
              First detected activity: {{ formatDateOnly(calendar?.earliestActivityPeriod) }}.
            </template>
            <template v-else>
              No accounting activity has been posted yet.
            </template>
          </div>
        </div>
      </section>

      <section
        v-if="calendar?.hasBrokenChain"
        class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900 shadow-card"
      >
        <div class="font-semibold">Legacy out-of-sequence close detected</div>
        <div class="mt-1">
          Reporting is now anchored only to the contiguous chain that ends at
          <span class="font-semibold">{{ formatDateOnly(calendar?.latestContiguousClosedPeriod) }}</span>.
          Reopen from <span class="font-semibold">{{ formatDateOnly(calendar?.latestClosedPeriod) }}</span> backward until
          <span class="font-semibold">{{ formatDateOnly(calendar?.firstGapPeriod) }}</span> is no longer open.
        </div>
      </section>

      <div class="grid gap-6 xl:grid-cols-[minmax(0,1.35fr)_minmax(360px,420px)]">
        <section class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-5 shadow-card">
          <div class="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div class="text-lg font-semibold text-ngb-text">Month Calendar</div>
              <div class="mt-1 text-sm text-ngb-muted">
                Every month in {{ selectedYear }} is visible here with activity, close status, blocking reason, and the next permitted action.
              </div>
            </div>

            <div class="flex flex-wrap items-center gap-2">
              <NgbBadge class="shrink-0 whitespace-nowrap" tone="neutral">Selected: {{ formatMonthValue(selectedMonth) }}</NgbBadge>
              <span v-if="calendarLoading" class="text-sm text-ngb-muted">Refreshing…</span>
            </div>
          </div>

          <div class="mt-4">
            <NgbValidationSummary :messages="calendarErrorMessages" />
          </div>

          <div class="mt-4 overflow-x-auto">
            <table class="min-w-full text-sm">
              <thead>
                <tr class="border-b border-ngb-border text-left text-xs uppercase tracking-[0.08em] text-ngb-muted">
                  <th class="min-w-[11.5rem] px-3 py-3 font-semibold whitespace-nowrap">Month</th>
                  <th class="px-3 py-3 font-semibold">Activity</th>
                  <th class="min-w-[14rem] px-3 py-3 font-semibold">Status</th>
                  <th class="min-w-[13.5rem] px-3 py-3 font-semibold whitespace-nowrap">Closed</th>
                  <th class="min-w-[6.5rem] px-3 py-3 font-semibold text-right whitespace-nowrap">Action</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="month in yearMonths"
                  :key="month.period"
                  class="border-b border-ngb-border/70 align-top transition-colors"
                  :class="month.period === selectedMonthPeriod ? 'bg-ngb-bg/80' : 'hover:bg-ngb-bg/40'"
                >
                  <td class="min-w-[11.5rem] px-3 py-3">
                    <button
                      class="text-left font-medium text-ngb-text whitespace-nowrap hover:underline"
                      @click="selectMonth(month.period)"
                    >
                      {{ formatDateOnly(month.period) }}
                    </button>
                    <div class="mt-1 text-xs text-ngb-muted">{{ month.period }}</div>
                  </td>

                  <td class="px-3 py-3">
                    <NgbBadge class="whitespace-nowrap" :tone="month.hasActivity ? 'neutral' : 'neutral'">
                      {{ month.hasActivity ? 'Has activity' : 'No activity' }}
                    </NgbBadge>
                  </td>

                  <td class="min-w-[14rem] px-3 py-3">
                    <div class="flex flex-nowrap items-center gap-2 overflow-x-auto">
                      <NgbBadge class="shrink-0 whitespace-nowrap" :tone="monthStateTone(month)">{{ monthStateLabel(month) }}</NgbBadge>
                      <NgbBadge v-if="month.canReopen" class="shrink-0 whitespace-nowrap" tone="warn">Reopenable</NgbBadge>
                    </div>
                    <div v-if="month.blockingReason" class="mt-2 text-xs text-ngb-muted">
                      {{ formatBlockingHint(month) }}
                    </div>
                  </td>

                  <td class="min-w-[13.5rem] px-3 py-3">
                    <div class="font-medium text-ngb-text whitespace-nowrap">{{ month.closedBy || '—' }}</div>
                    <div class="mt-1 text-xs text-ngb-muted whitespace-nowrap">{{ month.closedAtUtc ? formatUtc(month.closedAtUtc) : '—' }}</div>
                  </td>

                  <td class="min-w-[6.5rem] px-3 py-3">
                    <div class="flex items-center justify-end gap-2">
                      <button
                        class="ngb-iconbtn"
                        :class="month.period === selectedMonthPeriod ? 'text-[#5c7388]' : 'text-[#6f8294]'"
                        :title="month.period === selectedMonthPeriod ? 'Selected month' : 'Inspect month'"
                        :aria-label="month.period === selectedMonthPeriod ? 'Selected month' : 'Inspect month'"
                        @click="selectMonth(month.period)"
                      >
                        <NgbIcon :name="month.period === selectedMonthPeriod ? 'selected-check' : 'inspect-check'" :size="22" />
                      </button>
                      <button
                        v-if="month.canClose"
                        class="ngb-iconbtn"
                        style="color: var(--ngb-success)"
                        :disabled="monthBusy"
                        title="Close month"
                        aria-label="Close month"
                        @click="openCloseMonthConfirm(month.period)"
                      >
                        <NgbIcon name="close-month" :size="22" />
                      </button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <div class="space-y-6">
          <section class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-5 shadow-card">
            <div class="flex items-start justify-between gap-4">
              <div>
                <div class="text-lg font-semibold text-ngb-text">Month Workspace</div>
                <div class="mt-1 text-sm text-ngb-muted">
                  Work on one month at a time with clear preconditions and reopen reason capture.
                </div>
              </div>
              <NgbBadge class="shrink-0 whitespace-nowrap" :tone="monthStateTone(selectedMonthStatus)">{{ monthStateLabel(selectedMonthStatus) }}</NgbBadge>
            </div>

            <div class="mt-5 space-y-4">
              <div>
                <label class="mb-1 block text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Month</label>
                <NgbMonthPicker :model-value="selectedMonth" @update:modelValue="setSelectedMonth(String($event || defaultMonthValueForYear(selectedYear)))" />
              </div>

              <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-4 py-3 text-sm">
                <div class="flex items-start gap-2">
                  <span class="font-medium text-ngb-text">{{ formatMonthValue(selectedMonth) }}</span>
                  <div class="ml-auto flex flex-nowrap items-center gap-2 overflow-x-auto">
                    <NgbBadge class="shrink-0 whitespace-nowrap" :tone="monthStateTone(selectedMonthStatus)">{{ monthStateLabel(selectedMonthStatus) }}</NgbBadge>
                    <NgbBadge v-if="selectedMonthStatus?.hasActivity" class="shrink-0 whitespace-nowrap" tone="neutral">Has activity</NgbBadge>
                  </div>
                </div>

                <div class="mt-2 text-ngb-muted">{{ selectedMonthSummary }}</div>
                <div v-if="selectedMonthStatus?.blockingReason" class="mt-2 text-xs text-ngb-muted">
                  {{ formatBlockingHint(selectedMonthStatus) }}
                </div>
              </div>

              <div
                v-if="selectedMonthStatus?.canReopen"
                class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-4 py-3"
              >
                <label class="mb-1 block text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Reopen Reason</label>
                <NgbInput
                  :model-value="reopenReason"
                  placeholder="Explain why the chain needs to be reopened"
                  @update:modelValue="reopenReason = String($event ?? '')"
                />
                <div class="mt-2 text-xs text-ngb-muted">
                  Reopen is audit-tracked and restricted to the latest closed month only.
                </div>
              </div>

              <NgbValidationSummary :messages="monthErrorMessages" />

              <div class="flex flex-wrap justify-end gap-2">
                <NgbButton
                  variant="secondary"
                  :disabled="monthReopenDisabled"
                  :loading="monthReopenLoading"
                  @click="openReopenMonthConfirm()"
                >
                  Reopen Month
                </NgbButton>
                <NgbButton
                  variant="primary"
                  :disabled="monthCloseDisabled"
                  :loading="monthActionLoading"
                  @click="openCloseMonthConfirm()"
                >
                  Close Month
                </NgbButton>
              </div>
            </div>
          </section>

          <section class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-5 shadow-card">
            <div class="flex items-start justify-between gap-4">
              <div>
                <div class="text-lg font-semibold text-ngb-text">Fiscal Year Close</div>
                <div class="mt-1 text-sm text-ngb-muted">
                  Post closing entries into the selected end month after the month chain is stable and retained earnings is chosen.
                </div>
              </div>
              <NgbBadge class="shrink-0 whitespace-nowrap" :tone="fiscalBadgeTone(fiscalStatus?.state)">{{ fiscalStateLabel(fiscalStatus?.state) }}</NgbBadge>
            </div>

            <div class="mt-5 space-y-4">
              <div>
                <label class="mb-1 block text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Fiscal Year End Month</label>
                <NgbMonthPicker
                  :model-value="selectedFiscalYearEndMonth"
                  @update:modelValue="setSelectedFiscalYearEndMonth(String($event || defaultMonthValueForYear(selectedYear)))"
                />
              </div>

              <div>
                <label class="mb-1 block text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Retained Earnings Account</label>
                <NgbLookup
                  :model-value="retainedEarnings"
                  :items="retainedEarningsItems"
                  placeholder="Type account code or name…"
                  @update:modelValue="retainedEarnings = $event"
                  @query="loadRetainedEarningsOptions"
                />
                <div class="mt-1 text-xs text-ngb-muted">
                  Eligible accounts only: active, equity-section, credit-normal, and no required dimensions.
                </div>
              </div>

              <div
                v-if="fiscalStatus?.canReopen"
                class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-4 py-3"
              >
                <label class="mb-1 block text-xs font-semibold uppercase tracking-[0.08em] text-ngb-muted">Reopen Reason</label>
                <NgbInput
                  :model-value="fiscalReopenReason"
                  placeholder="Explain why this fiscal year close must be reopened"
                  @update:modelValue="fiscalReopenReason = String($event ?? '')"
                />
                <div class="mt-2 text-xs text-ngb-muted">
                  Reopen removes the current fiscal year close effect, clears the redo lock, and{{ fiscalStatus?.reopenWillOpenEndPeriod ? ' reopens the end month if it was already closed.' : ' keeps the end month available for another close run.' }}
                </div>
              </div>

              <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-4 py-3 text-sm">
                <div class="flex items-center gap-2">
                  <span class="font-medium text-ngb-text">Execution state</span>
                  <div class="ml-auto flex flex-nowrap items-center gap-2 overflow-x-auto">
                    <NgbBadge class="shrink-0 whitespace-nowrap" :tone="fiscalBadgeTone(fiscalStatus?.state)">{{ fiscalStateLabel(fiscalStatus?.state) }}</NgbBadge>
                    <span v-if="fiscalLoading || retainedEarningsLoading" class="shrink-0 text-ngb-muted">Refreshing…</span>
                  </div>
                </div>

                <div class="mt-2 text-ngb-muted">{{ fiscalSummary }}</div>
                <div v-if="fiscalStatus?.reopenBlockingReason" class="mt-2 text-xs text-ngb-muted">
                  {{ formatFiscalReopenBlockingHint(fiscalStatus) }}
                </div>

                <div class="mt-2 text-xs text-ngb-muted">
                  <template v-if="fiscalStatus?.state === 'Completed'">
                    <template v-if="fiscalStatus?.canReopen">
                      Reopen will remove the current closing effect for {{ formatMonthValue(selectedFiscalYearEndMonth) }} and prepare it for redo.
                    </template>
                    <template v-else>
                      Closing recorded for {{ formatMonthValue(selectedFiscalYearEndMonth) }}.
                    </template>
                  </template>
                  <template v-else-if="fiscalStatus?.state === 'InProgress' || fiscalStatus?.state === 'StaleInProgress'">
                    Closing run is being tracked for {{ formatMonthValue(selectedFiscalYearEndMonth) }}.
                  </template>
                  <template v-else>
                    If you run it, the closing record will be created for {{ formatMonthValue(selectedFiscalYearEndMonth) }}.
                  </template>
                </div>

                <div v-if="fiscalStatus?.endPeriodClosed" class="mt-2 text-xs text-ngb-muted">
                  End period was closed by <span class="font-medium text-ngb-text">{{ fiscalStatus.endPeriodClosedBy || 'unknown' }}</span>
                  on <span class="font-medium text-ngb-text">{{ formatUtc(fiscalStatus.endPeriodClosedAtUtc) }}</span>.
                </div>

                <div v-if="fiscalStatus?.closedRetainedEarningsAccount" class="mt-2 text-xs text-ngb-muted">
                  Closed using <span class="font-medium text-ngb-text">{{ fiscalStatus.closedRetainedEarningsAccount.display }}</span>.
                </div>
              </div>

              <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-bg px-4 py-3 text-sm">
                <div class="font-medium text-ngb-text">Prior Months Checklist</div>
                <div v-if="!fiscalStatus" class="mt-2 text-ngb-muted">
                  Status is unavailable.
                </div>
                <div v-else-if="openPriorMonths.length === 0" class="mt-2 text-ngb-muted">
                  All prior months from January through the month before the selected end period are already closed.
                </div>
                <div v-else class="mt-2 space-y-2">
                  <div class="text-ngb-muted">Close these months first:</div>
                  <div class="flex flex-wrap gap-2">
                    <NgbBadge v-for="item in openPriorMonths" :key="item.period" class="whitespace-nowrap" tone="warn">
                      {{ formatDateOnly(item.period) }}
                    </NgbBadge>
                  </div>
                </div>
              </div>

              <NgbValidationSummary :messages="fiscalErrorMessages" />

              <div class="flex flex-wrap justify-end gap-2">
                <NgbButton
                  variant="secondary"
                  :loading="fiscalExecuting"
                  :disabled="fiscalReopenDisabled"
                  @click="fiscalReopenConfirmOpen = true"
                >
                  Reopen Fiscal Year
                </NgbButton>
                <NgbButton
                  variant="primary"
                  :loading="fiscalExecuting"
                  :disabled="fiscalCloseDisabled"
                  @click="fiscalConfirmOpen = true"
                >
                  Close Fiscal Year
                </NgbButton>
              </div>
            </div>
          </section>
        </div>
      </div>
    </div>

    <NgbConfirmDialog
      :open="monthConfirmOpen"
      title="Close month?"
      :message="`This will close ${formatMonthValue(pendingCloseMonthValue || selectedMonth)} and block future posting into it.`"
      confirm-text="Close Month"
      :confirm-loading="monthActionLoading"
      @update:open="monthConfirmOpen = $event"
      @confirm="executeMonthClose"
    />

    <NgbConfirmDialog
      :open="reopenConfirmOpen"
      title="Reopen month?"
      :message="`This will reopen ${formatMonthValue(pendingReopenMonthValue || selectedMonth)} and restore posting access into that month.`"
      confirm-text="Reopen Month"
      confirm-variant="danger"
      :confirm-loading="monthReopenLoading"
      @update:open="reopenConfirmOpen = $event"
      @confirm="executeMonthReopen"
    />

    <NgbConfirmDialog
      :open="fiscalConfirmOpen"
      title="Close fiscal year?"
      :message="`This posts closing entries into ${formatMonthValue(selectedFiscalYearEndMonth)} using the retained earnings account you selected.`"
      confirm-text="Close Fiscal Year"
      :confirm-loading="fiscalExecuting"
      @update:open="fiscalConfirmOpen = $event"
      @confirm="executeFiscalClose"
    />

    <NgbConfirmDialog
      :open="fiscalReopenConfirmOpen"
      title="Reopen fiscal year?"
      :message="fiscalStatus?.reopenWillOpenEndPeriod
        ? `This will remove the current fiscal year close, reopen ${formatMonthValue(selectedFiscalYearEndMonth)}, and let you run the close again.`
        : `This will remove the current fiscal year close for ${formatMonthValue(selectedFiscalYearEndMonth)} and let you run it again.`"
      confirm-text="Reopen Fiscal Year"
      confirm-variant="danger"
      :confirm-loading="fiscalExecuting"
      @update:open="fiscalReopenConfirmOpen = $event"
      @confirm="executeFiscalReopen"
    />
  </div>
</template>
