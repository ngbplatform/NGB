import { describe, expect, it, vi } from 'vitest'

import { useEntityEditorCommitHandlers } from '../../../../src/ngb/editor/useEntityEditorCommitHandlers'

describe('entity editor commit handlers', () => {
  it('passes created context through reload and only closes when the decision allows it', async () => {
    const reload = vi.fn().mockResolvedValue(false)
    const closeDrawer = vi.fn()
    const onCreated = vi.fn()

    const handlers = useEntityEditorCommitHandlers({
      reload,
      closeDrawer,
      onCreated,
      closeOnCreated: (context) => context.reloadSucceeded,
    })

    await handlers.handleCreated('doc-1')

    expect(reload).toHaveBeenCalledTimes(1)
    expect(onCreated).toHaveBeenCalledWith({
      id: 'doc-1',
      reloadSucceeded: false,
    })
    expect(closeDrawer).not.toHaveBeenCalled()
  })

  it('supports explicit close decisions for saved and changed commits', async () => {
    const reload = vi.fn().mockResolvedValue(true)
    const closeDrawer = vi.fn()
    const onSaved = vi.fn()
    const onChanged = vi.fn()

    const handlers = useEntityEditorCommitHandlers({
      reload,
      closeDrawer,
      onSaved,
      onChanged,
      closeOnSaved: false,
      closeOnChanged: (context) => context.reason === 'post',
    })

    await handlers.handleSaved()
    await handlers.handleChanged('post')

    expect(onSaved).toHaveBeenCalledWith({
      reloadSucceeded: true,
    })
    expect(onChanged).toHaveBeenCalledWith({
      reason: 'post',
      reloadSucceeded: true,
    })
    expect(closeDrawer).toHaveBeenCalledTimes(1)
  })

  it('closes after delete by default and reports reload status to callbacks', async () => {
    const reload = vi.fn().mockResolvedValue(undefined)
    const closeDrawer = vi.fn()
    const onDeleted = vi.fn()

    const handlers = useEntityEditorCommitHandlers({
      reload,
      closeDrawer,
      onDeleted,
    })

    await handlers.handleDeleted()

    expect(onDeleted).toHaveBeenCalledWith({
      reloadSucceeded: true,
    })
    expect(closeDrawer).toHaveBeenCalledTimes(1)
  })
})
