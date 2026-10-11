<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NgbSiteShell, useAccessStore, useAuthStore, useMainMenuStore } from '@ngbplatform/ui'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const access = useAccessStore()
const menu = useMainMenuStore()
const nodes = computed(() => menu.groups.map((group) => ({
  id: group.label,
  label: group.label,
  children: group.items.map((item) => ({ id: item.route, label: item.label, route: item.route, icon: item.icon })),
})))

onMounted(async () => {
  await access.load(true)
  await menu.load()
})
</script>

<template>
  <NgbSiteShell
    module-title="CertificationApp"
    product-title="NGB"
    :user-name="auth.userName"
    :pinned="[]"
    :recent="[]"
    :nodes="nodes"
    :selected-id="route.path"
    @navigate="router.push($event)"
    @select="(_id, target) => router.push(target)"
    @sign-out="auth.logout()"
  >
    <router-view />
  </NgbSiteShell>
</template>
