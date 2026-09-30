import { describe, expect, it } from 'vitest'

import {
  findFirstPermittedMenuRoute,
  menuContainsRoute,
  type PermissionAwareMenuGroup,
  resolvePermissionAwareLanding,
} from '../../../src/router/permissionAwareLanding'

function group(ordinal: number, routes: Array<[string, number]>): PermissionAwareMenuGroup {
  return {
    ordinal,
    items: routes.map(([route, itemOrdinal]) => ({
      route,
      ordinal: itemOrdinal,
    })),
  }
}

describe('permission-aware landing', () => {
  it('keeps dashboard when it is present in the permission-filtered menu', () => {
    const groups = [
      group(10, [['/home', 10]]),
      group(20, [['/reports/accounting.balance_sheet', 10]]),
    ]

    expect(resolvePermissionAwareLanding(groups, '/home')).toBeNull()
  })

  it('redirects home to the first permitted menu route when dashboard is hidden', () => {
    const groups = [
      group(20, [['/reports/accounting.balance_sheet', 20]]),
      group(10, [['/reports/pm.tenant_statement', 30], ['/reports/pm.aging', 10]]),
    ]

    expect(findFirstPermittedMenuRoute(groups)).toBe('/reports/pm.aging')
    expect(resolvePermissionAwareLanding(groups, '/home')).toBe('/reports/pm.aging')
  })

  it('matches child routes under a permitted menu route', () => {
    const groups = [
      group(10, [['/admin/security/users', 10]]),
    ]

    expect(menuContainsRoute(groups, '/admin/security/users/123')).toBe(true)
    expect(resolvePermissionAwareLanding(groups, '/admin/security/users/123')).toBeNull()
  })

  it('rejects external and empty routes and reports exact and missing menu matches', () => {
    const groups = [
      group(10, [['https://example.test/external', 1], ['   ', 2], ['/catalogs/pm.property', 3]]),
    ]

    expect(findFirstPermittedMenuRoute(groups)).toBe('/catalogs/pm.property')
    expect(menuContainsRoute(groups, '/catalogs/pm.property')).toBe(true)
    expect(menuContainsRoute(groups, '/catalogs/pm.party')).toBe(false)
    expect(menuContainsRoute(groups, 'https://example.test/external')).toBe(false)
    expect(menuContainsRoute(groups, null as never)).toBe(false)
    expect(menuContainsRoute([group(1, [['https://example.test/external', 1]])], '/home')).toBe(false)
    expect(findFirstPermittedMenuRoute([])).toBeNull()
  })

  it('handles root, already-selected, empty, and external landing targets', () => {
    expect(resolvePermissionAwareLanding([group(1, [['/catalogs/pm.property', 1]])], '/')).toBe('/catalogs/pm.property')
    expect(resolvePermissionAwareLanding([group(1, [['/', 1]])], '/')).toBeNull()
    expect(resolvePermissionAwareLanding([], '/')).toBeNull()
    expect(resolvePermissionAwareLanding([], 'https://example.test')).toBeNull()
  })
})
