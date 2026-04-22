import { computed, type ComputedRef, type Ref } from 'vue'

import type { OpenItemsSummary, OpenItemsTabKey } from './presentation'
import { fmtDateOnly, fmtMoney } from './shared'

type OpenItemsChargeLike = {
  chargeDocumentId: string
}

type OpenItemsCreditLike = {
  creditDocumentId: string
}

type OpenItemsDataLike<
  TCharge extends OpenItemsChargeLike,
  TCredit extends OpenItemsCreditLike,
  TAllocation = unknown,
> = {
  totalOutstanding?: number | null
  totalCredit?: number | null
  charges?: TCharge[] | null
  credits?: TCredit[] | null
  allocations?: TAllocation[] | null
}

type UseOpenItemsPagePresentationArgs<
  TCharge extends OpenItemsChargeLike,
  TCredit extends OpenItemsCreditLike,
  TAllocation = unknown,
> = {
  data: Ref<OpenItemsDataLike<TCharge, TCredit, TAllocation> | null>
  focusItemId: ComputedRef<string | null>
  sourceDocumentType: ComputedRef<string | null>
  resolveTabFromSourceType: (sourceDocumentType: string | null) => OpenItemsTabKey | null
  buildFocusedChargeBadge: (charge: TCharge) => string
  buildFocusedCreditBadge: (credit: TCredit) => string
}

export function formatOpenItemsDateCell(value: unknown): string {
  return fmtDateOnly(typeof value === 'string' ? value : null)
}

export function formatOpenItemsMoneyCell(value: unknown): string {
  return fmtMoney(Number(value ?? 0))
}

export function useOpenItemsPagePresentation<
  TCharge extends OpenItemsChargeLike,
  TCredit extends OpenItemsCreditLike,
  TAllocation = unknown,
>(args: UseOpenItemsPagePresentationArgs<TCharge, TCredit, TAllocation>) {
  const summary = computed<OpenItemsSummary>(() => ({
    totalOutstanding: args.data.value?.totalOutstanding ?? 0,
    totalCredit: args.data.value?.totalCredit ?? 0,
    chargesCount: args.data.value?.charges?.length ?? 0,
    creditsCount: args.data.value?.credits?.length ?? 0,
    allocationsCount: args.data.value?.allocations?.length ?? 0,
  }))

  const focusedCharge = computed(() => {
    const id = args.focusItemId.value
    if (!id) return null
    return (args.data.value?.charges ?? []).find((item) => item.chargeDocumentId === id) ?? null
  })

  const focusedCredit = computed(() => {
    const id = args.focusItemId.value
    if (!id) return null
    return (args.data.value?.credits ?? []).find((item) => item.creditDocumentId === id) ?? null
  })

  const focusedContextBadge = computed(() => {
    if (focusedCharge.value) return args.buildFocusedChargeBadge(focusedCharge.value)
    if (focusedCredit.value) return args.buildFocusedCreditBadge(focusedCredit.value)
    return null
  })

  const preferredTabFromRoute = computed<OpenItemsTabKey | null>(() => {
    const routePreferred = args.resolveTabFromSourceType(args.sourceDocumentType.value)
    if (routePreferred) return routePreferred
    if (focusedCredit.value) return 'credits'
    if (focusedCharge.value) return 'charges'
    return null
  })

  return {
    summary,
    focusedCharge,
    focusedCredit,
    focusedContextBadge,
    preferredTabFromRoute,
  }
}
