/** Curated public surface for Work Center hosts and vertical adapters. */
export { default as NgbNotificationPreferencesPage } from './ngb/work-center/NgbNotificationPreferencesPage.vue'
export { default as NgbWorkCenterDrawer } from './ngb/work-center/NgbWorkCenterDrawer.vue'
export { default as NgbWorkCenterPage } from './ngb/work-center/NgbWorkCenterPage.vue'
export { configureNgbWorkCenter, getConfiguredNgbWorkCenter } from './ngb/work-center/config'
export { createDefaultNgbWorkCenterConfig } from './ngb/work-center/defaultConfig'
export { useWorkCenter } from './ngb/work-center/useWorkCenter'
export type {
  NgbWorkCenterConfig,
  WorkCenterRealtimeClient,
  WorkCenterRealtimeHandlers,
  WorkCenterSessionAdapter,
  WorkCenterSessionSnapshot,
} from './ngb/work-center/config'
export type { WorkCenterGateway } from './ngb/work-center/gateway'
export type {
  NotificationPreference,
  NotificationSeverity,
  WorkCenterItem,
  WorkCenterItemKind,
  WorkCenterPage,
  WorkCenterPreferenceKind,
  WorkCenterPriority,
  WorkCenterQuery,
  WorkCenterSummary,
  WorkCenterTaskStatus,
} from './ngb/work-center/types'
export type { NgbWorkCenterRuntime } from './ngb/work-center/useWorkCenter'
