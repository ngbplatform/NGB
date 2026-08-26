import { describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  httpGet: vi.fn(),
  httpPost: vi.fn(),
  httpPut: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpGet: mocks.httpGet,
  httpPost: mocks.httpPost,
  httpPut: mocks.httpPut,
}))

import {
  createRole,
  createUser,
  deactivateUser,
  deactivateRole,
  getCurrentAccess,
  getPermissionDefinitions,
  getRole,
  getRoles,
  getUser,
  getUserEffectiveAccess,
  getUsers,
  reactivateRole,
  reactivateUser,
  replaceRolePermissions,
  replaceUserRoles,
  updateRole,
  updateUser,
} from '../../../../src/ngb/security/api'

describe('security api client', () => {
  it('maps read endpoints', async () => {
    mocks.httpGet.mockResolvedValueOnce({ permissions: [] })
    await getCurrentAccess()
    expect(mocks.httpGet).toHaveBeenLastCalledWith('/api/security/me/access')

    mocks.httpGet.mockResolvedValueOnce([])
    await getPermissionDefinitions()
    expect(mocks.httpGet).toHaveBeenLastCalledWith('/api/security/permissions/definitions')

    mocks.httpGet.mockResolvedValueOnce([])
    await getUsers()
    expect(mocks.httpGet).toHaveBeenLastCalledWith('/api/security/users?offset=0&limit=50')

    mocks.httpGet.mockResolvedValueOnce([])
    await getUsers({ offset: -10, limit: 25, isActive: false })
    expect(mocks.httpGet).toHaveBeenLastCalledWith('/api/security/users?offset=0&limit=25&isActive=false')

    mocks.httpGet.mockResolvedValueOnce({})
    await getUser('user/id')
    expect(mocks.httpGet).toHaveBeenLastCalledWith('/api/security/users/user%2Fid')

    mocks.httpGet.mockResolvedValueOnce({})
    await getUserEffectiveAccess('user/id')
    expect(mocks.httpGet).toHaveBeenLastCalledWith('/api/security/users/user%2Fid/effective-access')

    mocks.httpGet.mockResolvedValueOnce([])
    await getRoles()
    expect(mocks.httpGet).toHaveBeenLastCalledWith('/api/security/roles')

    mocks.httpGet.mockResolvedValueOnce({})
    await getRole('role/id')
    expect(mocks.httpGet).toHaveBeenLastCalledWith('/api/security/roles/role%2Fid')
  })

  it('maps user write endpoints without delete operations', async () => {
    mocks.httpPost.mockResolvedValueOnce({})
    await createUser({
      email: 'new@example.test',
      firstName: null,
      lastName: null,
      displayName: null,
      enabled: true,
      temporaryPassword: null,
      requirePasswordUpdate: true,
      roleIds: ['role-1'],
    })
    expect(mocks.httpPost).toHaveBeenLastCalledWith('/api/security/users', expect.objectContaining({ email: 'new@example.test' }))

    mocks.httpPut.mockResolvedValueOnce({})
    await updateUser('user-1', {
      email: 'u@example.test',
      firstName: null,
      lastName: null,
      displayName: null,
      enabled: true,
      temporaryPassword: 'New#12345',
      requirePasswordUpdate: false,
      roleIds: [],
    })
    expect(mocks.httpPut).toHaveBeenLastCalledWith('/api/security/users/user-1', expect.objectContaining({ enabled: true, temporaryPassword: 'New#12345' }))

    mocks.httpPost.mockResolvedValueOnce(undefined)
    await deactivateUser('user-1')
    expect(mocks.httpPost).toHaveBeenLastCalledWith('/api/security/users/user-1/deactivate')

    mocks.httpPost.mockResolvedValueOnce(undefined)
    await reactivateUser('user/1')
    expect(mocks.httpPost).toHaveBeenLastCalledWith('/api/security/users/user%2F1/reactivate')

    mocks.httpPut.mockResolvedValueOnce(undefined)
    await replaceUserRoles('user-1', ['role-2'])
    expect(mocks.httpPut).toHaveBeenLastCalledWith('/api/security/users/user-1/roles', { roleIds: ['role-2'] })
  })

  it('maps role write endpoints', async () => {
    mocks.httpPost.mockResolvedValueOnce({})
    await createRole({ code: 'pm-auditor', name: 'Auditor', description: null, permissions: [] })
    expect(mocks.httpPost).toHaveBeenLastCalledWith('/api/security/roles', expect.objectContaining({ code: 'pm-auditor' }))

    mocks.httpPut.mockResolvedValueOnce({})
    await updateRole('role-1', { code: 'pm-auditor', name: 'Auditor', description: null, isActive: true, permissions: [] })
    expect(mocks.httpPut).toHaveBeenLastCalledWith('/api/security/roles/role-1', expect.objectContaining({ isActive: true }))

    mocks.httpPost.mockResolvedValueOnce(undefined)
    await reactivateRole('role-1')
    expect(mocks.httpPost).toHaveBeenLastCalledWith('/api/security/roles/role-1/reactivate')

    mocks.httpPost.mockResolvedValueOnce(undefined)
    await deactivateRole('role/1')
    expect(mocks.httpPost).toHaveBeenLastCalledWith('/api/security/roles/role%2F1/deactivate')

    mocks.httpPut.mockResolvedValueOnce(undefined)
    await replaceRolePermissions('role-1', [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'view' }])
    expect(mocks.httpPut).toHaveBeenLastCalledWith('/api/security/roles/role-1/permissions', {
      permissions: [{ resourceKind: 'system', resourceCode: 'users', actionCode: 'view' }],
    })
  })
})
