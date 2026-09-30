import { computed, nextTick, ref } from 'vue'
import { describe, expect, it } from 'vitest'

import {
  inferRegisterRowStatus,
  useRegisterRows,
} from '../../../../../src/ngb/components/register/useRegisterRows'
import type {
  RegisterDataRow,
  RegisterSortSpec,
} from '../../../../../src/ngb/components/register/registerTypes'

async function flushAsync() {
  await nextTick()
  await Promise.resolve()
}

function createHarness(options?: {
  rows?: RegisterDataRow[]
  groupBy?: string[]
  sortBy?: RegisterSortSpec[]
  defaultExpanded?: boolean
}) {
  const rows = ref<RegisterDataRow[]>(options?.rows ?? [])
  const groupBy = ref<string[]>(options?.groupBy ?? [])
  const sortBy = ref<RegisterSortSpec[]>(options?.sortBy ?? [])
  const defaultExpanded = ref(options?.defaultExpanded ?? false)

  const registerRows = useRegisterRows({
    rows: computed(() => rows.value),
    groupBy: computed(() => groupBy.value),
    sortBy: computed(() => sortBy.value),
    defaultExpanded: computed(() => defaultExpanded.value),
  })

  return {
    rows,
    groupBy,
    sortBy,
    defaultExpanded,
    registerRows,
  }
}

describe('register rows', () => {
  it('groups sorted rows, assigns data indices, and computes totals', async () => {
    const rows: RegisterDataRow[] = [
      {
        key: 'row-1',
        property: 'Riverfront',
        number: 'INV-001',
        debit: 10,
        credit: 0,
        status: 2,
      },
      {
        key: 'row-2',
        property: 'Riverfront',
        number: 'INV-002',
        debit: 0,
        credit: 5,
        isMarkedForDeletion: true,
      },
      {
        key: 'row-3',
        property: 'Harbor',
        number: 'INV-003',
        debit: 20,
        credit: 1,
        isActive: false,
      },
    ]

    const { registerRows } = createHarness({
      rows,
      groupBy: ['property'],
      sortBy: [{ key: 'debit', dir: 'desc' }],
      defaultExpanded: true,
    })

    await flushAsync()

    expect(registerRows.hasGroups.value).toBe(true)
    expect(registerRows.allGroupIds.value).toEqual(['property:Harbor', 'property:Riverfront'])
    expect(registerRows.allGroupsExpanded.value).toBe(true)
    expect(registerRows.displayRows.value.map((row) => row.type === 'group' ? `group:${row.label}` : `row:${row.key}:${row.__index}`))
      .toEqual([
        'group:Harbor',
        'row:row-3:0',
        'group:Riverfront',
        'row:row-1:1',
        'row:row-2:2',
      ])
    expect(registerRows.totals.value).toEqual({
      debit: 30,
      credit: 6,
    })
    expect(registerRows.dataRows.value.map((row) => row.key)).toEqual(['row-3', 'row-1', 'row-2'])
  })

  it('toggles grouped visibility and infers row statuses from explicit and derived flags', async () => {
    const rows: RegisterDataRow[] = [
      { key: 'row-1', property: 'Riverfront', status: 2 },
      { key: 'row-2', property: 'Riverfront', isMarkedForDeletion: true },
      { key: 'row-3', property: 'Harbor', __status: 'active' },
    ]

    const { registerRows } = createHarness({
      rows,
      groupBy: ['property'],
      defaultExpanded: false,
    })

    await flushAsync()

    expect(registerRows.displayRows.value.map((row) => row.type)).toEqual(['group', 'group'])

    registerRows.toggleAllGroups()
    await flushAsync()
    expect(registerRows.allGroupsExpanded.value).toBe(true)
    expect(registerRows.dataRows.value.map((row) => row.key)).toEqual(['row-1', 'row-2', 'row-3'])

    registerRows.toggleGroup('property:Riverfront')
    await flushAsync()
    expect(registerRows.allGroupsExpanded.value).toBe(false)
    expect(registerRows.dataRows.value.map((row) => row.key)).toEqual(['row-3'])

    expect(registerRows.isGroupExpanded('property:Riverfront')).toBe(false)
    registerRows.toggleGroup('property:Riverfront')
    expect(registerRows.isGroupExpanded('property:Riverfront')).toBe(true)
    registerRows.toggleAllGroups()
    expect(registerRows.allGroupsExpanded.value).toBe(false)

    expect(inferRegisterRowStatus(rows[0]!)).toBe('posted')
    expect(inferRegisterRowStatus(rows[1]!)).toBe('marked')
    expect(inferRegisterRowStatus(rows[2]!)).toBe('active')
    expect(inferRegisterRowStatus({ key: 'row-4', isActive: false })).toBe('saved')
    expect([
      inferRegisterRowStatus({ key: 'explicit-saved', __status: 'saved' }),
      inferRegisterRowStatus({ key: 'explicit-posted', __status: 'posted' }),
      inferRegisterRowStatus({ key: 'explicit-marked', __status: 'marked' }),
      inferRegisterRowStatus({ key: 'deleted', isDeleted: true }),
      inferRegisterRowStatus({ key: 'status-marked', status: 3 }),
      inferRegisterRowStatus({ key: 'status-saved', status: 1 }),
      inferRegisterRowStatus({ key: 'active', isActive: true }),
      inferRegisterRowStatus({ key: 'default' }),
    ]).toEqual(['saved', 'posted', 'marked', 'marked', 'marked', 'saved', 'active', 'active'])
  })

  it('sorts null, equal, ascending, and descending values and supports ungrouped rows', async () => {
    async function sortedKeys(values: Array<number | null>, dir: 'asc' | 'desc') {
      const { registerRows } = createHarness({
        rows: values.map((value, index) => ({ key: `row-${index}`, amount: value })),
        sortBy: [{ key: 'amount', dir }],
      })
      await flushAsync()
      return registerRows.displayRows.value.map((row) => row.key)
    }

    expect(await sortedKeys([2, null], 'asc')).toEqual(['row-1', 'row-0'])
    expect(await sortedKeys([2, null], 'desc')).toEqual(['row-0', 'row-1'])
    expect(await sortedKeys([null, 2], 'asc')).toEqual(['row-0', 'row-1'])
    expect(await sortedKeys([null, 2], 'desc')).toEqual(['row-1', 'row-0'])
    expect(await sortedKeys([1, 2], 'asc')).toEqual(['row-0', 'row-1'])
    expect(await sortedKeys([2, 1], 'desc')).toEqual(['row-0', 'row-1'])
    expect(await sortedKeys([1, 1], 'asc')).toEqual(['row-0', 'row-1'])

    const empty = createHarness({ defaultExpanded: true })
    await flushAsync()
    expect(empty.registerRows.hasGroups.value).toBe(false)
    expect(empty.registerRows.allGroupIds.value).toEqual([])
    expect(empty.registerRows.allGroupsExpanded.value).toBe(false)
    empty.registerRows.toggleAllGroups()
    expect(empty.registerRows.displayRows.value).toEqual([])

    const emptyGroup = createHarness({ groupBy: ['property'], defaultExpanded: true })
    await flushAsync()
    emptyGroup.registerRows.toggleAllGroups()
    expect(emptyGroup.registerRows.allGroupIds.value).toEqual([])
  })

  it('builds nested groups for blank values and defaults missing totals to zero', async () => {
    const { registerRows } = createHarness({
      rows: [
        { key: 'row-1', category: null, region: '', debit: undefined, credit: undefined },
        { key: 'row-2', category: null, region: 'North', debit: 5, credit: 2 },
      ],
      groupBy: ['category', 'region'],
      defaultExpanded: true,
    })
    await flushAsync()

    expect(registerRows.allGroupIds.value).toEqual([
      'category:—',
      'category:—|region:—',
      'category:—|region:North',
    ])
    expect(registerRows.displayRows.value.filter((row) => row.type === 'row')).toHaveLength(2)
    expect(registerRows.totals.value).toEqual({ debit: 5, credit: 2 })
  })
})
