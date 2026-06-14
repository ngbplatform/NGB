import { defineStore } from 'pinia'
import { toErrorMessage } from '../utils/errorMessage'
import { getCurrentAccess } from './api'
import { buildPermissionKey, hasPermission, SYSTEM_PERMISSIONS, toPermissionKeySet, type PermissionKeyLike } from './permissions'
import type { CurrentAccessDto } from './types'

export const useAccessStore = defineStore('access', {
  state: () => ({
    current: null as CurrentAccessDto | null,
    permissionKeys: new Set<string>(),
    isLoading: false,
    error: null as string | null,
    loadedAt: 0,
  }),
  getters: {
    isActive: (state) => state.current?.isActive === true,
    canViewUsers: (state) => state.current?.isBootstrapAdmin === true || hasPermission(state.current?.permissions, SYSTEM_PERMISSIONS.usersView),
    canManageUsers: (state) => state.current?.isBootstrapAdmin === true || hasPermission(state.current?.permissions, SYSTEM_PERMISSIONS.usersManage),
    canViewRoles: (state) => state.current?.isBootstrapAdmin === true || hasPermission(state.current?.permissions, SYSTEM_PERMISSIONS.rolesView),
    canManageRoles: (state) => state.current?.isBootstrapAdmin === true || hasPermission(state.current?.permissions, SYSTEM_PERMISSIONS.rolesManage),
    canViewPermissions: (state) => state.current?.isBootstrapAdmin === true || hasPermission(state.current?.permissions, SYSTEM_PERMISSIONS.permissionsView),
    applicationRoleNames: (state) => {
      const seen = new Set<string>()
      return (state.current?.roles ?? [])
        .filter((role) => role.isActive)
        .map((role) => (role.name || role.code).trim())
        .filter((name) => {
          const key = name.toLowerCase()
          if (!name || seen.has(key)) return false
          seen.add(key)
          return true
        })
    },
  },
  actions: {
    async load(force = false): Promise<CurrentAccessDto | null> {
      if (this.isLoading) return this.current
      if (!force && this.current) return this.current

      this.isLoading = true
      this.error = null

      try {
        const access = await getCurrentAccess()
        this.current = access
        this.permissionKeys = toPermissionKeySet(access.permissions)
        this.loadedAt = Date.now()
        return access
      } catch (cause) {
        this.current = null
        this.permissionKeys = new Set()
        this.error = toErrorMessage(cause, 'Failed to load access profile')
        return null
      } finally {
        this.isLoading = false
      }
    },
    hasPermission(permission: PermissionKeyLike): boolean {
      return this.permissionKeys.has(buildPermissionKey(permission))
    },
    reset(): void {
      this.current = null
      this.permissionKeys = new Set()
      this.error = null
      this.loadedAt = 0
      this.isLoading = false
    },
  },
})
