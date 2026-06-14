import { httpGet, httpPost, httpPut } from '../api/http'
import type {
  CreateRoleRequestDto,
  CreateUserRequestDto,
  CurrentAccessDto,
  EffectiveAccessDto,
  PermissionDefinitionDto,
  ReplaceRolePermissionsRequestDto,
  ReplaceUserRolesRequestDto,
  RoleDetailsDto,
  RoleListItemDto,
  UpdateRoleRequestDto,
  UpdateUserRequestDto,
  UserDetailsDto,
  UserListItemDto,
} from './types'

export async function getCurrentAccess(): Promise<CurrentAccessDto> {
  return await httpGet<CurrentAccessDto>('/api/security/me/access')
}

export async function getPermissionDefinitions(): Promise<PermissionDefinitionDto[]> {
  return await httpGet<PermissionDefinitionDto[]>('/api/security/permissions/definitions')
}

export async function getUsers(): Promise<UserListItemDto[]> {
  return await httpGet<UserListItemDto[]>('/api/security/users')
}

export async function createUser(request: CreateUserRequestDto): Promise<UserDetailsDto> {
  return await httpPost<UserDetailsDto, CreateUserRequestDto>('/api/security/users', request)
}

export async function getUser(userId: string): Promise<UserDetailsDto> {
  return await httpGet<UserDetailsDto>(`/api/security/users/${encodeURIComponent(userId)}`)
}

export async function updateUser(userId: string, request: UpdateUserRequestDto): Promise<UserDetailsDto> {
  return await httpPut<UserDetailsDto, UpdateUserRequestDto>(`/api/security/users/${encodeURIComponent(userId)}`, request)
}

export async function deactivateUser(userId: string): Promise<void> {
  await httpPost<void>(`/api/security/users/${encodeURIComponent(userId)}/deactivate`)
}

export async function reactivateUser(userId: string): Promise<void> {
  await httpPost<void>(`/api/security/users/${encodeURIComponent(userId)}/reactivate`)
}

export async function replaceUserRoles(userId: string, roleIds: string[]): Promise<void> {
  await httpPut<void, ReplaceUserRolesRequestDto>(
    `/api/security/users/${encodeURIComponent(userId)}/roles`,
    { roleIds },
  )
}

export async function getUserEffectiveAccess(userId: string): Promise<EffectiveAccessDto> {
  return await httpGet<EffectiveAccessDto>(`/api/security/users/${encodeURIComponent(userId)}/effective-access`)
}

export async function getRoles(): Promise<RoleListItemDto[]> {
  return await httpGet<RoleListItemDto[]>('/api/security/roles')
}

export async function createRole(request: CreateRoleRequestDto): Promise<RoleDetailsDto> {
  return await httpPost<RoleDetailsDto, CreateRoleRequestDto>('/api/security/roles', request)
}

export async function getRole(roleId: string): Promise<RoleDetailsDto> {
  return await httpGet<RoleDetailsDto>(`/api/security/roles/${encodeURIComponent(roleId)}`)
}

export async function updateRole(roleId: string, request: UpdateRoleRequestDto): Promise<RoleDetailsDto> {
  return await httpPut<RoleDetailsDto, UpdateRoleRequestDto>(`/api/security/roles/${encodeURIComponent(roleId)}`, request)
}

export async function deactivateRole(roleId: string): Promise<void> {
  await httpPost<void>(`/api/security/roles/${encodeURIComponent(roleId)}/deactivate`)
}

export async function reactivateRole(roleId: string): Promise<void> {
  await httpPost<void>(`/api/security/roles/${encodeURIComponent(roleId)}/reactivate`)
}

export async function replaceRolePermissions(roleId: string, permissions: ReplaceRolePermissionsRequestDto['permissions']): Promise<void> {
  await httpPut<void, ReplaceRolePermissionsRequestDto>(
    `/api/security/roles/${encodeURIComponent(roleId)}/permissions`,
    { permissions },
  )
}

