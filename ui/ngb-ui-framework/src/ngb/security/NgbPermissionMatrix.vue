<template>
  <div class="min-w-0 space-y-4" data-testid="permission-matrix">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <NgbInput
        v-model="query"
        class="max-w-[360px]"
        placeholder="Filter permissions"
        :disabled="disabled"
      />
      <div class="text-xs text-ngb-muted tabular-nums">{{ selectedCount }} / {{ definitions.length }}</div>
    </div>

    <div v-if="groups.length === 0" class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-sm text-ngb-muted">
      No permissions match the current filter.
    </div>

    <section
      v-for="group in groups"
      :key="group.group"
      class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card shadow-card"
    >
      <div class="flex flex-wrap items-center justify-between gap-3 border-b border-ngb-border px-4 py-3">
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold text-ngb-text">{{ group.group }}</h2>
          <div class="mt-0.5 text-xs text-ngb-muted tabular-nums">{{ selectedInGroup(group.permissions) }} / {{ group.permissions.length }}</div>
        </div>
        <NgbButton
          size="sm"
          variant="ghost"
          :disabled="disabled"
          @click="toggleGroup(group.permissions)"
        >
          <NgbIcon :name="isGroupFullySelected(group.permissions) ? 'minus' : 'check'" :size="15" />
          {{ isGroupFullySelected(group.permissions) ? 'Clear' : 'Select' }}
        </NgbButton>
      </div>

      <div class="divide-y divide-ngb-border">
        <label
          v-for="permission in group.permissions"
          :key="permissionKey(permission)"
          class="grid cursor-pointer grid-cols-[1.25rem,minmax(0,1fr),auto] items-start gap-3 px-4 py-3 hover:bg-[var(--ngb-row-hover)]"
          :class="disabled ? 'cursor-not-allowed opacity-70' : ''"
        >
          <input
            type="checkbox"
            class="mt-1 h-4 w-4 rounded-none border-ngb-border"
            :checked="selectedKeys.has(permissionKey(permission))"
            :disabled="disabled"
            @change="setPermission(permission, ($event.target as HTMLInputElement).checked)"
          />
          <span class="min-w-0">
            <span class="block text-sm font-medium text-ngb-text">{{ permission.displayName }}</span>
            <span v-if="permission.description" class="mt-0.5 block text-xs leading-5 text-ngb-muted">{{ permission.description }}</span>
            <span class="mt-1 block truncate font-mono text-[11px] text-ngb-muted">{{ permissionKey(permission) }}</span>
          </span>
          <span class="mt-0.5 rounded-[var(--ngb-radius)] border border-ngb-border px-2 py-0.5 text-[11px] text-ngb-muted">
            {{ permission.actionCode }}
          </span>
        </label>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import NgbButton from '../primitives/NgbButton.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbInput from '../primitives/NgbInput.vue'
import { buildPermissionKey, groupPermissionDefinitions } from './permissions'
import type { PermissionAssignmentDto, PermissionDefinitionDto } from './types'

const props = withDefaults(defineProps<{
  modelValue: PermissionAssignmentDto[]
  definitions: PermissionDefinitionDto[]
  disabled?: boolean
}>(), {
  disabled: false,
})

const emit = defineEmits<{
  (e: 'update:modelValue', value: PermissionAssignmentDto[]): void
}>()

const query = ref('')

const selectedKeys = computed(() => new Set(props.modelValue.map((entry) => buildPermissionKey(entry))))
const selectedCount = computed(() => selectedKeys.value.size)

const filteredDefinitions = computed(() => {
  const text = query.value.trim().toLowerCase()
  const definitions = props.definitions
  if (!text) return definitions

  return definitions.filter((permission) => [
    permission.displayName,
    permission.description ?? '',
    permission.group,
    permission.resourceKind,
    permission.resourceCode,
    permission.actionCode,
    buildPermissionKey(permission),
  ].join(' ').toLowerCase().includes(text))
})

const groups = computed(() => groupPermissionDefinitions(filteredDefinitions.value))

function permissionKey(permission: PermissionAssignmentDto): string {
  return buildPermissionKey(permission)
}

function normalizeAssignments(keys: Set<string>): PermissionAssignmentDto[] {
  const byKey = new Map(props.definitions.map((definition) => [permissionKey(definition), definition]))
  return Array.from(keys)
    .sort()
    .map((key) => byKey.get(key))
    .filter((entry): entry is PermissionDefinitionDto => !!entry)
    .map((entry) => ({
      resourceKind: entry.resourceKind,
      resourceCode: entry.resourceCode,
      actionCode: entry.actionCode,
    }))
}

function setPermission(permission: PermissionAssignmentDto, checked: boolean): void {
  if (props.disabled) return

  const keys = new Set(selectedKeys.value)
  const key = permissionKey(permission)
  if (checked) keys.add(key)
  else keys.delete(key)

  emit('update:modelValue', normalizeAssignments(keys))
}

function isGroupFullySelected(permissions: PermissionDefinitionDto[]): boolean {
  return permissions.every((permission) => selectedKeys.value.has(permissionKey(permission)))
}

function selectedInGroup(permissions: PermissionDefinitionDto[]): number {
  return permissions.filter((permission) => selectedKeys.value.has(permissionKey(permission))).length
}

function toggleGroup(permissions: PermissionDefinitionDto[]): void {
  if (props.disabled) return

  const keys = new Set(selectedKeys.value)
  const selected = isGroupFullySelected(permissions)

  for (const permission of permissions) {
    const key = permissionKey(permission)
    if (selected) keys.delete(key)
    else keys.add(key)
  }

  emit('update:modelValue', normalizeAssignments(keys))
}
</script>
