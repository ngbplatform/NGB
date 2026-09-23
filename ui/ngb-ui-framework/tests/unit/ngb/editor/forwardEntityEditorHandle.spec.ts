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
    getFlags: vi.fn(),
  } satisfies EntityEditorHandle<{ version: number }>
}

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
