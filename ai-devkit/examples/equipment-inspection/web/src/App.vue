<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NgbCommandPaletteDialog, NgbSiteShell, useAccessStore, useAuthStore,
  useCommandPaletteHotkeys, useCommandPaletteStore } from '@ngbplatform/ui'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const access = useAccessStore()
const palette = useCommandPaletteStore()
useCommandPaletteHotkeys()
function canView(resourceKind: string, resourceCode: string): boolean {
  return access.isActive && (access.current?.isBootstrapAdmin === true
    || access.hasPermission({ resourceKind, resourceCode, actionCode: 'view' }))
}

const nodes = computed(() => [
  { id: '/catalogs/example.equipment', label: 'Equipment', route: '/catalogs/example.equipment',
    allowed: canView('catalog', 'example.equipment') },
  { id: '/documents/example.inspection', label: 'Inspections', route: '/documents/example.inspection',
    allowed: canView('document', 'example.inspection') },
  { id: '/documents/example.equipment-usage', label: 'Equipment usage', route: '/documents/example.equipment-usage',
    allowed: canView('document', 'example.equipment-usage') },
  { id: '/documents/example.equipment-condition', label: 'Equipment assessments', route: '/documents/example.equipment-condition',
    allowed: canView('document', 'example.equipment-condition') },
  { id: '/documents/example.equipment-service', label: 'Equipment service', route: '/documents/example.equipment-service',
    allowed: canView('document', 'example.equipment-service') },
  { id: '/reports/example.inspection-summary', label: 'Inspection summary', route: '/reports/example.inspection-summary',
    allowed: canView('report', 'example.inspection-summary') },
  { id: '/admin/accounting/period-closing', label: 'Period closing', route: '/admin/accounting/period-closing',
    allowed: canView('admin', 'period_closing') },
].filter(node => node.allowed))
</script>

<template>
  <NgbSiteShell
    module-title="Equipment inspection"
    product-title="NGB"
    :user-name="auth.userName"
    :pinned="[]"
    :recent="[]"
    :nodes="nodes"
    :selected-id="route.path"
    @navigate="router.push($event)"
    @select="(_id, target) => router.push(target)"
    @sign-out="auth.logout()"
    @open-palette="palette.open()"
  >
    <router-view />
  </NgbSiteShell>
  <NgbCommandPaletteDialog />
</template>
