<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NgbCommandPaletteDialog,
  NgbSiteShell,
  useAccessStore,
  useAuthStore,
  useCommandPaletteHotkeys,
  useCommandPaletteStore,
  useMainMenuStore,
} from '@ngbplatform/ui'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const access = useAccessStore()
const menu = useMainMenuStore()
const palette = useCommandPaletteStore()
useCommandPaletteHotkeys()
const nodes = computed(() => menu.groups.map((group) => {
  const items = group.items.map((item) => ({
    id: item.route,
    label: item.label,
    route: item.route,
    icon: item.icon,
  }))
  // A one-item group with the same label represents a standalone root link.
  if (items.length === 1 && items[0].label === group.label) return items[0]
  return { id: group.label, label: group.label, icon: group.icon, children: items }
}))

function navigate(target: string): void {
  if (/^https?:\/\//i.test(target)) {
    window.open(target, '_blank', 'noopener')
    return
  }
  void router.push(target)
}

onMounted(async () => {
  await access.load()
  await menu.load()
})
</script>

<template>
  <router-view v-if="route.meta.bare === true" />
  <template v-else>
    <NgbSiteShell
      module-title="NgbApplication"
      product-title="NGB"
      :user-name="auth.userName"
      :pinned="[]"
      :recent="[]"
      :nodes="nodes"
      :selected-id="route.path"
      @navigate="navigate($event)"
      @open-palette="palette.open()"
      @sign-out="auth.logout()"
    >
      <router-view />
    </NgbSiteShell>
    <NgbCommandPaletteDialog />
  </template>
</template>
