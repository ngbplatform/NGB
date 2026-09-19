import type { ReportExecutionResponseDto } from './types'
import { areSheetsAppendCompatible, mergePagedReportResponses } from './paging'

/** Retains a small window of row data. Earlier pages retain only their continuation bookmarks. */
export class ReportPageWindow {
  private pages: ReportExecutionResponseDto[] = []
  private bookmarks = new Map<number, { cursor: string | null; bytes: number }>()
  private bookmarkBytes = 0
  private start = 0
  private retainedBytes = 0
  private sizes: number[] = []
  constructor(
    private readonly maxRows = 2_000,
    private readonly maxBytes = 8 * 1024 * 1024,
    private readonly maxBookmarks = 128,
    private readonly maxBookmarkBytes = 512 * 1024,
  ) {}

  reset(page: ReportExecutionResponseDto) {
    this.pages = [page]
    this.bookmarks.clear()
    this.bookmarkBytes = 0
    this.remember(0, null)
    this.start = 0
    this.sizes = [this.estimate(page)]
    this.retainedBytes = this.sizes[0]!
  }

  get canLoadPrevious() { return this.start > 0 && this.bookmarks.has(this.start - 1) }
  get historyTruncated() { return this.start > 0 && !this.canLoadPrevious }
  get retainedCursorCount() { return this.bookmarks.size }
  get retainedCursorBytes() { return this.bookmarkBytes }
  get previousCursor() { return this.bookmarks.get(this.start - 1)?.cursor ?? null }
  get rowCount() { return this.pages.reduce((n, page) => n + page.sheet.rows.length, 0) }
  get response(): ReportExecutionResponseDto | null {
    return this.pages.reduce<ReportExecutionResponseDto | null>((all, page) => all ? mergePagedReportResponses(all, page) : page, null)
  }

  append(cursor: string, page: ReportExecutionResponseDto): number {
    if (page.hasMore && page.nextCursor === cursor) throw new Error('The report cursor did not advance.')
    this.validateShape(page)
    this.remember(this.start + this.pages.length, cursor)
    this.pages.push(page)
    const size = this.estimate(page)
    this.sizes.push(size)
    this.retainedBytes += size
    let removed = 0
    while (this.overBudget()) {
      removed += this.pages.shift()!.sheet.rows.length
      this.retainedBytes -= this.sizes.shift()!
      this.start++
    }
    return removed
  }

  prepend(page: ReportExecutionResponseDto): number {
    if (!this.canLoadPrevious) throw new Error('No earlier page is retained. Run the report to return to the beginning.')
    this.validateShape(page)
    this.start--
    this.pages.unshift(page)
    const size = this.estimate(page)
    this.sizes.unshift(size)
    this.retainedBytes += size
    while (this.overBudget()) {
      this.pages.pop()
      this.retainedBytes -= this.sizes.pop()!
    }
    return page.sheet.rows.length
  }

  private remember(index: number, cursor: string | null) {
    this.bookmarkBytes -= this.bookmarks.get(index)?.bytes ?? 0
    const bytes = (cursor?.length ?? 0) * 2
    this.bookmarks.set(index, { cursor, bytes })
    this.bookmarkBytes += bytes
    while (this.bookmarks.size > this.maxBookmarks || this.bookmarkBytes > this.maxBookmarkBytes) {
      const oldest = this.bookmarks.keys().next().value!
      this.bookmarkBytes -= this.bookmarks.get(oldest)!.bytes
      this.bookmarks.delete(oldest)
    }
  }

  private validateShape(page: ReportExecutionResponseDto) {
    if (this.pages[0] && !areSheetsAppendCompatible(this.pages[0].sheet, page.sheet))
      throw new Error('The report columns changed. Run the report again.')
  }

  private overBudget() {
    return this.pages.length > 1 && (this.rowCount > this.maxRows || this.retainedBytes > this.maxBytes)
  }

  private estimate(page: ReportExecutionResponseDto): number {
    // Include values, displays and action payloads, rather than counting only rows in wide pivots.
    return JSON.stringify(page).length * 2
  }
}
