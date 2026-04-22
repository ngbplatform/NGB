import type { NavigationGuardWithThis } from 'vue-router'

export type AuthGuardStore = {
  redirecting: boolean
  initialized: boolean
  initializing: boolean
  authenticated: boolean
  error: string | null
  initialize(): Promise<void>
  login(targetPath?: string | null): Promise<void>
}

export function createAuthGuard(resolveAuthStore: () => AuthGuardStore): NavigationGuardWithThis<undefined> {
  return async (to) => {
    if (to.meta?.public === true) return true

    const auth = resolveAuthStore()
    if (auth.redirecting) return false

    if (!auth.initialized && !auth.initializing) {
      try {
        await auth.initialize()
      } catch {
        return true
      }
    }

    if (auth.authenticated) return true
    if (auth.initializing || auth.error) return false

    await auth.login(to.fullPath)
    return false
  }
}

