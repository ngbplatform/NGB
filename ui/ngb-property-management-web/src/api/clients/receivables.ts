import { httpGet, httpPost, type HttpRequestOptions } from '@ngbplatform/ui'
import type {
  ReceivablesApplyBatchRequestDto,
  ReceivablesApplyBatchResponseDto,
  ReceivablesOpenItemsDetailsResponseDto,
  ReceivablesReconciliationModeDto,
  ReceivablesReconciliationReportDto,
  ReceivablesSuggestFifoApplyRequestDto,
  ReceivablesSuggestFifoApplyResponseDto,
} from '../types/pmContracts'

export async function getReceivablesOpenItemsDetails(args: {
  leaseId: string
  partyId?: string | null
  propertyId?: string | null
  asOfMonth?: string | null // DateOnly: YYYY-MM-DD
  toMonth?: string | null   // DateOnly: YYYY-MM-DD
}, options?: HttpRequestOptions): Promise<ReceivablesOpenItemsDetailsResponseDto> {
  const query = {
    leaseId: args.leaseId,
    partyId: args.partyId,
    propertyId: args.propertyId,
    asOfMonth: args.asOfMonth,
    toMonth: args.toMonth,
  }
  return options
    ? await httpGet<ReceivablesOpenItemsDetailsResponseDto>('/api/receivables/open-items/details', query, options)
    : await httpGet<ReceivablesOpenItemsDetailsResponseDto>('/api/receivables/open-items/details', query)
}

export async function suggestLeaseFifoApply(
  request: ReceivablesSuggestFifoApplyRequestDto,
  options?: HttpRequestOptions,
): Promise<ReceivablesSuggestFifoApplyResponseDto> {
  return options
    ? await httpPost<ReceivablesSuggestFifoApplyResponseDto>('/api/receivables/apply/fifo/suggest/lease', request, options)
    : await httpPost<ReceivablesSuggestFifoApplyResponseDto>('/api/receivables/apply/fifo/suggest/lease', request)
}

export async function applyReceivablesBatch(
  request: ReceivablesApplyBatchRequestDto,
): Promise<ReceivablesApplyBatchResponseDto> {
  return await httpPost<ReceivablesApplyBatchResponseDto>('/api/receivables/apply/batch', request)
}

export async function unapplyReceivablesApply(applyId: string): Promise<void> {
  await httpPost<void>(`/api/receivables/apply/${encodeURIComponent(applyId)}/unapply`, {})
}

export async function getReceivablesReconciliation(args: {
  fromMonthInclusive: string // YYYY-MM-DD
  toMonthInclusive: string   // YYYY-MM-DD
  mode?: ReceivablesReconciliationModeDto | null
}, options?: HttpRequestOptions): Promise<ReceivablesReconciliationReportDto> {
  const query = {
    fromMonthInclusive: args.fromMonthInclusive,
    toMonthInclusive: args.toMonthInclusive,
    mode: args.mode,
  }
  return options
    ? await httpGet<ReceivablesReconciliationReportDto>('/api/receivables/reconciliation', query, options)
    : await httpGet<ReceivablesReconciliationReportDto>('/api/receivables/reconciliation', query)
}
