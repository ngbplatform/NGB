<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError } from '../api/http'
import type { RegisterColumn, RegisterDataRow } from '../components/register/registerTypes'
import NgbRecycleBinFilter from '../metadata/NgbRecycleBinFilter.vue'
import NgbRegisterPageLayout from '../metadata/NgbRegisterPageLayout.vue'
import type { QueryTrashMode } from '../router/queryParams'
import { toErrorMessage } from '../utils/errorMessage'
import NgbAccessDeniedState from './NgbAccessDeniedState.vue'
import { getRoles } from './api'
import { useAccessStore } from './useAccessStore'
import type { RoleListItemDto } from './types'

const router = useRouter()
const access = useAccessStore()

const loading = ref(false)
const error = ref<string | null>(null)
const accessDenied = ref(false)
const roles = ref<RoleListItemDto[]>([])
const status = ref<QueryTrashMode>('active')
let loadSequence = 0
let loadController: AbortController | null = null

const columns: RegisterColumn[] = [
  {
    key: 'role',
    title: 'Role',
    width: 320,
    minWidth: 240,
    pinned: 'left',
    format: (value) => Array.isArray(value) ? value.join('\n') : String(value ?? ''),
  },
  { key: 'description', title: 'Description', width: 360, minWidth: 220, wrap: true },
  {
    key: 'assignedUsersCount',
    title: 'Users',
    width: 110,
    minWidth: 90,
    align: 'right',
    format: (value) => String(Number.isFinite(Number(value)) ? Math.trunc(Number(value)) : 0),
  },
  { key: 'systemLabel', title: 'System', width: 110, minWidth: 90, align: 'center', sortable: false },
]

const filteredRoles = computed(() => {
  return roles.value
    .filter((role) => {
      if (status.value === 'active') return role.isActive
      if (status.value === 'deleted') return !role.isActive
      return true
    })
    .sort((a, b) => a.name.localeCompare(b.name))
})

const rows = computed<RegisterDataRow[]>(() => filteredRoles.value.map((role) => ({
  key: role.roleId,
  __status: role.isActive ? 'posted' : 'marked',
  __statusTitle: role.isActive ? 'Active' : 'Inactive',
  role: [role.name, role.code],
  description: role.description || '-',
  assignedUsersCount: role.assignedUsersCount,
  systemLabel: role.isSystem ? 'Yes' : 'No',
})))

async function load(): Promise<void> {
  const sequence = ++loadSequence
  loadController?.abort()
  const controller = new AbortController()
  loadController = controller
  loading.value = true
  error.value = null
  accessDenied.value = false

  try {
    const [, nextRoles] = await Promise.all([
      access.load(),
      getRoles({ signal: controller.signal }),
    ])
    if (sequence !== loadSequence || controller.signal.aborted) return
    roles.value = nextRoles
  } catch (cause) {
    if (controller.signal.aborted || sequence !== loadSequence) return
    roles.value = []
    accessDenied.value = (cause instanceof ApiError || (typeof cause === 'object' && cause !== null))
      && Number((cause as { status?: unknown }).status) === 403
    error.value = accessDenied.value ? null : toErrorMessage(cause, 'Failed to load roles')
  } finally {
    if (sequence === loadSequence) loading.value = false
    if (loadController === controller) loadController = null
  }
}

function openRole(roleId: string): void {
  void router.push(`/admin/security/roles/${encodeURIComponent(roleId)}`)
}

function createRole(): void {
  void router.push('/admin/security/roles/new')
}

function goBack(): void {
  void router.push('/home')
}

onMounted(() => {
  void load()
})

onBeforeUnmount(() => {
  loadSequence += 1
  loadController?.abort()
})
</script>

<template>
  <NgbAccessDeniedState v-if="accessDenied" />

  <NgbRegisterPageLayout
    v-else
    title="Roles and permissions"
    :items-count="filteredRoles.length"
    :total="roles.length"
    :loading="loading"
    :error="error"
    :show-filter="false"
    :disable-create="!access.canManageRoles"
    :disable-prev="true"
    :disable-next="true"
    :columns="columns"
    :rows="rows"
    storage-key="ngb:security:roles"
    @back="goBack"
    @refresh="load"
    @create="createRole"
    @rowActivate="openRole"
  >
    <template #filters>
      <NgbRecycleBinFilter v-model="status" />
    </template>
  </NgbRegisterPageLayout>
</template>
