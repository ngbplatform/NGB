import { computed, reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Router } from 'vue-router'

const pushCleanRouteQueryMock = vi.hoisted(() => vi.fn().mockResolvedValue(undefined))
const replaceCleanRouteQueryMock = vi.hoisted(() => vi.fn().mockResolvedValue(undefined))

vi.mock('../../../../src/ngb/router/queryParams', async () => {
  const actual = await vi.importActual('../../../../src/ngb/router/queryParams')
  return {
    ...actual,
    pushCleanRouteQuery: pushCleanRouteQueryMock,
    replaceCleanRouteQuery: replaceCleanRouteQueryMock,
  }
})

import { useRouteQueryEditorDrawer } from '../../../../src/ngb/editor/useRouteQueryEditorDrawer'

function createHarness(initialQuery: Record<string, unknown> = {}, options: Partial<Parameters<typeof useRouteQueryEditorDrawer>[0]> = {}) {
  const route = reactive({
    path: '/catalogs/pm.property',
    query: { ...initialQuery } as Record<string, unknown>,
  })
  const router = {
    push: vi.fn(),
    replace: vi.fn(),
  } as unknown as Router
  const onBeforeOpen = vi.fn().mockResolvedValue(true)

  const drawer = useRouteQueryEditorDrawer({
    route: route as never,
    router,
    clearKeys: ['copyDraft'],
    onBeforeOpen,
    ...options,
  })

  return {
    route,
    router,
    onBeforeOpen,
    drawer,
  }
}

describe('route query editor drawer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('derives drawer state from query params, including id-implies-edit mode', () => {
    const edit = createHarness({ panel: 'edit', id: 'property-1' })
    expect(edit.drawer.panelMode.value).toBe('edit')
    expect(edit.drawer.currentId.value).toBe('property-1')
    expect(edit.drawer.isPanelOpen.value).toBe(true)

    const implied = createHarness({ id: 'property-2' }, { idImpliesEdit: true })
    expect(implied.drawer.panelMode.value).toBe('edit')
    expect(implied.drawer.currentId.value).toBe('property-2')
  })

  it('opens create and edit drawers only when allowed and commits clean query patches', async () => {
    const onCommit = vi.fn()
    const harness = createHarness()

    expect(await harness.drawer.openCreateDrawer({ patch: { tab: 'details' }, onCommit })).toBe(true)
    expect(harness.onBeforeOpen).toHaveBeenCalledWith(
      { mode: 'new', id: null },
      { mode: null, id: null },
    )
    expect(onCommit).toHaveBeenCalledTimes(1)
    expect(pushCleanRouteQueryMock).toHaveBeenCalledWith(harness.route, harness.router, {
      copyDraft: undefined,
      panel: 'new',
      id: undefined,
      tab: 'details',
    })

    expect(await harness.drawer.openEditDrawer(' property-9 ', { patch: { source: 'grid' } })).toBe(true)
    expect(pushCleanRouteQueryMock).toHaveBeenLastCalledWith(harness.route, harness.router, {
      copyDraft: undefined,
      panel: 'edit',
      id: 'property-9',
      source: 'grid',
    })

    expect(await harness.drawer.openEditDrawer('   ')).toBe(false)
  })

  it('blocks open requests when onBeforeOpen rejects and closes by replacing query state', async () => {
    const onCommit = vi.fn()
    const harness = createHarness({ panel: 'new' })
    harness.onBeforeOpen.mockResolvedValueOnce(false)

    expect(await harness.drawer.openCreateDrawer()).toBe(false)
    expect(pushCleanRouteQueryMock).not.toHaveBeenCalled()

    await harness.drawer.closeDrawer({ patch: { keep: '1' }, onCommit })

    expect(onCommit).toHaveBeenCalledTimes(1)
    expect(replaceCleanRouteQueryMock).toHaveBeenCalledWith(harness.route, harness.router, {
      copyDraft: undefined,
      panel: undefined,
      id: undefined,
      keep: '1',
    })
  })
})
