export type PermissionAssignmentDto = {
  resourceKind: string
  resourceCode: string
  actionCode: string
}

export type PermissionDefinitionDto = PermissionAssignmentDto & {
  displayName: string
  group: string
  description?: string | null
}

export type PermissionGroupDto = {
  group: string
  permissions: PermissionDefinitionDto[]
}

export type RoleBadgeDto = {
  roleId: string
  code: string
  name: string
  isSystem: boolean
  isActive: boolean
}

export type UserBadgeDto = {
  userId: string
  email?: string | null
  displayName?: string | null
  isActive: boolean
}

export type UserListItemDto = {
  userId: string
  authSubject: string
  email?: string | null
  displayName?: string | null
  isActive: boolean
  keycloakEnabled?: boolean | null
  roles: RoleBadgeDto[]
  createdAtUtc: string
  updatedAtUtc: string
}

export type UserDetailsDto = {
  userId: string
  authSubject: string
  email?: string | null
  firstName?: string | null
  lastName?: string | null
  displayName?: string | null
  isActive: boolean
  keycloakEnabled?: boolean | null
  roles: RoleBadgeDto[]
  accessVersion: number
  createdAtUtc: string
  updatedAtUtc: string
}

export type CreateUserRequestDto = {
  email: string
  firstName?: string | null
  lastName?: string | null
  displayName?: string | null
  enabled: boolean
  temporaryPassword?: string | null
  requirePasswordUpdate: boolean
  roleIds: string[]
}

export type UpdateUserRequestDto = {
  email?: string | null
  firstName?: string | null
  lastName?: string | null
  displayName?: string | null
  enabled: boolean
  temporaryPassword?: string | null
  requirePasswordUpdate: boolean
  roleIds: string[]
}

export type ReplaceUserRolesRequestDto = {
  roleIds: string[]
}

export type RoleListItemDto = {
  roleId: string
  code: string
  name: string
  description?: string | null
  isSystem: boolean
  isActive: boolean
  assignedUsersCount: number
  createdAtUtc: string
  updatedAtUtc: string
}

export type RoleDetailsDto = {
  roleId: string
  code: string
  name: string
  description?: string | null
  isSystem: boolean
  isActive: boolean
  permissions: PermissionAssignmentDto[]
  assignedUsers: UserBadgeDto[]
  createdAtUtc: string
  updatedAtUtc: string
}

export type CreateRoleRequestDto = {
  code: string
  name: string
  description?: string | null
  permissions: PermissionAssignmentDto[]
}

export type UpdateRoleRequestDto = {
  code: string
  name: string
  description?: string | null
  isActive: boolean
  permissions: PermissionAssignmentDto[]
}

export type ReplaceRolePermissionsRequestDto = {
  permissions: PermissionAssignmentDto[]
}

export type CurrentAccessDto = {
  userId?: string | null
  authSubject?: string | null
  isAuthenticated: boolean
  isActive: boolean
  isBootstrapAdmin: boolean
  accessVersion: number
  roles?: RoleBadgeDto[]
  permissions: PermissionAssignmentDto[]
}

export type EffectiveAccessResourceDto = {
  resourceKind: string
  resourceCode: string
  displayName: string
  actions: string[]
}

export type EffectiveAccessGroupDto = {
  group: string
  resources: EffectiveAccessResourceDto[]
}

export type EffectiveAccessDto = {
  userId: string
  accessVersion: number
  groups: EffectiveAccessGroupDto[]
}
