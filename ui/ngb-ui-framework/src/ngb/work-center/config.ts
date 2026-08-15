import type { WorkCenterGateway } from './gateway'

export type WorkCenterSessionSnapshot = {
  authenticated: boolean
  subject?: string | null
}

export type WorkCenterSessionAdapter = {
  getSnapshot: () => WorkCenterSessionSnapshot
  getAccessToken: () => Promise<string | null>
  subscribe: (listener: (snapshot: WorkCenterSessionSnapshot) => void) => () => void
}

export type WorkCenterRealtimeHandlers = {
  changed: (version: number) => void
  reconnected: () => void
  disconnected: () => void
}

export type WorkCenterRealtimeClient = {
  start: (handlers: WorkCenterRealtimeHandlers) => Promise<void>
  stop: () => Promise<void>
}

export type NgbWorkCenterConfig = {
  gateway: WorkCenterGateway
  session: WorkCenterSessionAdapter
  createRealtimeClient: () => WorkCenterRealtimeClient
  isUnauthorizedError?: (cause: unknown) => boolean
}

let workCenterConfig: NgbWorkCenterConfig | null = null

export function configureNgbWorkCenter(config: NgbWorkCenterConfig): void {
  workCenterConfig = config
}

export function getConfiguredNgbWorkCenter(): NgbWorkCenterConfig {
  if (!workCenterConfig) {
    throw new Error('NGB Work Center is not configured. Call configureNgbWorkCenter(...) during app bootstrap.')
  }
  return workCenterConfig
}
