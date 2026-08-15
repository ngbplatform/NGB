import {
  createConfiguredDocumentEntityEditorPersistence,
  type DocumentEntityPersistenceAdapter,
} from '@ngbplatform/ui'

import type { AgencyBillingEntityEditorPersistenceContext } from './agencyBillingEntityEditorPersistenceContext'
import {
  buildAgencyBillingDocumentPartsPayload,
  hydrateAgencyBillingDocumentPartLookupRows,
  syncAgencyBillingDocumentComputedFields,
} from './documentParts'

export function useDocumentEntityEditorPersistence(
  args: AgencyBillingEntityEditorPersistenceContext,
): DocumentEntityPersistenceAdapter {
  return createConfiguredDocumentEntityEditorPersistence(args, {
    buildPayload: ({ documentType, partsMeta, partsModel }) =>
      buildAgencyBillingDocumentPartsPayload(documentType, partsMeta, partsModel),
    hydrate: ({ entityTypeCode, partsMeta, partsModel, lookupStore }) =>
      hydrateAgencyBillingDocumentPartLookupRows({
        entityTypeCode,
        partsMeta,
        partsModel,
        lookupStore,
      }),
    synchronize: ({ documentType, partsMeta, partsModel, model }) =>
      syncAgencyBillingDocumentComputedFields({
        documentType,
        partsMeta,
        partsModel,
        model,
      }),
  })
}
