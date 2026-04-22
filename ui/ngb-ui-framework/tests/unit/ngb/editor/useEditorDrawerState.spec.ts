import { describe, expect, it } from 'vitest'

import { useEditorDrawerState } from '../../../../src/ngb/editor/useEditorDrawerState'

describe('editor drawer state', () => {
  it('tracks heading and flag updates and can reset the heading', () => {
    const drawer = useEditorDrawerState()

    drawer.handleEditorState({
      title: 'Invoice INV-001',
      subtitle: 'Draft',
    })
    drawer.handleEditorFlags({
      canSave: true,
      isDirty: true,
      loading: false,
      saving: false,
      canExpand: true,
      canDelete: false,
      canMarkForDeletion: true,
      canUnmarkForDeletion: false,
      canPost: true,
      canUnpost: false,
      canShowAudit: true,
      canShareLink: true,
      extras: {
        hasPreview: true,
      },
    })

    expect(drawer.drawerTitle.value).toBe('Invoice INV-001')
    expect(drawer.drawerSubtitle.value).toBe('Draft')
    expect(drawer.editorFlags.value).toMatchObject({
      canSave: true,
      isDirty: true,
      canExpand: true,
      extras: {
        hasPreview: true,
      },
    })

    drawer.resetDrawerHeading()
    expect(drawer.drawerTitle.value).toBe('')
    expect(drawer.drawerSubtitle.value).toBeUndefined()
  })

  it('resolves discard requests through confirm and cancel flows', async () => {
    const drawer = useEditorDrawerState()

    const confirmPromise = drawer.requestDiscard()
    expect(drawer.discardOpen.value).toBe(true)
    drawer.discardConfirm()
    await expect(confirmPromise).resolves.toBe(true)
    expect(drawer.discardOpen.value).toBe(false)

    const cancelPromise = drawer.requestDiscard()
    expect(drawer.discardOpen.value).toBe(true)
    drawer.discardCancel()
    await expect(cancelPromise).resolves.toBe(false)
    expect(drawer.discardOpen.value).toBe(false)
  })

  it('asks for discard only when the editor is dirty before closing', async () => {
    const drawer = useEditorDrawerState()

    await expect(drawer.beforeCloseDrawer()).resolves.toBe(true)

    drawer.handleEditorFlags({
      ...drawer.editorFlags.value,
      isDirty: true,
    })

    const beforeClose = drawer.beforeCloseDrawer()
    expect(drawer.discardOpen.value).toBe(true)
    drawer.discardCancel()
    await expect(beforeClose).resolves.toBe(false)
  })
})
