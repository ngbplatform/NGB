import {
  createConfiguredDocumentEntityEditorPersistence,
  type DocumentEntityPersistenceAdapter,
} from '@ngbplatform/ui'

import type { TradeEntityEditorPersistenceContext } from './tradeEntityEditorPersistenceContext'
import {
  buildTradeDocumentPartsPayload,
  hydrateTradeDocumentPartLookupRows,
  syncTradeDocumentAmountField,
} from './documentParts'

export function useDocumentEntityEditorPersistence(
  args: TradeEntityEditorPersistenceContext,
): DocumentEntityPersistenceAdapter {
  return createConfiguredDocumentEntityEditorPersistence(args, {
    buildPayload: ({ partsMeta, partsModel }) =>
      buildTradeDocumentPartsPayload(partsMeta, partsModel),
    hydrate: ({ entityTypeCode, partsMeta, partsModel, lookupStore }) =>
      hydrateTradeDocumentPartLookupRows({
        entityTypeCode,
        partsMeta,
        partsModel,
        lookupStore,
      }),
    synchronize: ({ partsMeta, partsModel, model }) =>
      syncTradeDocumentAmountField({ partsMeta, partsModel, model }),
  })
}
