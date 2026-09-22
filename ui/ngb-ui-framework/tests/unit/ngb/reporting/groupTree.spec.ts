import { describe, expect, it, vi } from 'vitest'
import { ReportGroupTree, REPORT_GROUP_LIMITS } from '../../../../src/ngb/reporting/groupTree'
import { ReportRowKind, type ReportExecutionResponseDto, type ReportSheetRowDto } from '../../../../src/ngb/reporting/types'

const detail = (label: string): ReportSheetRowDto => ({ rowKind: ReportRowKind.Detail, cells: [{ display: label }] })
const group = (path: unknown[]): ReportSheetRowDto => ({ ...detail(String(path.at(-1))), rowKind: ReportRowKind.Group, childrenPath: path })
function page(rows: ReportSheetRowDto[], nextCursor: string | null = null): ReportExecutionResponseDto {
  return { sheet: { columns: [{ code: 'name', title: 'Name', dataType: 'string' }], rows }, offset: 0, limit: 100, hasMore: !!nextCursor, nextCursor }
}
function setup(rows: ReportSheetRowDto[], loader: Parameters<ReportGroupTree['reset']>[1]) {
  const tree = new ReportGroupTree(vi.fn())
  tree.reset(page(rows).sheet, loader)
  return tree
}
function toggle(tree: ReportGroupTree, label: string) {
  const row = tree.sheet!.rows.find(row => row.cells[0]?.display === label)!
  tree.act(row.group!.id, 'toggle')
}
const labels = (tree: ReportGroupTree) => tree.sheet!.rows.filter(row => !row.control).map(row => row.cells[0]?.display)
async function settled(tree: ReportGroupTree) {
  await vi.waitFor(() => expect(tree.sheet!.rows.some(row => row.control?.text === 'Loading group…')).toBe(false))
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}

describe('inline report groups', () => {
  it('ignores unknown groups and unavailable paging directions and renders empty groups', async () => {
    const tree = new ReportGroupTree(vi.fn())
    expect(tree.sheet).toBeNull()
    tree.act('missing', 'toggle')
    tree.act('missing', 'next')
    const loader = vi.fn(async () => page([]))
    tree.reset(page([group(['A']), { ...detail('Last group'), rowKind: ReportRowKind.Group }]).sheet, loader)
    tree.act('missing', 'toggle')
    toggle(tree, 'A')
    await settled(tree)
    expect(tree.sheet!.rows.find(row => row.control)?.control?.text).toBe('No rows in this group.')
    const id = tree.sheet!.rows[0]!.group!.id
    tree.act(id, 'next')
    tree.act(id, 'previous')
    expect(loader).toHaveBeenCalledOnce()
  })

  it('offers a reload for an expanded child cancelled before its first page arrived', async () => {
    const pending = deferred<ReportExecutionResponseDto>()
    const loader = vi.fn().mockResolvedValueOnce(page([group(['A', 'nested'])]))
      .mockReturnValueOnce(pending.promise).mockResolvedValueOnce(page([detail('reloaded')]))
    const tree = setup([group(['A'])], loader)
    toggle(tree, 'A'); await settled(tree)
    toggle(tree, 'nested')
    toggle(tree, 'A')
    toggle(tree, 'A')
    const reload = tree.sheet!.rows.find(row => row.control?.text === 'Reload group')!.control!
    expect(reload.action).toBe('restart')
    tree.act(reload.id, reload.action!)
    await settled(tree)
    pending.resolve(page([detail('stale')]))
    await Promise.resolve(); await Promise.resolve()
    expect(labels(tree)).toEqual(['A', 'nested', 'reloaded'])
  })

  it('restarts a group after its backward cursor history expires', async () => {
    const loader = vi.fn(async (_path: unknown[], cursor: string | null) => {
      const start = Number(cursor ?? 0)
      return page(Array.from({ length: 100 }, (_, i) => detail(String(start + i))), String(start + 100))
    })
    const tree = setup([group(['A'])], loader)
    toggle(tree, 'A'); await settled(tree)
    const id = tree.sheet!.rows[0]!.group!.id
    for (let i = 0; i < 40; i++) { tree.act(id, 'next'); await Promise.resolve(); await Promise.resolve() }
    while (tree.sheet!.rows.some(row => row.control?.action === 'previous')) {
      tree.act(id, 'previous'); await Promise.resolve(); await Promise.resolve()
    }
    expect(tree.sheet!.rows.find(row => row.control?.action === 'restart')?.control?.text).toBe('Back to group beginning')
    tree.act(id, 'restart'); await settled(tree)
    expect(labels(tree)[1]).toBe('0')
    expect(loader.mock.lastCall?.[1]).toBeNull()
  })

  it('ignores failures from a cancelled request after resetting the report', async () => {
    let reject!: (error: Error) => void
    const loader = vi.fn(() => new Promise<ReportExecutionResponseDto>((_resolve, fail) => { reject = fail }))
    const tree = setup([group(['A'])], loader)
    toggle(tree, 'A')
    tree.reset()
    reject(new Error('Old request failed'))
    await Promise.resolve(); await Promise.resolve()
    expect(tree.sheet).toBeNull()
    tree.reset(page([detail('Fresh report')]).sheet, loader)
    expect(labels(tree)).toEqual(['Fresh report'])
  })

  it('does not call a missing loader', () => {
    const tree = setup([group(['A'])], null)
    toggle(tree, 'A')
    expect(labels(tree)).toEqual(['A'])
    tree.reset()
    expect(tree.sheet).toBeNull()
  })

  it('lazily expands independent branches under their parents, keeps totals and reuses cached pages', async () => {
    const loader = vi.fn(async (path: unknown[]) => page([detail(`${path[0]} child`), { ...detail('branch total'), rowKind: ReportRowKind.Total }]))
    const total = { ...detail('total'), rowKind: ReportRowKind.Total }
    const tree = setup([group(['A']), group(['B']), total], loader)
    expect(loader).not.toHaveBeenCalled()
    toggle(tree, 'A')
    await settled(tree)
    expect(labels(tree)).toEqual(['A', 'A child', 'B', 'total'])
    const child = tree.sheet!.rows[1]!
    expect(child.outlineLevel).toBe(1)
    toggle(tree, 'B')
    await settled(tree)
    toggle(tree, 'A')
    expect(labels(tree)).toEqual(['A', 'B', 'B child', 'total'])
    toggle(tree, 'A')
    expect(tree.sheet!.rows[1]).toBe(child)
    expect(loader).toHaveBeenCalledTimes(2)
  })

  it('keeps typed keys, nesting and cursors isolated, and suppresses duplicate requests', async () => {
    const loader = vi.fn(async (path: unknown[], cursor: string | null) => path.length === 1
      ? page([group([...path, 'nested'])]) : page([detail(cursor ? 'second' : 'first')], cursor ? null : 'page-2'))
    const tree = setup([group([1]), group(['1'])], loader)
    const [numeric, text] = tree.sheet!.rows.map(row => row.group!.id)
    expect(numeric).not.toBe(text)
    tree.act(numeric!, 'toggle')
    await settled(tree)
    toggle(tree, 'nested')
    await settled(tree)
    const marker = tree.sheet!.rows.find(row => row.control?.autoLoad)!
    tree.act(marker.control!.id, 'next')
    tree.act(marker.control!.id, 'next')
    await settled(tree)
    expect(loader).toHaveBeenCalledTimes(3)
    expect(loader.mock.calls[2]?.slice(0, 2)).toEqual([[1, 'nested'], 'page-2'])
    expect(labels(tree)).toEqual(['1', 'nested', 'first', 'second', '1'])
    expect(tree.sheet!.rows[2]!.outlineLevel).toBe(2)
  })

  it('ignores continuation notifications delivered after an ancestor collapses', async () => {
    const loader = vi.fn(async (path: unknown[]) => path.length === 1
      ? page([group([...path, 'nested'])]) : page([detail('first')], 'next'))
    const tree = setup([group(['A'])], loader)
    toggle(tree, 'A'); await settled(tree)
    toggle(tree, 'nested'); await settled(tree)
    const nestedId = tree.sheet!.rows[1]!.group!.id
    toggle(tree, 'A')
    tree.act(nestedId, 'next')
    expect(loader).toHaveBeenCalledTimes(2)
    expect(labels(tree)).toEqual(['A'])
  })

  it('shows a branch error and retries the same cursor without replacing siblings', async () => {
    const loader = vi.fn().mockResolvedValueOnce(page([detail('one')], 'next')).mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValueOnce(page([detail('two')]))
    const tree = setup([group(['A']), detail('sibling')], loader)
    toggle(tree, 'A'); await settled(tree)
    const id = tree.sheet!.rows[0]!.group!.id
    tree.act(id, 'next'); await settled(tree)
    expect(labels(tree)).toEqual(['A', 'one', 'sibling'])
    expect(tree.sheet!.rows.find(row => row.control?.error)?.control?.text).toBe('Unavailable')
    tree.act(id, 'retry'); await settled(tree)
    expect(loader.mock.calls[1]?.[1]).toBe('next')
    expect(loader.mock.calls[2]?.[1]).toBe('next')
    expect(labels(tree)).toEqual(['A', 'one', 'two', 'sibling'])
  })

  it('cancels collapsed and reset branches and ignores late responses', async () => {
    const pending = deferred<ReportExecutionResponseDto>()
    const loader = vi.fn(() => pending.promise)
    const tree = setup([group(['A'])], loader)
    toggle(tree, 'A')
    const signal = (loader.mock.calls as unknown[][])[0]![2] as AbortSignal
    toggle(tree, 'A')
    expect(signal.aborted).toBe(true)
    tree.reset(page([detail('another report')]).sheet, loader)
    pending.resolve(page([detail('stale')]))
    await Promise.resolve(); await Promise.resolve()
    expect(labels(tree)).toEqual(['another report'])
  })

  it('bounds concurrent requests and discards cancelled queued work', async () => {
    const pending = deferred<ReportExecutionResponseDto>()
    const loader = vi.fn(() => pending.promise)
    const tree = setup([group(['A']), group(['B']), group(['C'])], loader)
    toggle(tree, 'A'); toggle(tree, 'B'); toggle(tree, 'C')
    expect(loader).toHaveBeenCalledTimes(REPORT_GROUP_LIMITS.concurrentRequests)
    toggle(tree, 'C')
    pending.resolve(page([detail('child')]))
    await settled(tree)
    expect(loader).toHaveBeenCalledTimes(2)
    toggle(tree, 'C'); await settled(tree)
    expect(loader).toHaveBeenCalledTimes(3)
  })

  it('bounds retained branch rows, reloads earlier pages and prunes branches outside the root window', async () => {
    const loader = vi.fn(async (_path: unknown[], cursor: string | null) => {
      const start = Number(cursor ?? 0)
      return page(Array.from({ length: 100 }, (_, i) => detail(String(start + i))), String(start + 100))
    })
    const tree = setup([group(['A'])], loader)
    toggle(tree, 'A'); await settled(tree)
    const id = tree.sheet!.rows[0]!.group!.id
    for (let i = 0; i < 7; i++) { tree.act(id, 'next'); await settled(tree) }
    expect(labels(tree)).toHaveLength(501)
    expect(labels(tree)[1]).toBe('300')
    tree.act(id, 'previous'); await settled(tree)
    expect(labels(tree)[1]).toBe('200')
    tree.setRoot(page([group(['B'])]).sheet)
    tree.setRoot(page([group(['A'])]).sheet)
    toggle(tree, 'A'); await settled(tree)
    expect(labels(tree)[1]).toBe('0')
  })

  it('evicts least recently used branch data under the shared memory budget', async () => {
    const loader = vi.fn(async () => page(Array.from({ length: 100 }, () => detail('x'.repeat(12_000)))))
    const tree = setup([group(['A']), group(['B']), group(['C']), group(['D'])], loader)
    for (const label of ['A', 'B', 'C', 'D']) { toggle(tree, label); await settled(tree) }
    expect(tree.sheet!.rows.find(row => row.cells[0]?.display === 'A')?.group?.expanded).toBe(false)
    expect(tree.sheet!.rows.filter(row => !row.control)).toHaveLength(304)
  })

  it('rejects oversized or incompatible pages and nonadvancing cursors without corrupting the tree', async () => {
    const loader = vi.fn().mockResolvedValueOnce(page([detail('one')], 'same'))
      .mockResolvedValueOnce(page([detail('duplicate')], 'same'))
    const tree = setup([group(['A'])], loader)
    toggle(tree, 'A'); await settled(tree)
    const id = tree.sheet!.rows[0]!.group!.id
    tree.act(id, 'next'); await settled(tree)
    expect(labels(tree)).toEqual(['A', 'one'])
    expect(tree.sheet!.rows.find(row => row.control?.error)?.control?.text).toContain('did not advance')
    loader.mockResolvedValueOnce({ ...page([detail('wrong')]), sheet: { columns: [], rows: [] } })
    tree.act(id, 'retry'); await settled(tree)
    expect(labels(tree)).toEqual(['A', 'one'])
    expect(tree.sheet!.rows.find(row => row.control?.error)?.control?.text).toContain('columns changed')
    loader.mockResolvedValueOnce(page([detail('x'.repeat(REPORT_GROUP_LIMITS.cachedBytes))]))
    tree.act(id, 'retry'); await settled(tree)
    expect(labels(tree)).toEqual(['A', 'one'])
    expect(tree.sheet!.rows.find(row => row.control?.error)?.control?.text).toContain('memory limit')
  })

  it('collapses materialized outlines without fetching and supports initially collapsed groups', () => {
    const loader = vi.fn()
    const tree = setup([
      { ...detail('local'), rowKind: ReportRowKind.Group, isExpanded: false },
      { ...detail('nested'), rowKind: ReportRowKind.Group, outlineLevel: 1 },
      { ...detail('leaf'), outlineLevel: 2 }, detail('sibling'),
    ], loader)
    expect(labels(tree)).toEqual(['local', 'sibling'])
    toggle(tree, 'local')
    expect(labels(tree)).toEqual(['local', 'nested', 'leaf', 'sibling'])
    toggle(tree, 'nested')
    expect(labels(tree)).toEqual(['local', 'nested', 'sibling'])
    expect(loader).not.toHaveBeenCalled()
  })
})
