import { httpGet, httpPost } from 'ngb-ui-framework'
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
}): Promise<PayablesOpenItemsDetailsResponseDto> {
  return await httpGet<PayablesOpenItemsDetailsResponseDto>('/api/payables/open-items/details', {
    partyId: args.partyId,
    propertyId: args.propertyId,
    asOfMonth: args.asOfMonth,
    toMonth: args.toMonth,
  })
}

export async function suggestPayablesFifoApply(
  request: PayablesSuggestFifoApplyRequestDto,
): Promise<PayablesSuggestFifoApplyResponseDto> {
  return await httpPost<PayablesSuggestFifoApplyResponseDto>('/api/payables/apply/fifo/suggest', request)
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
}): Promise<PayablesReconciliationReportDto> {
  return await httpGet<PayablesReconciliationReportDto>('/api/payables/reconciliation', {
    fromMonthInclusive: args.fromMonthInclusive,
    toMonthInclusive: args.toMonthInclusive,
    mode: args.mode,
  })
}
