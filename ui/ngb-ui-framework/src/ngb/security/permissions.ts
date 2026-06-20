import type { PermissionAssignmentDto, PermissionDefinitionDto, PermissionGroupDto } from './types'

export type PermissionKeyLike = PermissionAssignmentDto | string

function normalizeSegment(value: string | null | undefined): string {
  return String(value ?? '').trim().toLowerCase()
}

export function buildPermissionKey(permission: PermissionKeyLike): string {
  if (typeof permission === 'string') return permission.trim().toLowerCase()
  return [
    normalizeSegment(permission.resourceKind),
    normalizeSegment(permission.resourceCode),
    normalizeSegment(permission.actionCode),
  ].join('.')
}

export function buildPermissionAssignment(resourceKind: string, resourceCode: string, actionCode: string): PermissionAssignmentDto {
  return {
    resourceKind: normalizeSegment(resourceKind),
    resourceCode: normalizeSegment(resourceCode),
    actionCode: normalizeSegment(actionCode),
  }
}

export const SYSTEM_PERMISSIONS = {
  usersView: buildPermissionAssignment('system', 'users', 'view'),
  usersManage: buildPermissionAssignment('system', 'users', 'manage'),
  rolesView: buildPermissionAssignment('system', 'roles', 'view'),
  rolesManage: buildPermissionAssignment('system', 'roles', 'manage'),
  permissionsView: buildPermissionAssignment('system', 'permissions', 'view'),
  auditView: buildPermissionAssignment('system', 'audit', 'view'),
} as const

export function hasPermission(permissions: readonly PermissionAssignmentDto[] | null | undefined, permission: PermissionKeyLike): boolean {
  const key = buildPermissionKey(permission)
  if (!key || !permissions?.length) return false
  return permissions.some((entry) => buildPermissionKey(entry) === key)
}

export function toPermissionKeySet(permissions: readonly PermissionAssignmentDto[] | null | undefined): Set<string> {
  return new Set((permissions ?? []).map((entry) => buildPermissionKey(entry)).filter(Boolean))
}

export function groupPermissionDefinitions(definitions: readonly PermissionDefinitionDto[]): PermissionGroupDto[] {
  const groups = new Map<string, PermissionDefinitionDto[]>()

  for (const definition of definitions) {
    const group = definition.group?.trim() || 'Other'
    const items = groups.get(group) ?? []
    items.push(definition)
    groups.set(group, items)
  }

  return Array.from(groups.entries())
    .map(([group, permissions]) => ({
      group,
      permissions: permissions
        .slice()
        .sort((a, b) => a.resourceKind.localeCompare(b.resourceKind)
          || a.resourceCode.localeCompare(b.resourceCode)
          || a.actionCode.localeCompare(b.actionCode)),
    }))
    .sort((a, b) => a.group.localeCompare(b.group))
}
