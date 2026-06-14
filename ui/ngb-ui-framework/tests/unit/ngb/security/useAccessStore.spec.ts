import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  getCurrentAccess: vi.fn(),
}))

vi.mock('../../../../src/ngb/security/api', () => ({
  getCurrentAccess: mocks.getCurrentAccess,
}))

import { useAccessStore } from '../../../../src/ngb/security/useAccessStore'

describe('useAccessStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  it('loads current access and evaluates permissions from backend profile', async () => {
    mocks.getCurrentAccess.mockResolvedValue({
      userId: 'user-1',
      authSubject: 'kc-user',
      isAuthenticated: true,
      isActive: true,
      isBootstrapAdmin: false,
      accessVersion: 4,
      roles: [
        { roleId: 'role-1', code: 'pm-admin', name: 'PM Administrator', isSystem: true, isActive: true },
        { roleId: 'role-2', code: 'pm-old', name: 'PM Old', isSystem: false, isActive: false },
      ],
      permissions: [
        { resourceKind: 'system', resourceCode: 'users', actionCode: 'view' },
        { resourceKind: 'document', resourceCode: 'pm.lease', actionCode: 'post' },
      ],
    })

    const store = useAccessStore()
    await store.load()

    expect(store.canViewUsers).toBe(true)
    expect(store.canManageUsers).toBe(false)
    expect(store.applicationRoleNames).toEqual(['PM Administrator'])
    expect(store.hasPermission('document.pm.lease.post')).toBe(true)
    expect(store.hasPermission({ resourceKind: 'document', resourceCode: 'pm.lease', actionCode: 'unpost' })).toBe(false)
  })

  it('does not reload cached access unless forced', async () => {
    mocks.getCurrentAccess.mockResolvedValue({
      userId: 'user-1',
      authSubject: 'kc-user',
      isAuthenticated: true,
      isActive: true,
      isBootstrapAdmin: false,
      accessVersion: 4,
      permissions: [],
    })

    const store = useAccessStore()
    await store.load()
    await store.load()
    await store.load(true)

    expect(mocks.getCurrentAccess).toHaveBeenCalledTimes(2)
  })

  it('treats bootstrap admin as allowed for security management actions', async () => {
    mocks.getCurrentAccess.mockResolvedValue({
      userId: 'bootstrap',
      authSubject: 'bootstrap',
      isAuthenticated: true,
      isActive: true,
      isBootstrapAdmin: true,
      accessVersion: 1,
      permissions: [],
    })

    const store = useAccessStore()
    await store.load()

    expect(store.canViewUsers).toBe(true)
    expect(store.canManageUsers).toBe(true)
    expect(store.canViewRoles).toBe(true)
    expect(store.canManageRoles).toBe(true)
    expect(store.canViewPermissions).toBe(true)
  })

  it('clears permissions on load failure', async () => {
    mocks.getCurrentAccess.mockRejectedValue(new Error('offline'))

    const store = useAccessStore()
    await store.load()

    expect(store.current).toBeNull()
    expect(store.canViewUsers).toBe(false)
    expect(store.hasPermission('system.users.view')).toBe(false)
    expect(store.error).toBe('offline')
  })
})
