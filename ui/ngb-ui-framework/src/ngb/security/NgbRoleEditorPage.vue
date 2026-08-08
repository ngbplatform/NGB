<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '../api/http'
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbBadge from '../primitives/NgbBadge.vue'
import NgbConfirmDialog from '../components/NgbConfirmDialog.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbStatusIcon from '../primitives/NgbStatusIcon.vue'
import NgbTabs from '../primitives/NgbTabs.vue'
import { useOptionalToasts } from '../primitives/toast'
import NgbEntityAuditSidebar from '../editor/NgbEntityAuditSidebar.vue'
import NgbPageHeader from '../layout/NgbPageHeader.vue'
import { toErrorMessage } from '../utils/errorMessage'
import NgbAccessDeniedState from './NgbAccessDeniedState.vue'
import NgbPermissionMatrix from './NgbPermissionMatrix.vue'
import { ROLE_AUDIT_BEHAVIOR } from './audit'
import {
  createRole,
  deactivateRole,
  getPermissionDefinitions,
  getRole,
  reactivateRole,
  updateRole,
} from './api'
import { useAccessStore } from './useAccessStore'
import type { PermissionAssignmentDto, PermissionDefinitionDto, RoleDetailsDto } from './types'

type RoleForm = {
  code: string
  name: string
  description: string
}

const AUDIT_ENTITY_KIND_SECURITY_ROLE = 9

const route = useRoute()
const router = useRouter()
const access = useAccessStore()

const loading = ref(false)
const saving = ref(false)
const activating = ref(false)
const error = ref<string | null>(null)
const accessDenied = ref(false)
const role = ref<RoleDetailsDto | null>(null)
const definitions = ref<PermissionDefinitionDto[]>([])
const permissions = ref<PermissionAssignmentDto[]>([])
const confirmMode = ref<'deactivate' | 'reactivate' | null>(null)
const auditOpen = ref(false)
const activeTab = ref('permissions')
const toasts = useOptionalToasts()

const form = ref<RoleForm>({
  code: '',
  name: '',
  description: '',
})

const roleId = computed(() => String(route.params.roleId ?? 'new'))
const isNew = computed(() => roleId.value === 'new')
const canEdit = computed(() => access.canManageRoles)
const canOpenAudit = computed(() => !isNew.value && !!role.value)
const title = computed(() => isNew.value ? 'New role' : role.value?.name ?? 'Role')
const auditEntityTitle = computed(() => title.value)
const roleTabs = computed(() => [
  { key: 'permissions', label: 'Permissions' },
  ...(role.value ? [{ key: 'assigned-users', label: 'Assigned users' }] : []),
])

function resetForm(): void {
  role.value = null
  permissions.value = []
  auditOpen.value = false
  activeTab.value = 'permissions'
  form.value = {
    code: '',
    name: '',
    description: '',
  }
}

function applyRole(next: RoleDetailsDto): void {
  role.value = next
  form.value = {
    code: next.code,
    name: next.name,
    description: next.description ?? '',
  }
  permissions.value = [...next.permissions]
}

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  accessDenied.value = false
  resetForm()

  try {
    await access.load()
    if (isNew.value && !access.canManageRoles) {
      accessDenied.value = true
      return
    }

    definitions.value = await getPermissionDefinitions()
    if (!isNew.value) {
      applyRole(await getRole(roleId.value))
    }
  } catch (cause) {
    accessDenied.value = cause instanceof ApiError && cause.status === 403
    error.value = accessDenied.value ? null : toErrorMessage(cause, 'Failed to load role')
  } finally {
    loading.value = false
  }
}

async function save(): Promise<void> {
  if (!canEdit.value || saving.value) return

  saving.value = true
  error.value = null

  try {
    if (isNew.value) {
      const created = await createRole({
        code: form.value.code,
        name: form.value.name,
        description: form.value.description || null,
        permissions: permissions.value,
      })
      applyRole(created)
      toasts?.push({ title: 'Role created', message: 'Role was saved.', tone: 'success' })
      await router.replace(`/admin/security/roles/${encodeURIComponent(created.roleId)}`)
      return
    }

    const updated = await updateRole(roleId.value, {
      code: form.value.code,
      name: form.value.name,
      description: form.value.description || null,
      isActive: role.value?.isActive ?? true,
      permissions: permissions.value,
    })
    applyRole(updated)
    toasts?.push({ title: 'Role saved', message: 'Changes were saved.', tone: 'success' })
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to save role')
  } finally {
    saving.value = false
  }
}

async function confirmActivationChange(): Promise<void> {
  if (!role.value || !confirmMode.value) return

  activating.value = true
  error.value = null

  try {
    if (confirmMode.value === 'deactivate') await deactivateRole(role.value.roleId)
    else await reactivateRole(role.value.roleId)
    confirmMode.value = null
    await load()
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to update role status')
  } finally {
    activating.value = false
  }
}

function goBack(): void {
  void router.push('/admin/security/roles')
}

function openAuditLog(): void {
  if (!canOpenAudit.value) return
  auditOpen.value = true
}

function closeAuditLog(): void {
  auditOpen.value = false
}

watch(
  () => route.params.roleId,
  () => {
    void load()
  },
  { immediate: true },
)
</script>

<template>
  <NgbAccessDeniedState v-if="accessDenied" />

  <div v-else class="flex h-full min-h-0 flex-col">
    <NgbPageHeader :title="title" :can-back="true" :breadcrumbs="['Roles']" @back="goBack">
      <template #secondary>
        <div class="flex min-w-0 items-center gap-2">
          <NgbBadge v-if="role" :tone="role.isActive ? 'success' : 'danger'">{{ role.isActive ? 'Active' : 'Inactive' }}</NgbBadge>
          <NgbBadge v-if="role?.isSystem" tone="neutral">System</NgbBadge>
        </div>
      </template>
      <template #actions>
        <button v-if="canOpenAudit" type="button" class="ngb-iconbtn" title="Audit log" :disabled="loading || saving" @click="openAuditLog">
          <NgbIcon name="history" />
        </button>
        <button
          v-if="role && canEdit && role.isActive"
          type="button"
          class="ngb-iconbtn"
          title="Deactivate"
          aria-label="Deactivate"
          :disabled="saving || activating"
          @click="confirmMode = 'deactivate'"
        >
          <NgbStatusIcon status="marked" title="Deactivate" />
        </button>
        <button
          v-if="role && canEdit && !role.isActive"
          type="button"
          class="ngb-iconbtn"
          title="Reactivate"
          aria-label="Reactivate"
          :disabled="saving || activating"
          @click="confirmMode = 'reactivate'"
        >
          <NgbStatusIcon status="posted" title="Reactivate" />
        </button>
        <button
          type="button"
          class="ngb-iconbtn"
          title="Save"
          aria-label="Save"
          :disabled="!canEdit || loading || saving || !form.code.trim() || !form.name.trim()"
          @click="save"
        >
          <NgbIcon name="save" />
        </button>
      </template>
    </NgbPageHeader>

    <main class="flex-1 min-h-0 overflow-auto p-6">
      <div v-if="loading" class="text-sm text-ngb-muted">Loading...</div>

      <div v-else class="space-y-6">
        <section class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
          <div class="grid gap-4 md:grid-cols-2">
            <NgbInput v-model="form.code" label="Code" :disabled="!canEdit || role?.isSystem === true" />
            <NgbInput v-model="form.name" label="Name" :disabled="!canEdit" />
            <div class="md:col-span-2">
              <NgbInput v-model="form.description" label="Description" :disabled="!canEdit" />
            </div>
          </div>

          <div
            v-if="error"
            class="mt-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100"
          >
            {{ error }}
          </div>
        </section>

        <NgbTabs v-model="activeTab" :tabs="roleTabs" full-width-bar>
          <template #default="{ active }">
            <NgbPermissionMatrix
              v-if="active === 'permissions'"
              v-model="permissions"
              :definitions="definitions"
              :disabled="!canEdit"
            />

            <section v-else-if="role" class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card">
              <div class="divide-y divide-ngb-border">
                <div
                  v-for="assignedUser in role.assignedUsers"
                  :key="assignedUser.userId"
                  class="flex items-center justify-between gap-3 px-4 py-3"
                >
                  <div class="min-w-0">
                    <div class="truncate text-sm font-medium text-ngb-text">{{ assignedUser.displayName || assignedUser.email || assignedUser.userId }}</div>
                    <div v-if="assignedUser.email" class="mt-0.5 truncate text-xs text-ngb-muted">{{ assignedUser.email }}</div>
                  </div>
                  <NgbBadge :tone="assignedUser.isActive ? 'success' : 'danger'">{{ assignedUser.isActive ? 'Active' : 'Inactive' }}</NgbBadge>
                </div>
                <div v-if="role.assignedUsers.length === 0" class="px-4 py-6 text-sm text-ngb-muted">No assigned users.</div>
              </div>
            </section>
          </template>
        </NgbTabs>
      </div>
    </main>

    <NgbConfirmDialog
      :open="confirmMode !== null"
      :title="confirmMode === 'reactivate' ? 'Reactivate role?' : 'Deactivate role?'"
      :message="confirmMode === 'reactivate' ? 'Users assigned to this role will receive its permissions again after their access version is refreshed.' : 'Assigned users keep their history, but this role stops contributing permissions.'"
      :confirm-text="confirmMode === 'reactivate' ? 'Reactivate' : 'Deactivate'"
      :danger="confirmMode === 'deactivate'"
      :confirm-loading="activating"
      @update:open="(value) => { if (!value) confirmMode = null }"
      @confirm="confirmActivationChange"
    />

    <NgbDrawer v-model:open="auditOpen" title="Audit Log" hide-header flush-body>
      <NgbEntityAuditSidebar
        :open="auditOpen"
        :entity-kind="AUDIT_ENTITY_KIND_SECURITY_ROLE"
        :entity-id="role?.roleId ?? null"
        :entity-title="auditEntityTitle"
        :behavior="ROLE_AUDIT_BEHAVIOR"
        @back="closeAuditLog"
        @close="closeAuditLog"
      />
    </NgbDrawer>
  </div>
</template>
