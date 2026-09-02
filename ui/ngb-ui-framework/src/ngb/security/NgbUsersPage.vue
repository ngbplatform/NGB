<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError } from '../api/http'
import type { PageResponseDto } from '../api/contracts'
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
const page = ref<PageResponseDto<UserListItemDto> | null>(null)
const status = ref<QueryTrashMode>('active')
const offset = ref(0)
const limit = 100
let loadSequence = 0
let loadController: AbortController | null = null

const users = computed(() => page.value?.items ?? [])

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

const filteredUsers = computed(() => users.value)

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
  const sequence = ++loadSequence
  loadController?.abort()
  const controller = new AbortController()
  loadController = controller
  loading.value = true
  error.value = null
  accessDenied.value = false

  try {
    const requestedOffset = offset.value
    const requestedStatus = status.value
    const [, nextPage] = await Promise.all([
      access.load(),
      getUsers({
      offset: requestedOffset,
      limit,
      isActive: requestedStatus === 'active' ? true : requestedStatus === 'deleted' ? false : null,
      }, { signal: controller.signal }),
    ])
    if (sequence !== loadSequence || controller.signal.aborted) return
    page.value = nextPage
  } catch (cause) {
    if (controller.signal.aborted || sequence !== loadSequence) return
    page.value = null
    accessDenied.value = (cause instanceof ApiError || (typeof cause === 'object' && cause !== null))
      && Number((cause as { status?: unknown }).status) === 403
    error.value = accessDenied.value ? null : toErrorMessage(cause, 'Failed to load users')
  } finally {
    if (sequence === loadSequence) loading.value = false
    if (loadController === controller) loadController = null
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

function previousPage(): void {
  offset.value = Math.max(0, offset.value - limit)
  void load()
}

function nextPage(): void {
  offset.value += limit
  void load()
}

watch(status, () => {
  offset.value = 0
  void load()
})

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
    title="Users"
    :items-count="filteredUsers.length"
    :total="page?.total ?? null"
    :loading="loading"
    :error="error"
    :show-filter="false"
    :disable-create="!access.canManageUsers"
    :disable-prev="offset === 0"
    :disable-next="users.length < limit"
    :columns="columns"
    :rows="rows"
    storage-key="ngb:security:users"
    @back="goBack"
    @refresh="load"
    @create="createUser"
    @prev="previousPage"
    @next="nextPage"
    @rowActivate="openUser"
  >
    <template #filters>
      <NgbRecycleBinFilter v-model="status" />
    </template>
  </NgbRegisterPageLayout>
</template>
