<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '../api/http'
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbValidationSummary from '../components/forms/NgbValidationSummary.vue'
import NgbBadge from '../primitives/NgbBadge.vue'
import NgbButton from '../primitives/NgbButton.vue'
import NgbConfirmDialog from '../components/NgbConfirmDialog.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbStatusIcon from '../primitives/NgbStatusIcon.vue'
import NgbSwitch from '../primitives/NgbSwitch.vue'
import { useOptionalToasts } from '../primitives/toast'
import NgbEntityAuditSidebar from '../editor/NgbEntityAuditSidebar.vue'
import NgbPageHeader from '../layout/NgbPageHeader.vue'
import { toErrorMessage } from '../utils/errorMessage'
import NgbAccessDeniedState from './NgbAccessDeniedState.vue'
import NgbEffectiveAccessPanel from './NgbEffectiveAccessPanel.vue'
import { USER_AUDIT_BEHAVIOR } from './audit'
import {
  createUser,
  deactivateUser,
  getRoles,
  getUser,
  getUserEffectiveAccess,
  reactivateUser,
  updateUser,
} from './api'
import { useAccessStore } from './useAccessStore'
import type { EffectiveAccessDto, RoleListItemDto, UserDetailsDto } from './types'

type UserForm = {
  email: string
  displayName: string
  password: string
  confirmPassword: string
  requirePasswordUpdate: boolean
}

let loadSequence = 0
let effectiveSequence = 0
let loadController: AbortController | null = null
let effectiveController: AbortController | null = null

type UserFieldErrors = Partial<Record<keyof Omit<UserForm, 'requirePasswordUpdate'>, string>>

const AUDIT_ENTITY_KIND_SECURITY_USER = 8

const route = useRoute()
const router = useRouter()
const access = useAccessStore()

const loading = ref(false)
const saving = ref(false)
const activating = ref(false)
const effectiveLoading = ref(false)
const error = ref<string | null>(null)
const effectiveError = ref<string | null>(null)
const serverValidationMessages = ref<string[]>([])
const accessDenied = ref(false)
const user = ref<UserDetailsDto | null>(null)
const roles = ref<RoleListItemDto[]>([])
const selectedRoleIds = ref<string[]>([])
const effectiveAccess = ref<EffectiveAccessDto | null>(null)
const confirmMode = ref<'deactivate' | 'reactivate' | null>(null)
const auditOpen = ref(false)
const attemptedSave = ref(false)
const showPassword = ref(false)
const showConfirmPassword = ref(false)
const changePasswordMode = ref(false)
const toasts = useOptionalToasts()

const form = ref<UserForm>({
  email: '',
  displayName: '',
  password: '',
  confirmPassword: '',
  requirePasswordUpdate: true,
})

const userId = computed(() => String(route.params.userId ?? 'new'))
const isNew = computed(() => userId.value === 'new')
const canEdit = computed(() => access.canManageUsers)
const canOpenAudit = computed(() => !isNew.value && !!user.value)
const shouldShowPasswordFields = computed(() => isNew.value || changePasswordMode.value)
const title = computed(() => {
  if (isNew.value) return 'New user'
  return user.value?.displayName?.trim() || user.value?.email?.trim() || 'User'
})
const activeRoles = computed(() => roles.value.filter((role) => role.isActive || selectedRoleIds.value.includes(role.roleId)))
const auditEntityTitle = computed(() => title.value)
const fieldErrors = computed<UserFieldErrors>(() => {
  if (!attemptedSave.value) return {}

  const errors: UserFieldErrors = {}
  const values = form.value
  const passwordRequired = shouldShowPasswordFields.value

  if (!values.email.trim()) errors.email = 'Email is required.'
  else if (!isEmail(values.email)) errors.email = 'Enter a valid email address.'

  if (!values.displayName.trim()) errors.displayName = 'Display name is required.'

  if (passwordRequired && values.password.length === 0) errors.password = 'Password is required.'
  if (passwordRequired && values.confirmPassword.length === 0) errors.confirmPassword = 'Confirm password is required.'

  if (passwordRequired && values.password.length > 0 && values.confirmPassword.length > 0 && values.password !== values.confirmPassword) {
    errors.confirmPassword = 'Passwords do not match.'
  }

  return errors
})
const validationMessages = computed(() => uniqueMessages([
  ...Object.values(fieldErrors.value).filter((message): message is string => !!message),
  ...serverValidationMessages.value,
]))

function resetForm(): void {
  user.value = null
  selectedRoleIds.value = []
  effectiveAccess.value = null
  auditOpen.value = false
  attemptedSave.value = false
  serverValidationMessages.value = []
  showPassword.value = false
  showConfirmPassword.value = false
  changePasswordMode.value = false
  form.value = {
    email: '',
    displayName: '',
    password: '',
    confirmPassword: '',
    requirePasswordUpdate: true,
  }
}

function applyUser(next: UserDetailsDto): void {
  user.value = next
  selectedRoleIds.value = next.roles.map((role) => role.roleId)
  form.value = {
    email: next.email ?? '',
    displayName: next.displayName ?? '',
    password: '',
    confirmPassword: '',
    requirePasswordUpdate: true,
  }
  changePasswordMode.value = false
  showPassword.value = false
  showConfirmPassword.value = false
}

async function loadEffectiveAccess(): Promise<void> {
  if (isNew.value || !user.value) return

  const sequence = ++effectiveSequence
  effectiveController?.abort()
  const controller = new AbortController()
  effectiveController = controller
  const targetUserId = user.value.userId

  effectiveLoading.value = true
  effectiveError.value = null

  try {
    const nextAccess = await getUserEffectiveAccess(targetUserId, { signal: controller.signal })
    if (sequence !== effectiveSequence || user.value?.userId !== targetUserId) return
    effectiveAccess.value = nextAccess
  } catch (cause) {
    if (sequence !== effectiveSequence) return
    effectiveAccess.value = null
    effectiveError.value = toErrorMessage(cause, 'Failed to load effective access')
  } finally {
    if (effectiveController === controller) {
      effectiveLoading.value = false
      effectiveController = null
    }
  }
}

async function load(): Promise<void> {
  const sequence = ++loadSequence
  effectiveSequence += 1
  loadController?.abort()
  effectiveController?.abort()
  const controller = new AbortController()
  loadController = controller
  const targetUserId = userId.value
  const creating = isNew.value
  loading.value = true
  error.value = null
  accessDenied.value = false
  resetForm()

  try {
    await access.load()
    if (sequence !== loadSequence) return
    if (creating && !access.canManageUsers) {
      accessDenied.value = true
      return
    }

    if (creating) {
      roles.value = await getRoles({ signal: controller.signal })
    } else {
      effectiveLoading.value = true
      const [nextRoles, nextUser, effectiveResult] = await Promise.all([
        getRoles({ signal: controller.signal }),
        getUser(targetUserId, { signal: controller.signal }),
        Promise.resolve(getUserEffectiveAccess(targetUserId, { signal: controller.signal }))
          .then((value) => ({ value, error: null as unknown }))
          .catch((cause: unknown) => ({ value: null, error: cause })),
      ])
      if (sequence !== loadSequence) return
      roles.value = nextRoles
      applyUser(nextUser)
      effectiveAccess.value = effectiveResult.value
      effectiveError.value = effectiveResult.error
        ? toErrorMessage(effectiveResult.error, 'Failed to load effective access')
        : null
      effectiveLoading.value = false
    }
  } catch (cause) {
    if (sequence !== loadSequence) return
    accessDenied.value = (cause instanceof ApiError || (typeof cause === 'object' && cause !== null))
      && Number((cause as { status?: unknown }).status) === 403
    error.value = accessDenied.value ? null : toErrorMessage(cause, 'Failed to load user')
  } finally {
    if (sequence === loadSequence) loading.value = false
    if (loadController === controller) loadController = null
  }
}

function isRoleSelected(roleId: string): boolean {
  return selectedRoleIds.value.includes(roleId)
}

function setRoleSelected(roleId: string, selected: boolean): void {
  const next = new Set(selectedRoleIds.value)
  if (selected) next.add(roleId)
  else next.delete(roleId)
  selectedRoleIds.value = Array.from(next)
}

function isEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim())
}

function uniqueMessages(messages: string[]): string[] {
  const seen = new Set<string>()
  const result: string[] = []
  for (const message of messages) {
    const text = message.trim()
    if (seen.has(text)) continue
    seen.add(text)
    result.push(text)
  }
  return result
}

function mapApiIssueMessage(path: string, message: string): string {
  const normalizedPath = path.toLowerCase()
  const normalizedMessage = message.trim()
  if (normalizedPath.includes('email') && /valid email|email/i.test(normalizedMessage)) return 'Enter a valid email address.'
  if (normalizedPath.includes('password') && /policy|invalid|weak|length/i.test(normalizedMessage)) return 'Password does not meet the password policy.'
  return normalizedMessage
}

function mapKeycloakError(cause: ApiError): string | null {
  if (cause.errorCode !== 'ngb.keycloak.admin_request_failed') return null

  const body = typeof cause.context?.keycloakErrorBody === 'string' ? cause.context.keycloakErrorBody : ''
  const text = body.toLowerCase()
  const statusCode = typeof cause.context?.statusCode === 'number' ? cause.context.statusCode : cause.status

  if (statusCode === 409 || text.includes('already exists') || text.includes('duplicate')) {
    return 'A user with this email already exists.'
  }

  if ((text.includes('email') && text.includes('invalid')) || text.includes('invalid email')) {
    return 'Enter a valid email address.'
  }

  if (text.includes('password')) {
    return 'Password does not meet the password policy.'
  }

  return 'The identity provider rejected the user data. Check the email and password, then try again.'
}

function mapUserSaveError(cause: unknown): string[] {
  if (!(cause instanceof ApiError)) return []

  const keycloak = mapKeycloakError(cause)
  if (keycloak) return [keycloak]

  const issueMessages = (cause.issues ?? [])
    .map((issue) => mapApiIssueMessage(issue.path, issue.message))
    .filter((message) => message.length > 0)

  const errorMessages = Object.entries(cause.errors ?? {})
    .flatMap(([path, messages]) => messages.map((message) => mapApiIssueMessage(path, message)))
    .filter((message) => message.length > 0)

  return uniqueMessages([...issueMessages, ...errorMessages])
}

function validateBeforeSave(): boolean {
  attemptedSave.value = true
  serverValidationMessages.value = []
  return Object.keys(fieldErrors.value).length === 0
}

function startChangePassword(): void {
  changePasswordMode.value = true
  form.value.password = ''
  form.value.confirmPassword = ''
  serverValidationMessages.value = []
  attemptedSave.value = false
  showPassword.value = false
  showConfirmPassword.value = false
}

function cancelChangePassword(): void {
  changePasswordMode.value = false
  form.value.password = ''
  form.value.confirmPassword = ''
  serverValidationMessages.value = []
  attemptedSave.value = false
  showPassword.value = false
  showConfirmPassword.value = false
}

async function save(): Promise<void> {
  if (saving.value) return

  if (!validateBeforeSave()) {
    error.value = null
    return
  }

  saving.value = true
  error.value = null

  try {
    if (isNew.value) {
      const created = await createUser({
        email: form.value.email.trim(),
        firstName: null,
        lastName: null,
        displayName: form.value.displayName.trim(),
        enabled: true,
        temporaryPassword: form.value.password,
        requirePasswordUpdate: form.value.requirePasswordUpdate,
        roleIds: selectedRoleIds.value,
      })
      applyUser(created)
      toasts?.push({ title: 'User created', message: 'User was saved.', tone: 'success' })
      await router.replace(`/admin/security/users/${encodeURIComponent(created.userId)}`)
      await loadEffectiveAccess()
      return
    }

    const password = changePasswordMode.value ? form.value.password : null
    const updated = await updateUser(userId.value, {
      email: form.value.email.trim(),
      firstName: null,
      lastName: null,
      displayName: form.value.displayName.trim(),
      enabled: user.value!.keycloakEnabled ?? user.value!.isActive,
      temporaryPassword: password,
      requirePasswordUpdate: false,
      roleIds: selectedRoleIds.value,
    })
    applyUser(updated)
    toasts?.push({ title: 'User saved', message: 'Changes were saved.', tone: 'success' })
    await loadEffectiveAccess()
  } catch (cause) {
    const messages = mapUserSaveError(cause)
    if (messages.length > 0) serverValidationMessages.value = messages
    else error.value = toErrorMessage(cause, 'Failed to save user')
  } finally {
    saving.value = false
  }
}

async function confirmActivationChange(): Promise<void> {
  activating.value = true
  error.value = null

  try {
    if (confirmMode.value === 'deactivate') await deactivateUser(user.value!.userId)
    else await reactivateUser(user.value!.userId)
    confirmMode.value = null
    await load()
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to update user status')
  } finally {
    activating.value = false
  }
}

function goBack(): void {
  void router.push('/admin/security/users')
}

function openRole(roleId: string): void {
  void router.push(`/admin/security/roles/${encodeURIComponent(roleId)}`)
}

function openAuditLog(): void {
  auditOpen.value = true
}

function closeAuditLog(): void {
  auditOpen.value = false
}

watch(
  () => route.params.userId,
  () => {
    void load()
  },
  { immediate: true },
)

onBeforeUnmount(() => {
  loadSequence += 1
  effectiveSequence += 1
  loadController?.abort()
  effectiveController?.abort()
  loadController = null
  effectiveController = null
})
</script>

<template>
  <NgbAccessDeniedState v-if="accessDenied" />

  <div v-else class="flex h-full min-h-0 flex-col">
    <NgbPageHeader :title="title" :can-back="true" :breadcrumbs="['Users']" @back="goBack">
      <template #secondary>
        <div class="flex min-w-0 items-center gap-2">
          <NgbBadge v-if="user" :tone="user.isActive ? 'success' : 'danger'">{{ user.isActive ? 'Active' : 'Inactive' }}</NgbBadge>
        </div>
      </template>
      <template #actions>
        <button v-if="canOpenAudit" type="button" class="ngb-iconbtn" title="Audit log" :disabled="loading || saving" @click="openAuditLog">
          <NgbIcon name="history" />
        </button>
        <button
          v-if="user && canEdit && user.isActive"
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
          v-if="user && canEdit && !user.isActive"
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
          :disabled="!canEdit || loading || saving"
          @click="save"
        >
          <NgbIcon name="save" />
        </button>
      </template>
    </NgbPageHeader>

    <main class="flex-1 min-h-0 overflow-auto p-6 pb-8">
      <div v-if="loading" class="text-sm text-ngb-muted">Loading...</div>

      <div v-else class="grid min-h-[calc(100vh-270px)] items-stretch gap-6 xl:grid-cols-[minmax(0,1fr),420px]">
        <div class="flex min-w-0 flex-col gap-6">
          <section class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-4 shadow-card">
            <NgbValidationSummary v-if="validationMessages.length > 0" class="mb-4" :messages="validationMessages" />

            <div class="grid gap-4 md:grid-cols-2">
              <div>
                <NgbInput v-model="form.email" label="Email" type="email" :disabled="!canEdit" />
                <div v-if="fieldErrors.email" class="mt-1 text-xs text-ngb-danger">{{ fieldErrors.email }}</div>
              </div>
              <div>
                <NgbInput v-model="form.displayName" label="Display name" :disabled="!canEdit" />
                <div v-if="fieldErrors.displayName" class="mt-1 text-xs text-ngb-danger">{{ fieldErrors.displayName }}</div>
              </div>
            </div>

            <div v-if="shouldShowPasswordFields" class="mt-4 grid gap-4 md:grid-cols-2">
              <div>
                <label class="mb-1 block text-xs font-semibold text-ngb-muted">Password</label>
                <div class="relative">
                  <input
                    :type="showPassword ? 'text' : 'password'"
                    :value="form.password"
                    :disabled="!canEdit"
                    autocomplete="new-password"
                    class="h-9 w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 pr-10 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus disabled:cursor-not-allowed disabled:opacity-60"
                    @input="form.password = ($event.target as HTMLInputElement).value"
                  />
                  <button
                    type="button"
                    class="ngb-iconbtn absolute right-1 top-1/2 h-7 w-7 -translate-y-1/2"
                    :disabled="!canEdit"
                    :title="showPassword ? 'Hide password' : 'Show password'"
                    @click="showPassword = !showPassword"
                  >
                    <NgbIcon :name="showPassword ? 'eye-off' : 'eye'" :size="16" />
                  </button>
                </div>
                <div v-if="fieldErrors.password" class="mt-1 text-xs text-ngb-danger">{{ fieldErrors.password }}</div>
              </div>

              <div>
                <label class="mb-1 block text-xs font-semibold text-ngb-muted">Confirm password</label>
                <div class="relative">
                  <input
                    :type="showConfirmPassword ? 'text' : 'password'"
                    :value="form.confirmPassword"
                    :disabled="!canEdit"
                    autocomplete="new-password"
                    class="h-9 w-full rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card px-3 pr-10 text-sm text-ngb-text placeholder:text-ngb-muted/70 ngb-focus disabled:cursor-not-allowed disabled:opacity-60"
                    @input="form.confirmPassword = ($event.target as HTMLInputElement).value"
                  />
                  <button
                    type="button"
                    class="ngb-iconbtn absolute right-1 top-1/2 h-7 w-7 -translate-y-1/2"
                    :disabled="!canEdit"
                    :title="showConfirmPassword ? 'Hide password' : 'Show password'"
                    @click="showConfirmPassword = !showConfirmPassword"
                  >
                    <NgbIcon :name="showConfirmPassword ? 'eye-off' : 'eye'" :size="16" />
                  </button>
                </div>
                <div v-if="fieldErrors.confirmPassword" class="mt-1 text-xs text-ngb-danger">{{ fieldErrors.confirmPassword }}</div>
              </div>
            </div>

            <div v-if="isNew" class="mt-4">
              <NgbSwitch v-model="form.requirePasswordUpdate" label="Require password update" :disabled="!canEdit" />
            </div>

            <div v-else class="mt-4 flex flex-wrap items-center gap-2">
              <NgbButton v-if="!changePasswordMode" size="sm" variant="secondary" :disabled="!canEdit" @click="startChangePassword">
                <NgbIcon name="shield" :size="15" />
                Change password
              </NgbButton>
              <NgbButton v-else size="sm" variant="ghost" :disabled="!canEdit" @click="cancelChangePassword">
                Cancel
              </NgbButton>
            </div>

            <div
              v-if="error"
              class="mt-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100"
            >
              {{ error }}
            </div>
          </section>

          <NgbEffectiveAccessPanel
            v-if="!isNew"
            class="min-h-[560px] flex-1"
            :access="effectiveAccess"
            :loading="effectiveLoading"
            :error="effectiveError"
            :show-refresh="false"
            @refresh="loadEffectiveAccess"
          />
        </div>

        <section class="flex min-h-full flex-col rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card">
          <div class="border-b border-ngb-border px-4 py-3">
            <h2 class="text-sm font-semibold text-ngb-text">Roles</h2>
          </div>
          <div class="min-h-0 flex-1 overflow-auto divide-y divide-ngb-border">
            <div
              v-for="role in activeRoles"
              :key="role.roleId"
              class="grid grid-cols-[minmax(0,1fr),2rem] items-start gap-2 px-4 py-3 hover:bg-[var(--ngb-row-hover)]"
              :class="!canEdit ? 'opacity-70' : ''"
            >
              <label class="grid min-w-0 cursor-pointer grid-cols-[1.25rem,minmax(0,1fr)] gap-3" :class="!canEdit ? 'cursor-not-allowed' : ''">
                <input
                  type="checkbox"
                  class="mt-1 h-4 w-4"
                  :checked="isRoleSelected(role.roleId)"
                  :disabled="!canEdit"
                  @change="setRoleSelected(role.roleId, ($event.target as HTMLInputElement).checked)"
                />
                <span class="min-w-0">
                  <span class="flex min-w-0 items-center gap-2">
                    <span class="truncate text-sm font-medium text-ngb-text">{{ role.name }}</span>
                    <NgbBadge v-if="!role.isActive" tone="danger">Inactive</NgbBadge>
                    <NgbBadge v-if="role.isSystem" tone="neutral">System</NgbBadge>
                  </span>
                  <span class="mt-0.5 block truncate font-mono text-xs text-ngb-muted">{{ role.code }}</span>
                </span>
              </label>
              <button
                type="button"
                class="ngb-iconbtn"
                :title="`Open ${role.name}`"
                :aria-label="`Open role ${role.name}`"
                @click.stop="openRole(role.roleId)"
              >
                <NgbIcon name="open-in-new" :size="16" />
              </button>
            </div>
            <div v-if="activeRoles.length === 0" class="px-4 py-6 text-sm text-ngb-muted">No roles available.</div>
          </div>
        </section>
      </div>
    </main>

    <NgbConfirmDialog
      :open="confirmMode !== null"
      :title="confirmMode === 'reactivate' ? 'Reactivate user?' : 'Deactivate user?'"
      :message="confirmMode === 'reactivate' ? 'This enables the NGB application user and attempts to enable the linked Keycloak user.' : 'This disables access without deleting history, audit records, or ownership links.'"
      :confirm-text="confirmMode === 'reactivate' ? 'Reactivate' : 'Deactivate'"
      :danger="confirmMode === 'deactivate'"
      :confirm-loading="activating"
      @update:open="confirmMode = null"
      @confirm="confirmActivationChange"
    />

    <NgbDrawer v-model:open="auditOpen" title="Audit Log" hide-header flush-body>
      <NgbEntityAuditSidebar
        :open="auditOpen"
        :entity-kind="AUDIT_ENTITY_KIND_SECURITY_USER"
        :entity-id="user!.userId"
        :entity-title="auditEntityTitle"
        :behavior="USER_AUDIT_BEHAVIOR"
        @back="closeAuditLog"
        @close="closeAuditLog"
      />
    </NgbDrawer>
  </div>
</template>
