import {
  createConfiguredDocumentEntityEditorPersistence,
  type DocumentEntityPersistenceAdapter,
} from '@ngbplatform/ui'

import type { CRMEntityEditorPersistenceContext } from './crmEntityEditorPersistenceContext'
import {
  buildCRMDocumentPartsPayload,
  hydrateCRMDocumentPartLookupRows,
  syncCRMDocumentAmountField,
} from './documentParts'

export function useDocumentEntityEditorPersistence(
  args: CRMEntityEditorPersistenceContext,
): DocumentEntityPersistenceAdapter {
  return createConfiguredDocumentEntityEditorPersistence(args, {
    buildPayload: ({ partsMeta, partsModel }) =>
      buildCRMDocumentPartsPayload(partsMeta, partsModel),
    hydrate: ({ entityTypeCode, partsMeta, partsModel, lookupStore }) =>
      hydrateCRMDocumentPartLookupRows({
        entityTypeCode,
        partsMeta,
        partsModel,
        lookupStore,
      }),
    synchronize: ({ partsMeta, partsModel, model }) =>
      syncCRMDocumentAmountField({ partsMeta, partsModel, model }),
  })
}
