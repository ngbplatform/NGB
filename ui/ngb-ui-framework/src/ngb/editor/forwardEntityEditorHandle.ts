import type { Ref } from 'vue'
import type { EntityEditorHandle } from './types'

/** Keep configuration wrappers transparent to parent toolbars and page actions. */
export function forwardEntityEditorHandle<TDocumentEffects>(
  editor: Readonly<Ref<EntityEditorHandle<TDocumentEffects> | null>>,
): EntityEditorHandle<TDocumentEffects> {
  function current(): EntityEditorHandle<TDocumentEffects> {
    if (!editor.value) throw new Error('Entity editor is not mounted.')
    return editor.value
  }

  // Resolve the ref on invocation: it is null during setup and may change on remount.
  return {
    save: () => current().save(),
    load: () => current().load(),
    openFullPage: () => current().openFullPage(),
    openCompactPage: () => current().openCompactPage(),
    closePage: () => current().closePage(),
    toggleMarkForDeletion: () => current().toggleMarkForDeletion(),
    togglePost: () => current().togglePost(),
    markForDeletion: () => current().markForDeletion(),
    unmarkForDeletion: () => current().unmarkForDeletion(),
    deleteEntity: () => current().deleteEntity(),
    post: () => current().post(),
    unpost: () => current().unpost(),
    getDocumentEffects: () => current().getDocumentEffects(),
    reloadDocumentEffects: () => current().reloadDocumentEffects(),
    copyShareLink: () => current().copyShareLink(),
    copyDocument: () => current().copyDocument(),
    printDocument: () => current().printDocument(),
    openAuditLog: () => current().openAuditLog(),
    openAudit: () => current().openAudit(),
    closeAuditLog: () => current().closeAuditLog(),
    getIsDirty: () => current().getIsDirty(),
    getCanSave: () => current().getCanSave(),
    getFlags: () => current().getFlags(),
  }
}
