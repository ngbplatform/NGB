import { httpGet, httpPost, httpPut, type HttpRequestOptions } from '../api/http'
import type { PageResponseDto } from '../api/contracts'
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

function httpGetWithOptions<T>(url: string, options?: HttpRequestOptions): Promise<T> {
  return options ? httpGet<T>(url, null, options) : httpGet<T>(url)
}

export async function getCurrentAccess(): Promise<CurrentAccessDto> {
  return await httpGet<CurrentAccessDto>('/api/security/me/access')
}

export async function getPermissionDefinitions(options?: HttpRequestOptions): Promise<PermissionDefinitionDto[]> {
  return await httpGetWithOptions<PermissionDefinitionDto[]>('/api/security/permissions/definitions', options)
}

export async function getUsers(request: {
  offset?: number
  limit?: number
  isActive?: boolean | null
} = {}, options?: HttpRequestOptions): Promise<PageResponseDto<UserListItemDto>> {
  const query = new URLSearchParams()
  query.set('offset', String(Math.max(0, request.offset ?? 0)))
  query.set('limit', String(Math.max(1, request.limit ?? 50)))
  if (request.isActive != null) query.set('isActive', String(request.isActive))
  return await httpGetWithOptions<PageResponseDto<UserListItemDto>>(`/api/security/users?${query.toString()}`, options)
}

export async function createUser(request: CreateUserRequestDto): Promise<UserDetailsDto> {
  return await httpPost<UserDetailsDto, CreateUserRequestDto>('/api/security/users', request)
}

export async function getUser(userId: string, options?: HttpRequestOptions): Promise<UserDetailsDto> {
  return await httpGetWithOptions<UserDetailsDto>(`/api/security/users/${encodeURIComponent(userId)}`, options)
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

export async function getUserEffectiveAccess(userId: string, options?: HttpRequestOptions): Promise<EffectiveAccessDto> {
  return await httpGetWithOptions<EffectiveAccessDto>(`/api/security/users/${encodeURIComponent(userId)}/effective-access`, options)
}

export async function getRoles(options?: HttpRequestOptions): Promise<RoleListItemDto[]> {
  return await httpGetWithOptions<RoleListItemDto[]>('/api/security/roles', options)
}

export async function createRole(request: CreateRoleRequestDto): Promise<RoleDetailsDto> {
  return await httpPost<RoleDetailsDto, CreateRoleRequestDto>('/api/security/roles', request)
}

export async function getRole(roleId: string, options?: HttpRequestOptions): Promise<RoleDetailsDto> {
  return await httpGetWithOptions<RoleDetailsDto>(`/api/security/roles/${encodeURIComponent(roleId)}`, options)
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
