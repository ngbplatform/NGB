import { shallowRef } from 'vue'
import { expect, test, vi } from 'vitest'

import { forwardEntityEditorHandle } from '../../../../src/ngb/editor/forwardEntityEditorHandle'
import type { EntityEditorHandle } from '../../../../src/ngb/editor/types'

function createHandle() {
  return {
    save: vi.fn().mockResolvedValue(undefined),
    load: vi.fn().mockResolvedValue(undefined),
    openFullPage: vi.fn(),
    openCompactPage: vi.fn(),
    closePage: vi.fn(),
    toggleMarkForDeletion: vi.fn(),
    togglePost: vi.fn(),
    markForDeletion: vi.fn().mockResolvedValue(undefined),
    unmarkForDeletion: vi.fn().mockResolvedValue(undefined),
    deleteEntity: vi.fn().mockResolvedValue(undefined),
    post: vi.fn().mockResolvedValue(undefined),
    unpost: vi.fn().mockResolvedValue(undefined),
    getDocumentEffects: vi.fn().mockReturnValue({ version: 1 }),
    reloadDocumentEffects: vi.fn().mockResolvedValue({ version: 2 }),
    copyShareLink: vi.fn().mockResolvedValue(undefined),
    copyDocument: vi.fn(),
    printDocument: vi.fn(),
    openAuditLog: vi.fn(),
    openAudit: vi.fn(),
    closeAuditLog: vi.fn(),
    getIsDirty: vi.fn().mockReturnValue(true),
    getCanSave: vi.fn().mockReturnValue(true),
    getFlags: vi.fn().mockReturnValue({ canSave: true, isDirty: true }),
  } satisfies EntityEditorHandle<{ version: number }>
}

test.each(Object.keys(createHandle()) as (keyof EntityEditorHandle)[])(
  'forwards %s to the mounted editor, preserving its result and receiver',
  async (method) => {
    const mounted = createHandle()
    const forwarded = forwardEntityEditorHandle(shallowRef(mounted))

    const result = forwarded[method]()

    expect(mounted[method]).toHaveBeenCalledExactlyOnceWith()
    expect(mounted[method].mock.contexts[0]).toBe(mounted)
    expect(result).toBe(mounted[method].mock.results[0]!.value)
    await result
  },
)

test('resolves the current editor after mount and replacement without capturing a stale handle', async () => {
  const editor = shallowRef<EntityEditorHandle<{ version: number }> | null>(null)
  const forwarded = forwardEntityEditorHandle(editor)
  expect(() => forwarded.save()).toThrow('Entity editor is not mounted.')

  const first = createHandle()
  editor.value = first
  const pendingSave = forwarded.save()
  expect(pendingSave).toBe(first.save.mock.results[0]!.value)
  await pendingSave
  expect(forwarded.getDocumentEffects()).toEqual({ version: 1 })
  expect(await forwarded.reloadDocumentEffects()).toEqual({ version: 2 })

  const second = createHandle()
  editor.value = second
  const failure = new Error('Save failed')
  second.save.mockRejectedValueOnce(failure)
  await expect(forwarded.save()).rejects.toBe(failure)
  expect(first.save).toHaveBeenCalledOnce()
  expect(second.save).toHaveBeenCalledOnce()

  editor.value = null
  expect(() => forwarded.getFlags()).toThrow('Entity editor is not mounted.')
})

test('forwards content actions across editor wrappers and tolerates older handles', () => {
  const editor = shallowRef<EntityEditorHandle | null>(null)
  const forwarded = forwardEntityEditorHandle(editor)

  expect(forwarded.getContentActionGroups?.()).toEqual([])
  expect(forwarded.handleContentAction?.('content.notes')).toBe(false)

  editor.value = createHandle()
  expect(forwarded.getContentActionGroups?.()).toEqual([])
  expect(forwarded.handleContentAction?.('content.notes')).toBe(false)

  const getContentActionGroups = vi.fn().mockReturnValue([
    { key: 'attachments-and-notes', label: 'Attachments & Notes', items: [] },
  ])
  const handleContentAction = vi.fn().mockReturnValue(true)
  editor.value = { ...createHandle(), getContentActionGroups, handleContentAction }

  expect(forwarded.getContentActionGroups?.()).toBe(getContentActionGroups.mock.results[0]!.value)
  expect(forwarded.handleContentAction?.('content.notes')).toBe(true)
  expect(handleContentAction).toHaveBeenCalledWith('content.notes')
})
