import { httpGet, httpPost, type HttpRequestOptions } from '@ngbplatform/ui'
import type {
  PayablesApplyBatchRequestDto,
  PayablesApplyBatchResponseDto,
  PayablesOpenItemsDetailsResponseDto,
  PayablesReconciliationModeDto,
  PayablesReconciliationReportDto,
  PayablesSuggestFifoApplyRequestDto,
  PayablesSuggestFifoApplyResponseDto,
} from '../types/pmContracts'

export async function getPayablesOpenItemsDetails(args: {
  partyId: string
  propertyId: string
  asOfMonth?: string | null // DateOnly: YYYY-MM-DD
  toMonth?: string | null   // DateOnly: YYYY-MM-DD
}, options?: HttpRequestOptions): Promise<PayablesOpenItemsDetailsResponseDto> {
  const query = {
    partyId: args.partyId,
    propertyId: args.propertyId,
    asOfMonth: args.asOfMonth,
    toMonth: args.toMonth,
  }
  return options
    ? await httpGet<PayablesOpenItemsDetailsResponseDto>('/api/payables/open-items/details', query, options)
    : await httpGet<PayablesOpenItemsDetailsResponseDto>('/api/payables/open-items/details', query)
}

export async function suggestPayablesFifoApply(
  request: PayablesSuggestFifoApplyRequestDto,
  options?: HttpRequestOptions,
): Promise<PayablesSuggestFifoApplyResponseDto> {
  return options
    ? await httpPost<PayablesSuggestFifoApplyResponseDto>('/api/payables/apply/fifo/suggest', request, options)
    : await httpPost<PayablesSuggestFifoApplyResponseDto>('/api/payables/apply/fifo/suggest', request)
}

export async function applyPayablesBatch(
  request: PayablesApplyBatchRequestDto,
): Promise<PayablesApplyBatchResponseDto> {
  return await httpPost<PayablesApplyBatchResponseDto>('/api/payables/apply/batch', request)
}

export async function unapplyPayablesApply(applyId: string): Promise<void> {
  await httpPost(`/api/payables/apply/${encodeURIComponent(applyId)}/unapply`, {})
}

export async function getPayablesReconciliation(args: {
  fromMonthInclusive: string // YYYY-MM-DD
  toMonthInclusive: string   // YYYY-MM-DD
  mode?: PayablesReconciliationModeDto | null
}, options?: HttpRequestOptions): Promise<PayablesReconciliationReportDto> {
  const query = {
    fromMonthInclusive: args.fromMonthInclusive,
    toMonthInclusive: args.toMonthInclusive,
    mode: args.mode,
  }
  return options
    ? await httpGet<PayablesReconciliationReportDto>('/api/payables/reconciliation', query, options)
    : await httpGet<PayablesReconciliationReportDto>('/api/payables/reconciliation', query)
}
