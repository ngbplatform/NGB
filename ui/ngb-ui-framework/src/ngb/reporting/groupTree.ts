import { stableStringify } from '../utils/stableValue'
import { toErrorMessage } from '../utils/errorMessage'
import { ReportPageWindow } from './pageWindow'
import { areSheetsAppendCompatible } from './paging'
import { ReportRowKind, type ReportExecutionResponseDto, type ReportSheetDto, type ReportSheetRowDto } from './types'

export type GroupAction = 'toggle' | 'next' | 'previous' | 'restart' | 'retry'
export type ReportDisplayRow = ReportSheetRowDto & {
  viewKey: string
  group?: { id: string; expanded: boolean }
  control?: { id: string; action?: GroupAction; text: string; autoLoad?: boolean; error?: boolean }
}
export type ReportDisplaySheet = Omit<ReportSheetDto, 'rows'> & { rows: ReportDisplayRow[] }
type Loader = (path: unknown[], cursor: string | null, signal: AbortSignal) => Promise<ReportExecutionResponseDto>
type Direction = 'first' | 'next' | 'previous'
type Branch = {
  id: string
  path: unknown[]
  expanded: boolean
  window: ReportPageWindow
  controller?: AbortController
  error?: string
  direction: Direction
  touched: number
}

export const REPORT_GROUP_LIMITS = {
  pageRows: 100,
  branchRows: 500,
  cachedRows: 2_000,
  cachedBytes: 8 * 1024 * 1024,
  branches: 64,
  concurrentRequests: 2,
  bookmarks: 32,
} as const

/** Client-only tree state. Network pages and their cursors stay scoped to one executed request. */
export class ReportGroupTree {
  private root: ReportSheetDto | null = null
  private loader: Loader | null = null
  private branches = new Map<string, Branch>()
  private projections = new WeakMap<ReportSheetRowDto, ReportDisplayRow>()
  private localExpanded = new WeakMap<ReportSheetRowDto, boolean>()
  private localGroups = new Map<string, ReportSheetRowDto>()
  private controls = new Map<string, ReportDisplayRow>()
  private queue: { branch: Branch; controller: AbortController }[] = []
  private active = 0
  private generation = 0
  private serial = 0
  private clock = 0

  constructor(private readonly changed: () => void) {}

  reset(sheet: ReportSheetDto | null = null, loader: Loader | null = null) {
    this.generation++
    this.branches.forEach(branch => branch.controller?.abort())
    this.branches.clear()
    this.queue = []
    this.controls.clear()
    this.localGroups.clear()
    this.root = sheet
    this.loader = loader
    this.changed()
  }

  setRoot(sheet: ReportSheetDto) {
    this.root = sheet
    this.prune()
    this.changed()
  }

  private project(row: ReportSheetRowDto, depth: number): ReportDisplayRow {
    let view = this.projections.get(row)
    if (!view) {
      view = { ...row, viewKey: `row:${++this.serial}` }
      this.projections.set(row, view)
    }
    view.outlineLevel = (row.outlineLevel ?? 0) + depth
    view.group = undefined
    return view
  }

  private control(branch: Branch, depth: number, suffix: string, text: string, action?: GroupAction, autoLoad = false): ReportDisplayRow {
    const key = `${branch.id}:${suffix}`
    let row = this.controls.get(key)
    if (!row) {
      row = { viewKey: key, rowKind: ReportRowKind.Detail, cells: [] }
      this.controls.set(key, row)
    }
    row.outlineLevel = depth
    row.control = { id: branch.id, action, text, autoLoad, error: !!branch.error }
    return row
  }

  get sheet(): ReportDisplaySheet | null {
    if (!this.root) return null
    this.localGroups.clear()
    const rows: ReportDisplayRow[] = []
    const append = (source: ReportSheetRowDto[], depth: number) => {
      let hiddenBelow: number | null = null
      for (let index = 0; index < source.length; index++) {
        const row = source[index]!
        const level = row.outlineLevel ?? 0
        if (hiddenBelow != null && level > hiddenBelow) continue
        hiddenBelow = null
        const view = this.project(row, depth)
        rows.push(view)
        if (row.childrenPath?.length) {
          const id = stableStringify(row.childrenPath)
          const branch = this.branches.get(id)
          view.group = { id, expanded: branch?.expanded ?? false }
          if (!branch?.expanded) continue
          const childDepth = (view.outlineLevel ?? 0) + 1
          const page = branch.window.response
          if (branch.window.canLoadPrevious)
            rows.push(this.control(branch, childDepth, 'previous', 'Load previous rows', 'previous'))
          else if (branch.window.historyTruncated)
            rows.push(this.control(branch, childDepth, 'previous', 'Back to group beginning', 'restart'))
          if (page) append(page.sheet.rows, childDepth)
          if (branch.controller)
            rows.push(this.control(branch, childDepth, 'status', 'Loading group…'))
          else if (branch.error)
            rows.push(this.control(branch, childDepth, 'status', branch.error, 'retry'))
          else if (!page)
            rows.push(this.control(branch, childDepth, 'status', 'Reload group', 'restart'))
          else if (page.hasMore)
            rows.push(this.control(branch, childDepth, 'status', 'Load more in group', 'next', true))
          else if (page.sheet.rows.length === 0)
            rows.push(this.control(branch, childDepth, 'status', 'No rows in this group.'))
        } else if (row.rowKind === ReportRowKind.Group && (source[index + 1]?.outlineLevel ?? 0) > level) {
          const expanded = this.localExpanded.get(row) ?? row.isExpanded !== false
          view.group = { id: view.viewKey, expanded }
          this.localGroups.set(view.viewKey, row)
          if (!expanded) hiddenBelow = level
        }
      }
    }
    append(this.root.rows, 0)
    return { ...this.root, rows }
  }

  act(id: string, action: GroupAction) {
    // An observer notification may already be queued when its group is collapsed.
    if (action !== 'toggle' && !this.sheet?.rows.some(row => row.group?.id === id && row.group.expanded)) return
    const local = this.localGroups.get(id)
    if (local) {
      this.localExpanded.set(local, !(this.localExpanded.get(local) ?? local.isExpanded !== false))
      this.changed()
      return
    }
    let branch = this.branches.get(id)
    if (!branch) {
      // Accept only a currently visible group's server-issued path.
      const row = this.sheet?.rows.find(row => row.group?.id === id)
      if (!row?.childrenPath) return
      branch = { id, path: row.childrenPath, expanded: false, window: this.newWindow(), direction: 'first', touched: ++this.clock }
      this.branches.set(id, branch)
    }
    branch.touched = ++this.clock
    if (action === 'toggle') {
      branch.expanded = !branch.expanded
      if (!branch.expanded) {
        for (const child of this.branches.values()) {
          if (this.isDescendant(child.path, branch.path)) {
            this.cancel(child)
          }
        }
        this.changed()
        return
      }
      if (branch.window.response) { this.changed(); return }
    }
    if (branch.controller) return
    if (action === 'next' && !branch.window.response?.nextCursor) return
    if (action === 'previous' && !branch.window.canLoadPrevious) return
    branch.direction = action === 'retry' ? branch.direction : action === 'next' ? 'next' : action === 'previous' ? 'previous' : 'first'
    branch.error = undefined
    branch.controller = new AbortController()
    this.queue.push({ branch, controller: branch.controller })
    this.trim(branch)
    this.changed()
    this.pump()
  }

  private newWindow() {
    return new ReportPageWindow(REPORT_GROUP_LIMITS.branchRows, REPORT_GROUP_LIMITS.cachedBytes, REPORT_GROUP_LIMITS.bookmarks)
  }

  private isDescendant(path: unknown[], parent: unknown[]) {
    return path.length >= parent.length && stableStringify(path.slice(0, parent.length)) === stableStringify(parent)
  }

  private cancel(branch: Branch) {
    branch.controller?.abort()
    branch.controller = undefined
    this.queue = this.queue.filter(entry => entry.branch !== branch)
  }

  private drop(branch: Branch) {
    this.cancel(branch)
    this.branches.delete(branch.id)
    for (const key of this.controls.keys()) if (key.startsWith(`${branch.id}:`)) this.controls.delete(key)
  }

  private prune() {
    const reachable = new Set<string>()
    const visit = (rows: ReportSheetRowDto[]) => {
      for (const row of rows) {
        if (!row.childrenPath) continue
        const id = stableStringify(row.childrenPath)
        reachable.add(id)
        const child = this.branches.get(id)?.window.response
        if (child) visit(child.sheet.rows)
      }
    }
    visit(this.root?.rows ?? [])
    for (const branch of this.branches.values()) if (!reachable.has(branch.id)) this.drop(branch)
  }

  private trim(protectedBranch: Branch) {
    const overBudget = () => {
      let rows = 0, bytes = 0
      for (const branch of this.branches.values()) { rows += branch.window.rowCount; bytes += branch.window.byteSize }
      return rows > REPORT_GROUP_LIMITS.cachedRows || bytes > REPORT_GROUP_LIMITS.cachedBytes || this.branches.size > REPORT_GROUP_LIMITS.branches
    }
    const candidates = [...this.branches.values()]
      .filter(branch => !this.isDescendant(protectedBranch.path, branch.path))
      .sort((a, b) => Number(a.expanded) - Number(b.expanded) || a.touched - b.touched)
    for (const branch of candidates) {
      if (!overBudget()) break
      this.drop(branch)
      this.prune()
    }
    return !overBudget()
  }

  private pump() {
    while (this.active < REPORT_GROUP_LIMITS.concurrentRequests && this.queue.length) {
      const { branch, controller } = this.queue.shift()!
      if (controller !== branch.controller || controller.signal.aborted || this.branches.get(branch.id) !== branch || !this.loader) continue
      this.active++
      const generation = this.generation
      const cursor = branch.direction === 'next' ? branch.window.response?.nextCursor ?? null
        : branch.direction === 'previous' ? branch.window.previousCursor : null
      const loader = this.loader
      void (async () => {
        try {
          const result = await loader(branch.path, cursor, controller.signal)
          if (generation !== this.generation || controller.signal.aborted || branch.controller !== controller) return
          if (!this.root || !areSheetsAppendCompatible(this.root, result.sheet)) throw new Error('The report columns changed. Run the report again.')
          // Parent group already contains its totals. A branch grand total would duplicate them.
          const page = { ...result, sheet: { ...result.sheet, rows: result.sheet.rows.filter(row => row.rowKind !== ReportRowKind.Total && row.semanticRole !== 'grand_total') } }
          if (page.hasMore && (!page.nextCursor || page.nextCursor === cursor)) throw new Error('The group cursor did not advance.')
          const previous = branch.window
          const window = branch.direction === 'first' ? this.newWindow() : branch.window.clone()
          if (branch.direction === 'next') window.append(cursor!, page)
          else if (branch.direction === 'previous') window.prepend(page)
          else window.reset(page)
          branch.window = window
          if (!this.trim(branch)) {
            branch.window = previous
            throw new Error('This group exceeds the report memory limit. Narrow the report filters.')
          }
          this.prune()
        } catch (err) {
          if (generation === this.generation && !controller.signal.aborted)
            branch.error = toErrorMessage(err, 'Failed to load group. Retry to continue.')
        } finally {
          this.active--
          if (branch.controller === controller) branch.controller = undefined
          if (generation === this.generation) this.changed()
          this.pump()
        }
      })()
    }
  }
}
