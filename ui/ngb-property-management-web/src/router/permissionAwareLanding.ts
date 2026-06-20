export type PermissionAwareMenuItem = {
  route: string
  ordinal: number
}

export type PermissionAwareMenuGroup = {
  items: PermissionAwareMenuItem[]
  ordinal: number
}

function isExternalRoute(value: string | null | undefined): boolean {
  return /^https?:\/\//i.test(String(value ?? '').trim())
}

function normalizeMenuRoute(value: string | null | undefined): string {
  if (isExternalRoute(value)) return ''
  return String(value ?? '').trim()
}

function sortGroups(groups: PermissionAwareMenuGroup[]): PermissionAwareMenuGroup[] {
  return groups.slice().sort((a, b) => a.ordinal - b.ordinal)
}

function sortItems(group: PermissionAwareMenuGroup) {
  return group.items.slice().sort((a, b) => a.ordinal - b.ordinal)
}

export function findFirstPermittedMenuRoute(groups: PermissionAwareMenuGroup[]): string | null {
  for (const group of sortGroups(groups)) {
    for (const item of sortItems(group)) {
      const route = normalizeMenuRoute(item.route)
      if (route) return route
    }
  }

  return null
}

export function menuContainsRoute(groups: PermissionAwareMenuGroup[], path: string): boolean {
  const normalizedPath = normalizeMenuRoute(path)
  if (!normalizedPath) return false

  for (const group of groups) {
    for (const item of group.items) {
      const route = normalizeMenuRoute(item.route)
      if (!route) continue
      if (normalizedPath === route) return true
      if (normalizedPath.startsWith(`${route}/`)) return true
    }
  }

  return false
}

export function resolvePermissionAwareLanding(groups: PermissionAwareMenuGroup[], targetPath: string): string | null {
  const normalizedTarget = normalizeMenuRoute(targetPath)
  if (normalizedTarget !== '/' && normalizedTarget !== '/home') return null

  if (normalizedTarget === '/home' && menuContainsRoute(groups, '/home')) return null

  const firstRoute = findFirstPermittedMenuRoute(groups)
  if (!firstRoute || firstRoute === normalizedTarget) return null

  return firstRoute
}
