import { httpGet, type CommandPaletteSearchRequestDto, type CommandPaletteSearchResponseDto } from '@ngbplatform/ui'

interface SearchResult {
  items: { kind: string, typeCode: string, id: string | null, title: string }[]
  truncated: boolean
  unavailableSources: string[]
}

// The service authorizes every source. Do not substitute cached catalog/document lists.
export async function searchApplication(
  request: CommandPaletteSearchRequestDto,
  signal?: AbortSignal,
): Promise<CommandPaletteSearchResponseDto> {
  if (request.scope === 'commands' || request.scope === 'pages') return { groups: [] }
  const kinds = request.scope === 'documents' ? ['Document', 'GeneralJournalEntry']
    : request.scope === 'catalogs' ? ['Catalog'] : request.scope === 'reports' ? ['Report'] : ['']
  const limit = Math.max(1, Math.min(request.limit ?? 20, 50))
  const results = await Promise.all(kinds.map(kind => {
    const query = new URLSearchParams({ query: request.query, limit: String(limit) })
    if (kind) query.set('kind', kind)
    return httpGet<SearchResult>(`/api/application-search?${query}`, null, { signal })
  }))
  const groups: CommandPaletteSearchResponseDto['groups'] = []
  for (const hit of results.flatMap(result => result.items).slice(0, limit)) {
    const catalog = hit.kind === 'catalog'
    const report = hit.kind === 'report'
    const journal = hit.kind === 'generalJournalEntry'
    if (!catalog && !report && !journal && hit.kind !== 'document') continue
    if (!report && !hit.id) continue
    const code = report ? 'reports' : catalog ? 'catalogs' : 'documents'
    let group = groups.find(item => item.code === code)
    if (!group) {
      group = { code, label: report ? 'Reports' : catalog ? 'Catalogs' : 'Documents', items: [] }
      groups.push(group)
    }
    const type = encodeURIComponent(hit.typeCode)
    const id = encodeURIComponent(hit.id ?? '')
    group.items.push({
      key: `${hit.kind}:${hit.typeCode}:${hit.id ?? ''}`,
      kind: report ? 'report' : catalog ? 'catalog' : 'document',
      title: hit.title,
      icon: report ? 'bar-chart' : catalog ? 'folder' : 'file-text',
      route: report ? `/reports/${type}` : journal ? `/accounting/general-journal-entries/${id}`
        : `/${catalog ? 'catalogs' : 'documents'}/${type}/${id}`,
      openInNewTabSupported: true,
      score: 100,
    })
  }
  if (groups.length === 0 && results.some(result => result.unavailableSources.length > 0)) {
    throw new Error('Search is temporarily unavailable. Please retry.')
  }
  return { groups }
}
