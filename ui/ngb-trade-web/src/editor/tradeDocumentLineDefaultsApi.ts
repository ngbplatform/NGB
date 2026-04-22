import { httpPost, type ReferenceValue } from 'ngb-ui-framework'

export type TradeDocumentLineDefaultsRequest = {
  documentType: string
  asOfDate?: string | null
  warehouseId?: string | null
  priceTypeId?: string | null
  salesInvoiceId?: string | null
  purchaseReceiptId?: string | null
  rows: TradeDocumentLineDefaultsRowRequest[]
}

export type TradeDocumentLineDefaultsRowRequest = {
  rowKey: string
  itemId: string
  priceTypeId?: string | null
}

export type TradeDocumentLineDefaultsResponse = {
  rows: TradeDocumentLineDefaultsRowResult[]
}

export type TradeDocumentLineDefaultsRowResult = {
  rowKey: string
  priceType?: ReferenceValue | null
  unitPrice?: number | null
  currency?: string | null
  unitCost?: number | null
}

export async function resolveTradeDocumentLineDefaults(
  request: TradeDocumentLineDefaultsRequest,
  signal?: AbortSignal,
): Promise<TradeDocumentLineDefaultsResponse> {
  return await httpPost<TradeDocumentLineDefaultsResponse>(
    '/api/trade/document-line-defaults/resolve',
    request,
    { signal },
  )
}
