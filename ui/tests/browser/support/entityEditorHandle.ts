import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref, type Component } from 'vue'
import type { EntityEditorHandle } from '@ngbplatform/ui/editor'

const handles: EntityEditorHandle[] = []

export function createConfiguredEditorStub() {
  return defineComponent({
    setup(_props, { expose }) {
      const flags = { canSave: true, isDirty: true, loading: false, saving: false, canExpand: true,
        canDelete: false, canMarkForDeletion: true, canUnmarkForDeletion: false, canPost: true,
        canUnpost: false, canShowAudit: true, canShareLink: true }
      const effects = { accountingEntries: [], operationalRegisterMovements: [], referenceRegisterWrites: [] }
      const handle = {
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
        getDocumentEffects: vi.fn().mockReturnValue(effects),
        reloadDocumentEffects: vi.fn().mockResolvedValue(effects),
        copyShareLink: vi.fn().mockResolvedValue(undefined),
        copyDocument: vi.fn(),
        printDocument: vi.fn(),
        openAuditLog: vi.fn(),
        openAudit: vi.fn(),
        closeAuditLog: vi.fn(),
        getIsDirty: vi.fn().mockReturnValue(true),
        getCanSave: vi.fn().mockReturnValue(true),
        getFlags: vi.fn().mockReturnValue(flags),
      } satisfies EntityEditorHandle
      handles.push(handle)
      expose(handle)
      return () => h('div', 'Configured editor')
    },
  })
}

export function testEntityEditorHandleForwarding(editor: Component, typeCode: string) {
  test('exposes the complete document editor contract to parent refs', async () => {
    handles.length = 0
    const editorRef = ref<EntityEditorHandle | null>(null)
    const generation = ref(0)
    const view = await render(defineComponent({
      setup: () => () => h(editor, { ref: editorRef, key: generation.value, kind: 'document', typeCode }),
    }))
    await expect.element(view.getByText('Configured editor')).toBeVisible()
    const inner = handles[0]!
    for (const key of Object.keys(inner) as (keyof EntityEditorHandle)[]) {
      const result = editorRef.value![key]()
      expect(inner[key], key).toHaveBeenCalledOnce()
      // Preserve return values and the original promises for callers awaiting lifecycle actions.
      expect(result, key).toBe(vi.mocked(inner[key]).mock.results[0]!.value)
      await result
    }
    const failure = new Error('Save failed')
    vi.mocked(inner.save).mockRejectedValueOnce(failure)
    await expect(editorRef.value!.save()).rejects.toBe(failure)

    generation.value++
    await expect.poll(() => handles.length).toBe(2)
    await editorRef.value!.save()
    expect(handles[1]!.save).toHaveBeenCalledOnce()
    expect(inner.save).toHaveBeenCalledTimes(2)
  })
}
