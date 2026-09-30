import { computed, nextTick, ref } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const loadJsonMock = vi.hoisted(() => vi.fn())
const saveJsonMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../../src/ngb/utils/storage', () => ({
  loadJson: loadJsonMock,
  saveJson: saveJsonMock,
}))

import { useRegisterColumnState } from '../../../../../src/ngb/components/register/useRegisterColumnState'
import type { RegisterColumn } from '../../../../../src/ngb/components/register/registerTypes'

async function flushAsync() {
  await nextTick()
  await Promise.resolve()
}

function createHarness(options?: {
  columns?: RegisterColumn[]
  visibleColumnKeys?: string[]
  storageKey?: string
  showStatusColumn?: boolean
  statusColWidth?: number
}) {
  const columns = ref<RegisterColumn[]>(options?.columns ?? [])
  const visibleColumnKeys = ref<string[] | undefined>(options?.visibleColumnKeys)
  const storageKey = ref<string | undefined>(options?.storageKey)
  const showStatusColumn = ref(options?.showStatusColumn ?? true)
  const statusColWidth = ref(options?.statusColWidth ?? 40)
  const emittedVisible: string[][] = []

  const state = useRegisterColumnState({
    columns: computed(() => columns.value),
    visibleColumnKeys: computed(() => visibleColumnKeys.value),
    storageKey: computed(() => storageKey.value),
    showStatusColumn: computed(() => showStatusColumn.value),
    statusColWidth: computed(() => statusColWidth.value),
    emitVisibleColumnKeys: (value) => {
      emittedVisible.push([...value])
    },
  })

  return {
    columns,
    visibleColumnKeys,
    storageKey,
    showStatusColumn,
    statusColWidth,
    emittedVisible,
    state,
  }
}

describe('register column state', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.clearAllMocks()
    loadJsonMock.mockReturnValue(null)
  })

  afterEach(() => {
    vi.runOnlyPendingTimers()
    vi.useRealTimers()
  })

  it('hydrates persisted order and widths, computes sticky layout, and persists later edits', async () => {
    loadJsonMock.mockReturnValue({
      order: ['name', 'amount'],
      widths: {
        name: 180,
        amount: 220,
      },
    })

    const columns: RegisterColumn[] = [
      {
        key: 'name',
        title: 'Name',
        width: 150,
        minWidth: 120,
        pinned: 'left',
      },
      {
        key: 'amount',
        title: 'Amount',
        width: 140,
        minWidth: 100,
        align: 'right',
      },
    ]

    const { state } = createHarness({
      columns,
      visibleColumnKeys: ['name', 'amount'],
      storageKey: 'register:test',
    })

    await flushAsync()

    expect(loadJsonMock).toHaveBeenCalledWith('register:test', null)
    expect(state.localOrder.value).toEqual(['name', 'amount'])
    expect(state.localWidths.value).toEqual({
      name: 180,
      amount: 220,
    })
    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['name', 'amount'])
    expect(state.gridTemplateColumns.value).toBe('40px 180px 220px')
    expect(state.colWidth(columns[0]!)).toBe(180)
    expect(state.stickyStyle(columns[0]!)).toMatchObject({
      position: 'sticky',
      left: '40px',
      zIndex: 2,
    })
    expect(state.cellStickyStyle(columns[0]!)).toMatchObject({
      position: 'sticky',
      left: '40px',
      zIndex: 1,
    })

    state.localOrder.value = ['amount', 'name']
    state.localWidths.value = {
      name: 200,
      amount: 260,
    }
    await flushAsync()
    vi.advanceTimersByTime(150)

    expect(saveJsonMock).toHaveBeenLastCalledWith('register:test', {
      order: ['amount', 'name'],
      widths: {
        name: 200,
        amount: 260,
      },
      visible: ['name', 'amount'],
    })
  })

  it('expands accidental single-column local order into the full column set once columns are ready', async () => {
    loadJsonMock.mockReturnValue({
      order: ['display'],
    })

    const { columns, emittedVisible, state } = createHarness({
      columns: [
        {
          key: 'display',
          title: 'Display',
        },
      ],
      storageKey: 'register:partial',
    })

    await flushAsync()

    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['display'])
    expect(state.localOrder.value).toEqual(['display'])

    columns.value = [
      {
        key: 'display',
        title: 'Display',
      },
      {
        key: 'amount',
        title: 'Amount',
      },
    ]
    await flushAsync()

    expect(state.localOrder.value).toEqual(['display', 'amount'])
    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['display', 'amount'])

    state.setVisible(['amount'])
    await flushAsync()

    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['amount'])
    expect(emittedVisible).toEqual([['amount']])
  })

  it('rehydrates fresh local state when the storage key changes', async () => {
    loadJsonMock.mockImplementation((key: string) => {
      if (key === 'register:a') {
        return {
          order: ['name', 'amount'],
          widths: { name: 150 },
          visible: ['name'],
        }
      }

      return {
        order: ['amount', 'name'],
        widths: { amount: 320 },
        visible: ['amount'],
      }
    })

    const { state, storageKey } = createHarness({
      columns: [
        {
          key: 'name',
          title: 'Name',
        },
        {
          key: 'amount',
          title: 'Amount',
        },
      ],
      storageKey: 'register:a',
    })

    await flushAsync()
    expect(state.localOrder.value).toEqual(['name', 'amount'])
    expect(state.localWidths.value).toEqual({ name: 150 })
    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['name'])

    storageKey.value = 'register:b'
    await flushAsync()

    expect(state.localOrder.value).toEqual(['amount', 'name'])
    expect(state.localWidths.value).toEqual({ amount: 320 })
    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['amount'])
  })

  it('hydrates legacy array storage as visible columns for backward compatibility', async () => {
    loadJsonMock.mockReturnValue(['amount'])

    const { state } = createHarness({
      columns: [
        {
          key: 'name',
          title: 'Name',
        },
        {
          key: 'amount',
          title: 'Amount',
        },
      ],
      storageKey: 'register:legacy-visible',
    })

    await flushAsync()

    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['amount'])
    expect(state.localOrder.value).toEqual(['name', 'amount'])
  })

  it('recovers accidental display-only visibility after partial columns finish loading', async () => {
    const { columns, state } = createHarness({
      columns: [{ key: 'display', title: 'Display' }],
      visibleColumnKeys: ['display'],
      storageKey: 'register:deferred',
    })

    await flushAsync()
    state.localOrder.value = ['display']

    columns.value = [
      { key: 'display', title: 'Display' },
      { key: 'amount', title: 'Amount' },
    ]
    await flushAsync()

    expect(state.localOrder.value).toEqual(['display', 'amount'])
    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['display'])

    const alreadyLoaded = createHarness({
      columns: [
        { key: 'display', title: 'Display' },
        { key: 'amount', title: 'Amount' },
      ],
      visibleColumnKeys: ['display'],
    })
    await flushAsync()
    expect(alreadyLoaded.state.visibleColumns.value.map((column) => column.key)).toEqual(['display'])
  })

  it('handles missing persistence, stale order keys, default widths, and non-sticky columns', async () => {
    const columns: RegisterColumn[] = [
      { key: 'plain', title: 'Plain' },
      { key: 'narrow', title: 'Narrow', width: 50, minWidth: 80 },
      { key: 'pinned', title: 'Pinned', pinned: 'left', width: 100 },
    ]
    const { state, visibleColumnKeys, showStatusColumn } = createHarness({
      columns,
      showStatusColumn: false,
    })

    await flushAsync()
    expect(loadJsonMock).not.toHaveBeenCalled()
    expect(state.colWidth(columns[0]!)).toBe(140)
    expect(state.colWidth(columns[1]!)).toBe(80)
    expect(state.gridTemplateColumns.value).toBe('140px 80px 100px')
    expect(state.stickyStyle(columns[0]!)).toEqual({})
    expect(state.cellStickyStyle(columns[0]!)).toEqual({})

    state.localOrder.value = ['missing', 'narrow']
    state.setVisible(['narrow'])
    await flushAsync()
    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['narrow'])
    expect(state.stickyStyle(columns[2]!)).toMatchObject({ left: '0px' })
    expect(state.cellStickyStyle(columns[2]!)).toMatchObject({ left: '0px' })

    showStatusColumn.value = true
    await flushAsync()
    expect(state.gridTemplateColumns.value).toBe('40px 80px')
    expect(state.stickyStyle(columns[2]!)).toMatchObject({ left: '40px' })
    expect(state.cellStickyStyle(columns[2]!)).toMatchObject({ left: '40px' })

    visibleColumnKeys.value = ['plain']
    await flushAsync()
    visibleColumnKeys.value = undefined
    await flushAsync()
    expect(state.visibleColumns.value.map((column) => column.key)).toEqual(['plain'])
  })

  it('ignores empty persisted payloads and appends columns missing from persisted order', async () => {
    loadJsonMock.mockReturnValueOnce(null).mockReturnValueOnce({})

    const partial = createHarness({
      columns: [{ key: 'name', title: 'Name' }],
      storageKey: 'register:empty',
    })
    await flushAsync()
    partial.state.localWidths.value = { name: 120 }
    await flushAsync()
    expect(saveJsonMock).not.toHaveBeenCalled()

    partial.columns.value = [
      { key: 'name', title: 'Name' },
      { key: 'amount', title: 'Amount' },
    ]
    await flushAsync()
    expect(partial.state.localOrder.value).toEqual(['name'])
    expect(partial.state.visibleColumns.value.map((column) => column.key)).toEqual(['name', 'amount'])

    partial.storageKey.value = 'register:empty-object'
    await flushAsync()
    partial.columns.value = [
      ...partial.columns.value,
      { key: 'currency', title: 'Currency' },
    ]
    await flushAsync()
    expect(partial.state.visibleColumns.value.map((column) => column.key)).toEqual(['name', 'amount', 'currency'])
  })
})
