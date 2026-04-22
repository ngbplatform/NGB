import type { RouteLocationNormalizedLoaded, Router } from 'vue-router'

import { normalizeTrashMode, useRouteQueryMigration } from 'ngb-ui-framework'

export function usePropertiesLegacyQueryCompat(
  route: RouteLocationNormalizedLoaded,
  router: Router,
) {
  useRouteQueryMigration({
    route,
    router,
    sources: () => [route.query.trash, route.query.bTrash, route.query.uTrash] as const,
    migrate: ([legacyTrash, buildingTrash, unitTrash]) => {
      if (legacyTrash == null) return null

      if (buildingTrash != null || unitTrash != null) {
        return { trash: undefined }
      }

      const normalized = normalizeTrashMode(legacyTrash)
      return {
        trash: undefined,
        bTrash: normalized,
        uTrash: normalized,
        bOffset: 0,
        uOffset: 0,
      }
    },
  })
}
