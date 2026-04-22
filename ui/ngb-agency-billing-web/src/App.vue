<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NgbCommandPaletteDialog,
  NgbSiteShell,
  normalizeNgbRouteAliasPath,
  useAuthStore,
  useCommandPaletteHotkeys,
  useCommandPaletteStore,
  useMainMenuStore,
} from 'ngb-ui-framework'
import type { SiteNavNode, SiteQuickLink } from 'ngb-ui-framework'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const menu = useMainMenuStore()
const palette = useCommandPaletteStore()
const shellHydrated = ref(false)

useCommandPaletteHotkeys()

watch(
  () => auth.authenticated,
  async (authenticated) => {
    if (!authenticated || shellHydrated.value) return

    shellHydrated.value = true
    await Promise.all([
      menu.load(),
      palette.hydrate(),
    ])
  },
  { immediate: true },
)

watch(
  () => route.fullPath,
  (nextPath) => {
    palette.setCurrentRoute(nextPath)
  },
  { immediate: true },
)

function groupId(label: string): string {
  const slug = label
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')

  return `group:${slug || 'menu'}`
}

function isExternalRoute(value: string | null | undefined): value is string {
  const routeValue = String(value ?? '').trim()
  return /^https?:\/\//i.test(routeValue)
}

function navigateTo(targetRoute: string | null | undefined): void {
  const value = String(targetRoute ?? '').trim()
  if (!value) return

  if (isExternalRoute(value)) {
    if (typeof window !== 'undefined') window.location.assign(value)
    return
  }

  void router.push(normalizeNgbRouteAliasPath(value))
}

function signOut(): void {
  void auth.logout()
}

function retryAuthentication(): void {
  void auth.initialize()
    .then(async () => {
      if (!auth.authenticated && !auth.error) {
        await auth.login(route.fullPath)
      }
    })
    .catch(() => {
      // The auth store already captures the user-facing error state.
    })
}

function routeMatches(targetRoute: string | null | undefined, currentPath: string): boolean {
  const normalizedTarget = normalizeNgbRouteAliasPath(targetRoute)
  const normalizedCurrent = normalizeNgbRouteAliasPath(currentPath)

  if (!normalizedTarget || !normalizedCurrent) return false
  if (normalizedCurrent === normalizedTarget) return true

  return normalizedCurrent.startsWith(`${normalizedTarget}/`)
}

const nodes = computed<SiteNavNode[]>(() => {
  return (menu.groups ?? [])
    .slice()
    .sort((a, b) => a.ordinal - b.ordinal)
    .map((group) => {
      const id = groupId(group.label)
      const items = group.items
        .slice()
        .sort((a, b) => a.ordinal - b.ordinal)

      const rootLeaf = items.length === 1 && items[0]?.label === group.label

      if (rootLeaf) {
        return {
          id,
          label: group.label,
          route: items[0]?.route,
          icon: group.icon ?? items[0]?.icon ?? null,
        }
      }

      return {
        id,
        label: group.label,
        icon: group.icon ?? null,
        children: items.map((item) => ({
          id: `${item.kind}:${item.code}`,
          label: item.label,
          route: item.route,
          icon: item.icon ?? null,
        })),
      }
    })
})

const selectedId = computed<string | null>(() => {
  const path = route.path
  for (const group of nodes.value) {
    if (routeMatches(group.route, path)) return group.id
    for (const child of group.children ?? []) {
      if (routeMatches(child.route, path)) return child.id
    }
  }
  return null
})

const pinned = computed<SiteQuickLink[]>(() => [])
const recent = computed<SiteQuickLink[]>(() => [])
const isBareRoute = computed(() => route.matched.some((record) => record.meta?.bare === true))
const showBlockingAuthState = computed(() => auth.initializing || auth.redirecting || (!auth.authenticated && !!auth.error))
const authStateTitle = computed(() => {
  if (auth.redirecting) return 'Redirecting to secure sign-in'
  if (auth.initializing) return 'Connecting to Keycloak'
  return 'Unable to start the secure session'
})
const authStateDetail = computed(() => {
  if (auth.redirecting) return 'You will be sent to the login page in a moment.'
  if (auth.initializing) return 'Checking whether an existing SSO session is already available.'
  return auth.error ?? 'The UI could not initialize the Keycloak adapter.'
})
</script>

<template>
  <div
    v-if="showBlockingAuthState"
    class="min-h-screen bg-ngb-bg text-ngb-text flex items-center justify-center px-6"
  >
    <div class="w-full max-w-[520px] rounded-[28px] border border-ngb-border bg-ngb-card p-8 shadow-card">
      <div class="text-xs font-semibold uppercase tracking-[0.18em] text-ngb-muted">Security</div>
      <h1 class="mt-4 text-2xl font-semibold tracking-tight">{{ authStateTitle }}</h1>
      <p class="mt-3 text-sm leading-6 text-ngb-muted">{{ authStateDetail }}</p>

      <div v-if="!auth.initializing && !auth.redirecting" class="mt-6 flex flex-wrap gap-3">
        <button
          class="rounded-[var(--ngb-radius)] bg-ngb-blue px-4 py-2 text-sm font-semibold text-white ngb-focus"
          @click="retryAuthentication"
        >
          Retry
        </button>
        <button
          class="rounded-[var(--ngb-radius)] border border-ngb-border px-4 py-2 text-sm font-semibold text-ngb-text ngb-focus"
          @click="auth.login(route.fullPath)"
        >
          Sign in
        </button>
      </div>
    </div>
  </div>
  <router-view v-else-if="isBareRoute" />
  <NgbSiteShell
    v-else
    module-title="Agency Billing"
    product-title="NGB"
    :user-name="auth.userName"
    :user-email="auth.email"
    :user-meta="auth.primaryRoleLabel"
    :user-meta-icon="auth.primaryRoleIcon"
    :pinned="pinned"
    :recent="recent"
    :nodes="nodes"
    :selected-id="selectedId"
    @navigate="navigateTo"
    @select="(_id, nextRoute) => navigateTo(nextRoute)"
    @openPalette="palette.open()"
    @signOut="signOut"
  >
    <router-view />
  </NgbSiteShell>
  <NgbCommandPaletteDialog v-if="!showBlockingAuthState && !isBareRoute" />
</template>
