import { describe, expect, it } from 'vitest'

import type { AuthSnapshot } from '../../../../src/ngb/auth/types'

describe('auth types', () => {
  it('supports normalized authentication snapshots', () => {
    const snapshot: AuthSnapshot = {
      initialized: true,
      authenticated: true,
      token: 'access-token',
      subject: 'user-1',
      displayName: 'Ava Admin',
      preferredUsername: 'ava',
      email: 'ava@example.com',
      realmRoles: ['admin'],
      resourceRoles: {
        ngb: ['editor', 'approver'],
      },
      roles: ['admin', 'editor', 'approver'],
    }

    expect(snapshot.authenticated).toBe(true)
    expect(snapshot.resourceRoles.ngb).toContain('approver')
  })
})
