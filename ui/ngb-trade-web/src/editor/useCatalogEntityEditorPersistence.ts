import {
  createConfiguredCatalogEntityEditorPersistence,
  type CatalogEntityPersistenceAdapter,
} from '@ngbplatform/ui'

import type { TradeEntityEditorPersistenceContext } from './tradeEntityEditorPersistenceContext'

export function useCatalogEntityEditorPersistence(
  args: TradeEntityEditorPersistenceContext,
): CatalogEntityPersistenceAdapter {
  return createConfiguredCatalogEntityEditorPersistence(args)
}
