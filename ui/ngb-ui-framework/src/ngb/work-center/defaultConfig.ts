import { ApiError } from '../api/http'
import { getAccessToken, getAuthSnapshot, subscribeAuth } from '../auth/keycloak'
import { readAppEnv } from '../env/runtimeConfig'
import { workCenterHttpGateway } from './api'
import type { NgbWorkCenterConfig } from './config'
import { createSignalRWorkCenterClient } from './signalr'

function apiBaseUrl(): string {
  const configured = readAppEnv('VITE_API_BASE_URL')
  if (configured.length > 0) return configured
  // This helper is called only by the browser realtime factory below. Keeping
  // the SSR fallback in that factory avoids an unreachable branch here and
  // makes the browser origin the single source of truth for relative API use.
  return window.location.origin
}

export function createDefaultNgbWorkCenterConfig(): NgbWorkCenterConfig {
  return {
    gateway: workCenterHttpGateway,
    session: {
      getSnapshot: getAuthSnapshot,
      getAccessToken,
      subscribe: subscribeAuth,
    },
    createRealtimeClient: () => typeof window === 'undefined'
      ? { start: async () => undefined, stop: async () => undefined }
      : createSignalRWorkCenterClient({
          baseUrl: apiBaseUrl(),
          getAccessToken,
        }),
    isUnauthorizedError: (cause) =>
      cause instanceof ApiError && (cause.status === 401 || cause.status === 403),
  }
}
