import type { RecordPayload } from 'ngb-ui-framework'

export type ReceivablesOpenChargeItemDetailsDto = {
  chargeDocumentId: string
  documentType: string
  number?: string | null
  chargeDisplay?: string | null
  dueOnUtc: string
  chargeTypeId?: string | null
  chargeTypeDisplay?: string | null
  memo?: string | null
  originalAmount: number
  outstandingAmount: number
}

export type ReceivablesOpenCreditItemDetailsDto = {
  creditDocumentId: string
  documentType: string
  number?: string | null
  creditDocumentDisplay?: string | null
  receivedOnUtc: string
  memo?: string | null
  originalAmount: number
  availableCredit: number
}

export type ReceivablesAllocationDetailsDto = {
  applyId: string
  applyDisplay?: string | null
  applyNumber?: string | null
  creditDocumentId: string
  creditDocumentType: string
  creditDocumentDisplay?: string | null
  creditDocumentNumber?: string | null
  chargeDocumentId: string
  chargeDocumentType: string
  chargeDisplay?: string | null
  chargeNumber?: string | null
  appliedOnUtc: string
  amount: number
  isPosted: boolean
}

export type ReceivablesOpenItemsDetailsResponseDto = {
  registerId: string
  partyId: string
  partyDisplay?: string | null
  propertyId: string
  propertyDisplay?: string | null
  leaseId: string
  leaseDisplay?: string | null
  charges: ReceivablesOpenChargeItemDetailsDto[]
  credits: ReceivablesOpenCreditItemDetailsDto[]
  allocations: ReceivablesAllocationDetailsDto[]
  totalOutstanding: number
  totalCredit: number
}

export type ReceivablesApplyWarningDto = {
  code: string
  message: string
}

export type ReceivablesSuggestedLeaseApplyDto = {
  applyId?: string | null
  creditDocumentId: string
  creditDocumentType: string
  creditDocumentDisplay?: string | null
  creditDocumentDateUtc: string
  creditAmountBefore: number
  creditAmountAfter: number
  chargeDocumentId: string
  chargeDisplay?: string | null
  chargeDueOnUtc: string
  chargeOutstandingBefore: number
  chargeOutstandingAfter: number
  amount: number
  applyPayload: RecordPayload
}

export type ReceivablesSuggestFifoApplyRequestDto = {
  leaseId: string
  partyId?: string | null
  propertyId?: string | null
  asOfMonth?: string | null
  toMonth?: string | null
  limit?: number | null
  createDrafts?: boolean
}

export type ReceivablesSuggestFifoApplyResponseDto = {
  registerId: string
  partyId: string
  partyDisplay?: string | null
  propertyId: string
  propertyDisplay?: string | null
  leaseId: string
  leaseDisplay?: string | null
  totalOutstanding: number
  totalCredit: number
  totalApplied: number
  remainingOutstanding: number
  remainingCredit: number
  suggestedApplies: ReceivablesSuggestedLeaseApplyDto[]
  warnings: ReceivablesApplyWarningDto[]
}

export type ReceivablesApplyBatchItemDto = {
  applyId?: string | null
  applyPayload: RecordPayload
}

export type ReceivablesApplyBatchRequestDto = {
  applies: ReceivablesApplyBatchItemDto[]
}

export type ReceivablesApplyBatchExecutedItemDto = {
  applyId: string
  creditDocumentId: string
  chargeDocumentId: string
  appliedOnUtc: string
  amount: number
  createdDraft: boolean
}

export type ReceivablesApplyBatchResponseDto = {
  registerId: string
  totalApplied: number
  executedApplies: ReceivablesApplyBatchExecutedItemDto[]
}

export type ReceivablesReconciliationModeDto = 'Movement' | 'Balance'

export type ReceivablesReconciliationRowKindDto = 'Matched' | 'Mismatch' | 'GlOnly' | 'OpenItemsOnly'

export type ReceivablesReconciliationRowDto = {
  partyId: string
  partyDisplay?: string | null
  propertyId: string
  propertyDisplay?: string | null
  leaseId: string
  leaseDisplay?: string | null
  arNet: number
  openItemsNet: number
  diff: number
  rowKind: ReceivablesReconciliationRowKindDto
  hasDiff: boolean
}

export type ReceivablesReconciliationReportDto = {
  fromMonthInclusive: string
  toMonthInclusive: string
  mode: ReceivablesReconciliationModeDto
  arAccountId: string
  openItemsRegisterId: string
  totalArNet: number
  totalOpenItemsNet: number
  totalDiff: number
  rowCount: number
  mismatchRowCount: number
  rows: ReceivablesReconciliationRowDto[]
}

export type PayablesOpenChargeItemDetailsDto = {
  chargeDocumentId: string
  documentType: string
  number?: string | null
  chargeDisplay?: string | null
  dueOnUtc: string
  chargeTypeId?: string | null
  chargeTypeDisplay?: string | null
  vendorInvoiceNo?: string | null
  memo?: string | null
  originalAmount: number
  outstandingAmount: number
}

export type PayablesOpenCreditItemDetailsDto = {
  creditDocumentId: string
  documentType: string
  number?: string | null
  creditDocumentDisplay?: string | null
  creditDocumentDateUtc: string
  memo?: string | null
  originalAmount: number
  availableCredit: number
}

export type PayablesAllocationDetailsDto = {
  applyId: string
  applyDisplay?: string | null
  applyNumber?: string | null
  creditDocumentId: string
  creditDocumentType: string
  creditDocumentDisplay?: string | null
  creditDocumentNumber?: string | null
  chargeDocumentId: string
  chargeDocumentType: string
  chargeDisplay?: string | null
  chargeNumber?: string | null
  appliedOnUtc: string
  amount: number
  isPosted: boolean
}

export type PayablesOpenItemsDetailsResponseDto = {
  registerId: string
  vendorId: string
  vendorDisplay?: string | null
  propertyId: string
  propertyDisplay?: string | null
  charges: PayablesOpenChargeItemDetailsDto[]
  credits: PayablesOpenCreditItemDetailsDto[]
  allocations: PayablesAllocationDetailsDto[]
  totalOutstanding: number
  totalCredit: number
}

export type PayablesReconciliationModeDto = 'Movement' | 'Balance'

export type PayablesReconciliationRowKindDto = 'Matched' | 'Mismatch' | 'GlOnly' | 'OpenItemsOnly'

export type PayablesReconciliationRowDto = {
  vendorId: string
  vendorDisplay?: string | null
  propertyId: string
  propertyDisplay?: string | null
  apNet: number
  openItemsNet: number
  diff: number
  rowKind: PayablesReconciliationRowKindDto
  hasDiff: boolean
}

export type PayablesReconciliationReportDto = {
  fromMonthInclusive: string
  toMonthInclusive: string
  mode: PayablesReconciliationModeDto
  apAccountId: string
  openItemsRegisterId: string
  totalApNet: number
  totalOpenItemsNet: number
  totalDiff: number
  rowCount: number
  mismatchRowCount: number
  rows: PayablesReconciliationRowDto[]
}

export type PayablesApplyWarningDto = {
  code: string
  message: string
}

export type PayablesSuggestedApplyDto = {
  applyId?: string | null
  creditDocumentId: string
  creditDocumentType: string
  creditDocumentDisplay?: string | null
  creditDocumentDateUtc: string
  creditAmountBefore: number
  creditAmountAfter: number
  chargeDocumentId: string
  chargeDisplay?: string | null
  chargeDueOnUtc: string
  chargeOutstandingBefore: number
  chargeOutstandingAfter: number
  amount: number
  applyPayload: RecordPayload
}

export type PayablesSuggestFifoApplyRequestDto = {
  partyId: string
  propertyId: string
  asOfMonth?: string | null
  toMonth?: string | null
  limit?: number | null
  createDrafts?: boolean
}

export type PayablesSuggestFifoApplyResponseDto = {
  registerId: string
  vendorId: string
  vendorDisplay?: string | null
  propertyId: string
  propertyDisplay?: string | null
  totalOutstanding: number
  totalCredit: number
  totalApplied: number
  remainingOutstanding: number
  remainingCredit: number
  suggestedApplies: PayablesSuggestedApplyDto[]
  warnings: PayablesApplyWarningDto[]
}

export type PayablesApplyBatchItemDto = {
  applyId?: string | null
  applyPayload: RecordPayload
}

export type PayablesApplyBatchRequestDto = {
  applies: PayablesApplyBatchItemDto[]
}

export type PayablesApplyBatchExecutedItemDto = {
  applyId: string
  creditDocumentId: string
  chargeDocumentId: string
  appliedOnUtc: string
  amount: number
  createdDraft: boolean
}

export type PayablesApplyBatchResponseDto = {
  registerId: string
  totalApplied: number
  executedApplies: PayablesApplyBatchExecutedItemDto[]
}

export type PayablesUnapplyResponseDto = {
  applyId: string
  creditDocumentId: string
  chargeDocumentId: string
  appliedOnUtc: string
  unappliedAmount: number
}
