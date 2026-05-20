export const PM_DOCUMENT_TYPES = {
  lease: 'pm.lease',
  rentCharge: 'pm.rent_charge',
  receivableCharge: 'pm.receivable_charge',
  lateFeeCharge: 'pm.late_fee_charge',
  receivablePayment: 'pm.receivable_payment',
  receivableReturnedPayment: 'pm.receivable_returned_payment',
  receivableCreditMemo: 'pm.receivable_credit_memo',
  receivableApply: 'pm.receivable_apply',
  maintenanceRequest: 'pm.maintenance_request',
  workOrder: 'pm.work_order',
  workOrderCompletion: 'pm.work_order_completion',
} as const;

export type PmDocumentType = typeof PM_DOCUMENT_TYPES[keyof typeof PM_DOCUMENT_TYPES];
