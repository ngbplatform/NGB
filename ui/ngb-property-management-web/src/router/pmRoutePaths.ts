export type PmOpenItemsKind = 'receivables' | 'payables'
export type PmReconciliationKind = 'receivables' | 'payables'

export type PmReconciliationRouteOptions = {
  fromMonth?: string | null
  toMonth?: string | null
  mode?: string | null
}

function appendQuery(
  path: string,
  query: Record<string, string | null | undefined>,
): string {
  const params = new URLSearchParams()

  for (const [key, value] of Object.entries(query)) {
    const normalized = String(value ?? '').trim()
    if (!normalized) continue
    params.set(key, normalized)
  }

  const serialized = params.toString()
  return serialized ? `${path}?${serialized}` : path
}

export function buildPmOpenItemsPath(kind: PmOpenItemsKind): string {
  return kind === 'payables' ? '/payables/open-items' : '/receivables/open-items'
}

export function buildPmReconciliationPath(
  kind: PmReconciliationKind,
  options: PmReconciliationRouteOptions = {},
): string {
  const base = kind === 'payables' ? '/payables/reconciliation' : '/receivables/reconciliation'
  return appendQuery(base, {
    fromMonth: options.fromMonth ?? undefined,
    toMonth: options.toMonth ?? undefined,
    mode: options.mode ?? undefined,
  })
}
