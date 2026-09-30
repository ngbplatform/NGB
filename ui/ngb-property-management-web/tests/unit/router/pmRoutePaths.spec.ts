import { describe, expect, it } from 'vitest'

import {
  buildPmOpenItemsPath,
  buildPmReconciliationPath,
  buildPmSecurityRolesPath,
  buildPmSecurityUsersPath,
} from '../../../src/router/pmRoutePaths'

describe('pm route paths', () => {
  it('builds open items routes', () => {
    expect(buildPmOpenItemsPath('receivables')).toBe('/receivables/open-items')
    expect(buildPmOpenItemsPath('payables')).toBe('/payables/open-items')
  })

  it('adds only populated reconciliation query params', () => {
    expect(buildPmReconciliationPath('receivables')).toBe('/receivables/reconciliation')
    expect(buildPmReconciliationPath('payables', {
      fromMonth: '2026-04-01',
      toMonth: '2026-04-01',
      mode: 'Balance',
    })).toBe('/payables/reconciliation?fromMonth=2026-04-01&toMonth=2026-04-01&mode=Balance')
    expect(buildPmReconciliationPath('receivables', {
      fromMonth: null,
      toMonth: ' ',
      mode: '',
    })).toBe('/receivables/reconciliation')
  })

  it('builds security list and encoded entity routes', () => {
    expect(buildPmSecurityUsersPath()).toBe('/admin/security/users')
    expect(buildPmSecurityUsersPath(null)).toBe('/admin/security/users')
    expect(buildPmSecurityUsersPath(' user/1 ')).toBe('/admin/security/users/user%2F1')

    expect(buildPmSecurityRolesPath()).toBe('/admin/security/roles')
    expect(buildPmSecurityRolesPath(' ')).toBe('/admin/security/roles')
    expect(buildPmSecurityRolesPath(' role/1 ')).toBe('/admin/security/roles/role%2F1')
  })
})
