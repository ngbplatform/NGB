import { beforeEach, expect, it, vi } from 'vitest'
import { searchApplication } from '../../src/search'

const state = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('@ngbplatform/ui', () => ({ httpGet: state.get }))

beforeEach(() => {
  vi.resetAllMocks()
  state.get.mockResolvedValue({ items: [], unavailableSources: [], truncated: false })
})

it('maps authorized server identities to the correct editor and report routes', async () => {
  state.get.mockResolvedValue({ unavailableSources: [], truncated: false, items: [
    { kind: 'document', typeCode: 'example.order', id: 'first', title: 'O-2026-000001' },
    { kind: 'document', typeCode: 'example.order', id: 'second', title: 'O-2026-000002' },
    { kind: 'catalog', typeCode: 'example.customer', id: 'customer', title: 'Alex Morgan' },
    { kind: 'report', typeCode: 'example.summary', id: null, title: 'Summary' },
    { kind: 'generalJournalEntry', typeCode: 'general_journal_entry', id: 'journal', title: 'Journal entry' },
    { kind: 'unknown', typeCode: 'unknown', id: null, title: 'Unknown source' },
    { kind: 'document', typeCode: 'example.order', id: null, title: 'Missing identity' },
  ] })
  const controller = new AbortController()
  const response = await searchApplication({ query: 'Alex & order', scope: null }, controller.signal)
  const items = response.groups.flatMap(group => group.items)
  expect(items.map(item => item.route)).toEqual([
    '/documents/example.order/first', '/documents/example.order/second', '/accounting/general-journal-entries/journal',
    '/catalogs/example.customer/customer', '/reports/example.summary',
  ])
  expect(items.map(item => item.icon)).toEqual(['file-text', 'file-text', 'file-text', 'folder', 'bar-chart'])
  expect(new Set(items.map(item => item.key)).size).toBe(5)
  expect(state.get).toHaveBeenCalledWith('/api/equipment-search?query=Alex+%26+order&limit=20', null,
    { signal: controller.signal })
})

it.each(['commands', 'pages'] as const)('keeps %s local without fetching records', async scope => {
  expect(await searchApplication({ query: 'Open', scope })).toEqual({ groups: [] })
  expect(state.get).not.toHaveBeenCalled()
})

it('queries document and journal scopes separately and respects the request limit', async () => {
  state.get.mockResolvedValue({ unavailableSources: [], truncated: true, items: [
    { kind: 'document', typeCode: 'example.order', id: 'a/b', title: 'Order' },
  ] })
  const response = await searchApplication({ query: 'Order', scope: 'documents', limit: 1 })
  expect(state.get.mock.calls.map(([url]) => new URL(url, 'https://example.test').searchParams.get('kind')))
    .toEqual(['Document', 'GeneralJournalEntry'])
  expect(response.groups[0].items).toHaveLength(1)
  expect(response.groups[0].items[0].route).toBe('/documents/example.order/a%2Fb')
})

it.each([['catalogs', 'Catalog', 0, '1'], ['reports', 'Report', 1000, '50']] as const)(
  'filters %s and bounds the limit', async (scope, kind, limit, bounded) => {
    await searchApplication({ query: 'Summary', scope, limit })
    const query = new URL(state.get.mock.calls[0][0], 'https://example.test').searchParams
    expect(query.get('kind')).toBe(kind)
    expect(query.get('limit')).toBe(bounded)
  },
)

it('does not misrepresent a failed or cancelled source as an empty successful search', async () => {
  state.get.mockResolvedValueOnce({ items: [], unavailableSources: ['catalog:example.customer'] })
  await expect(searchApplication({ query: 'Alex', scope: null })).rejects.toThrow('temporarily unavailable')
  state.get.mockRejectedValueOnce(new DOMException('Cancelled', 'AbortError'))
  await expect(searchApplication({ query: 'Alex', scope: null })).rejects.toMatchObject({ name: 'AbortError' })
})

it('keeps authorized matches from available sources when another source fails', async () => {
  state.get.mockResolvedValue({
    items: [{ kind: 'catalog', typeCode: 'example.customer', id: 'customer', title: 'Alex' }],
    truncated: true,
    unavailableSources: ['reports'],
  })
  const result = await searchApplication({ query: 'Alex', scope: null })
  expect(result.groups[0].items[0].route).toBe('/catalogs/example.customer/customer')
})
