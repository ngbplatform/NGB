import {
  createConfiguredCatalogEntityEditorPersistence,
  type CatalogEntityPersistenceAdapter,
} from '@ngbplatform/ui'

import type { AgencyBillingEntityEditorPersistenceContext } from './agencyBillingEntityEditorPersistenceContext'

export function useCatalogEntityEditorPersistence(
  args: AgencyBillingEntityEditorPersistenceContext,
): CatalogEntityPersistenceAdapter {
  return createConfiguredCatalogEntityEditorPersistence(args)
}
