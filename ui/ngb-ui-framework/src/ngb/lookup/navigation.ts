import { currentRouteBackTarget, withBackTarget } from '../router/backNavigation'
import type { LookupHint, LookupSource } from '../metadata/types'
import { getConfiguredNgbLookup } from './config'

export type LookupValueLike =
  | string
  | {
      id?: string | null
    }
  | null
  | undefined

export type LookupNavigationSource = LookupHint | LookupSource

export function lookupValueId(value: LookupValueLike): string | null {
  if (!value) return null

  if (typeof value === 'string') {
    const normalized = value.trim()
    return normalized.length > 0 ? normalized : null
  }

  const normalized = String(value.id ?? '').trim()
  return normalized.length > 0 ? normalized : null
}

async function resolveDocumentType(documentTypes: string[], id: string): Promise<string | null> {
  const candidates = Array.from(new Set(documentTypes.map((entry) => String(entry ?? '').trim()).filter((entry) => entry.length > 0)))
  if (candidates.length === 0) return null
  if (candidates.length === 1) return candidates[0] ?? null

  const config = getConfiguredNgbLookup()

  try {
    const items = await config.loadDocumentItemsByIds(candidates, [id])
    const resolved = items.find((item) => {
      const itemId = String(item.id ?? '').trim()
      const documentType = String(item.documentType ?? '').trim()
      return itemId === id && candidates.includes(documentType)
    })

    const resolvedType = String(resolved?.documentType ?? '').trim()
    return resolvedType.length > 0 ? resolvedType : null
  } catch {
    return null
  }
}

export async function buildLookupFieldTargetUrl(args: {
  hint: LookupNavigationSource | null
  value: LookupValueLike
  route: {
    fullPath: string
  }
}): Promise<string | null> {
  const id = lookupValueId(args.value)
  const hint = args.hint

  if (!id || !hint) return null

  const config = getConfiguredNgbLookup()
  let path: string | null = null

  if (hint.kind === 'catalog') {
    path = config.buildCatalogUrl(hint.catalogType, id)
  } else if (hint.kind === 'coa') {
    path = config.buildCoaUrl(id)
  } else if (hint.kind === 'document') {
    const documentType = await resolveDocumentType(hint.documentTypes, id)
    if (!documentType) return null
    path = config.buildDocumentUrl(documentType, id)
  }

  if (!path) return null
  return withBackTarget(path, currentRouteBackTarget(args.route))
}
