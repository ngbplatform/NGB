import { describe, expect, it } from 'vitest'

import {
  buildPermissionAssignment,
  buildPermissionKey,
  groupPermissionDefinitions,
  hasPermission,
  toPermissionKeySet,
} from '../../../../src/ngb/security/permissions'
import type { PermissionDefinitionDto } from '../../../../src/ngb/security/types'

describe('security permissions', () => {
  it('normalizes permission keys and keeps dotted resource codes intact', () => {
    expect(buildPermissionKey({
      resourceKind: ' Document ',
      resourceCode: ' PM.Lease ',
      actionCode: ' View ',
    })).toBe('document.pm.lease.view')

    expect(buildPermissionAssignment('REPORT', 'pm.ar.aging', 'EXECUTE')).toEqual({
      resourceKind: 'report',
      resourceCode: 'pm.ar.aging',
      actionCode: 'execute',
    })
    expect(buildPermissionKey({ resourceKind: null as never, resourceCode: undefined as never, actionCode: ' View ' })).toBe('..view')
  })

  it('checks permissions deny-by-default', () => {
    expect(hasPermission(null, 'system.users.view')).toBe(false)
    expect(hasPermission([], 'system.users.view')).toBe(false)
    expect(hasPermission([
      { resourceKind: 'system', resourceCode: 'users', actionCode: 'view' },
    ], 'system.users.view')).toBe(true)
    expect(toPermissionKeySet(null)).toEqual(new Set())
    expect(toPermissionKeySet([
      { resourceKind: ' System ', resourceCode: ' Users ', actionCode: ' View ' },
    ])).toEqual(new Set(['system.users.view']))
  })

  it('groups definitions deterministically', () => {
    const definitions: PermissionDefinitionDto[] = [
      { resourceKind: 'document', resourceCode: 'pm.lease', actionCode: 'post', displayName: 'Post Lease', group: 'Documents' },
      { resourceKind: 'system', resourceCode: 'users', actionCode: 'view', displayName: 'View Users', group: 'System' },
      { resourceKind: 'document', resourceCode: 'pm.lease', actionCode: 'view', displayName: 'View Lease', group: 'Documents' },
      { resourceKind: 'catalog', resourceCode: 'pm.property', actionCode: 'view', displayName: 'View Property', group: null },
    ]

    const groups = groupPermissionDefinitions(definitions)

    expect(groups.map((group) => group.group)).toEqual(['Documents', 'Other', 'System'])
    expect(groups[0]?.permissions.map((permission) => permission.actionCode)).toEqual(['post', 'view'])
  })
})
