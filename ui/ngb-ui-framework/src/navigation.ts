/** Curated public surface for host-owned navigation composition. */
export {
  configureNgbNavigation,
  resolveNgbNavigationRoutes,
  resolveNgbNavigationTarget,
  resolveNgbPageContext,
} from './ngb/navigation/config'
export type {
  NgbNavigationConfig,
  NgbNavigationContext,
  NgbNavigationLocation,
  NgbNavigationRoutes,
  NgbNavigationTarget,
  NgbPageContext,
} from './ngb/navigation/types'
export { NgbSourceKinds } from './ngb/api/sourceKinds'
export type { NgbSourceIdentity, NgbSourceKind } from './ngb/api/sourceKinds'
