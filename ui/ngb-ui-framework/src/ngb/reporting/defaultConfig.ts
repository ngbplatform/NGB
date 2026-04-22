import { buildLookupFieldTargetUrl } from '../lookup/navigation'
import { useLookupStore } from '../lookup/store'
import type { ReportingFrameworkConfig } from './config'

export function createDefaultNgbReportingConfig(): ReportingFrameworkConfig {
  return {
    useLookupStore: () => useLookupStore(),
    resolveLookupTarget: async ({ hint, value, routeFullPath }) =>
      await buildLookupFieldTargetUrl({
        hint,
        value,
        route: { fullPath: routeFullPath },
      }),
  }
}
