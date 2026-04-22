export type AuthSnapshot = {
  initialized: boolean
  authenticated: boolean
  token: string | null
  subject: string | null
  displayName: string | null
  preferredUsername: string | null
  email: string | null
  realmRoles: string[]
  resourceRoles: Record<string, string[]>
  roles: string[]
}
