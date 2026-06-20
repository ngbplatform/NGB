<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError } from '../api/http'
import type { RegisterColumn, RegisterDataRow } from '../components/register/registerTypes'
import NgbRecycleBinFilter from '../metadata/NgbRecycleBinFilter.vue'
import NgbRegisterPageLayout from '../metadata/NgbRegisterPageLayout.vue'
import type { QueryTrashMode } from '../router/queryParams'
import { toErrorMessage } from '../utils/errorMessage'
import NgbAccessDeniedState from './NgbAccessDeniedState.vue'
import { getUsers } from './api'
import { useAccessStore } from './useAccessStore'
import type { UserListItemDto } from './types'

const router = useRouter()
const access = useAccessStore()

const loading = ref(false)
const error = ref<string | null>(null)
const accessDenied = ref(false)
const users = ref<UserListItemDto[]>([])
const status = ref<QueryTrashMode>('active')

const columns: RegisterColumn[] = [
  {
    key: 'user',
    title: 'User',
    width: 320,
    minWidth: 240,
    pinned: 'left',
    format: (value) => Array.isArray(value) ? value.join('\n') : String(value ?? ''),
  },
  { key: 'roles', title: 'Roles', width: 280, minWidth: 200, wrap: true },
  { key: 'keycloakLabel', title: 'Keycloak', width: 120, minWidth: 100, align: 'center', sortable: false },
]

const filteredUsers = computed(() => {
  return users.value
    .filter((user) => {
      if (status.value === 'active') return user.isActive
      if (status.value === 'deleted') return !user.isActive
      return true
    })
    .sort((a, b) => (a.displayName ?? a.email ?? '').localeCompare(b.displayName ?? b.email ?? ''))
})

const rows = computed<RegisterDataRow[]>(() => filteredUsers.value.map((user) => ({
  key: user.userId,
  __status: user.isActive ? 'posted' : 'marked',
  __statusTitle: user.isActive ? 'Active' : 'Inactive',
  user: [userTitle(user), user.email || user.authSubject],
  roles: roleSummary(user),
  keycloakLabel: user.keycloakEnabled === true ? 'Yes' : 'No',
})))

function userTitle(user: UserListItemDto): string {
  return user.displayName?.trim() || user.email?.trim() || user.authSubject
}

function roleSummary(user: UserListItemDto): string {
  if (!user.roles.length) return 'No roles'
  return user.roles.map((role) => role.name).join(', ')
}

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  accessDenied.value = false

  try {
    await access.load()
    users.value = await getUsers()
  } catch (cause) {
    users.value = []
    accessDenied.value = cause instanceof ApiError && cause.status === 403
    error.value = accessDenied.value ? null : toErrorMessage(cause, 'Failed to load users')
  } finally {
    loading.value = false
  }
}

function openUser(userId: string): void {
  void router.push(`/admin/security/users/${encodeURIComponent(userId)}`)
}

function createUser(): void {
  void router.push('/admin/security/users/new')
}

function goBack(): void {
  void router.push('/home')
}

onMounted(() => {
  void load()
})
</script>

<template>
  <NgbAccessDeniedState v-if="accessDenied" />

  <NgbRegisterPageLayout
    v-else
    title="Users"
    :items-count="filteredUsers.length"
    :total="users.length"
    :loading="loading"
    :error="error"
    :show-filter="false"
    :disable-create="!access.canManageUsers"
    :disable-prev="true"
    :disable-next="true"
    :columns="columns"
    :rows="rows"
    storage-key="ngb:security:users"
    @back="goBack"
    @refresh="load"
    @create="createUser"
    @rowActivate="openUser"
  >
    <template #filters>
      <NgbRecycleBinFilter v-model="status" />
    </template>
  </NgbRegisterPageLayout>
</template>
