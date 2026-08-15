import {
  createConfiguredCatalogEntityEditorPersistence,
  type CatalogEntityPersistenceAdapter,
} from '@ngbplatform/ui'

import type { CRMEntityEditorPersistenceContext } from './crmEntityEditorPersistenceContext'

export function useCatalogEntityEditorPersistence(
  args: CRMEntityEditorPersistenceContext,
): CatalogEntityPersistenceAdapter {
  return createConfiguredCatalogEntityEditorPersistence(args)
}
