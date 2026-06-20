import type { EditorAuditBehavior } from '../editor/types'

export const USER_AUDIT_BEHAVIOR: EditorAuditBehavior = {
  actionTitles: {
    'security.user.create': 'User created',
    'security.user.update': 'User updated',
    'security.user.deactivate': 'User deactivated',
    'security.user.reactivate': 'User reactivated',
    'security.user.roles.replace': 'User roles changed',
  },
}

export const ROLE_AUDIT_BEHAVIOR: EditorAuditBehavior = {
  actionTitles: {
    'security.role.create': 'Role created',
    'security.role.update': 'Role updated',
    'security.role.deactivate': 'Role deactivated',
    'security.role.reactivate': 'Role reactivated',
    'security.role.permissions.replace': 'Role permissions changed',
  },
}
