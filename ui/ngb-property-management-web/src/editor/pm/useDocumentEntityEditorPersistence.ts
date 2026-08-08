import {
  applyInitialFieldValues,
  buildFieldsPayload,
  createDraft,
  ensureModelKeys,
  getDocumentEditorState,
  getDocumentEffects,
  hydrateEntityReferenceFieldsForEditing,
  setModelFromFields,
  syncNgbEditorComputedDisplay,
  updateDraft,
  type DocumentEntityPersistenceAdapter,
} from '@ngbplatform/ui'
import type { PmEntityEditorPersistenceContext } from './pmEntityEditorPersistenceContext'

export function useDocumentEntityEditorPersistence(args: PmEntityEditorPersistenceContext): DocumentEntityPersistenceAdapter {
  async function loadEffectsSnapshot(documentType: string, id: string) {
    try {
      args.docEffects.value = await getDocumentEffects(documentType, id)
    } catch {
      args.docEffects.value = null
    }
  }

  async function load() {
    args.catalogMeta.value = null
    args.catalogItem.value = null
    args.docMeta.value = await args.ensureDocumentMetadata(args.typeCode.value)

    if (args.isNew.value) {
      args.doc.value = null
      args.docEffects.value = null
      args.model.value = {}
      ensureModelKeys(args.docMeta.value.form, args.model.value)
      applyInitialFieldValues(args.model.value, args.initialFields.value)
      await hydrateEntityReferenceFieldsForEditing({
        entityTypeCode: args.typeCode.value,
        form: args.docMeta.value.form,
        model: args.model.value,
        lookupStore: args.lookupStore,
      })
      syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
      args.leaseEditor.applyInitialParts(args.initialParts.value)
      args.resetInitialSnapshot()
      return
    }

    const { document } = await getDocumentEditorState(args.typeCode.value, args.currentId.value!)
    args.doc.value = document
    setModelFromFields(args.model, document.payload?.fields)
    ensureModelKeys(args.docMeta.value.form, args.model.value)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.docMeta.value.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    })
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
    args.leaseEditor.applyPersistedParts(document.payload?.parts)
    args.resetInitialSnapshot()
  }

  async function save() {
    const fields = buildFieldsPayload(args.metadata.value!.form!, args.model.value)
    let parts = args.leaseEditor.buildSaveParts()

    if (args.leaseEditor.isLeaseDocument.value) {
      args.leaseEditor.ensureLeasePartiesInitialized()
      const validationError = args.leaseEditor.validateLeasePartiesBeforeSave()

      if (validationError) {
        args.setEditorError({
          summary: 'Tenant list is invalid.',
          issues: [{ path: 'parties', label: 'Tenants', scope: 'collection', messages: [validationError], code: null }],
          errorCode: null,
          status: 400,
          context: null,
        })
        return
      }

      parts = args.leaseEditor.buildSaveParts()
    }

    if (args.isNew.value) {
      const created = await createDraft(args.typeCode.value, { fields, parts })
      args.currentId.value = created.id
      args.doc.value = created
      await args.onCreated(created.id)
      args.leaseEditor.applyPersistedParts(created.payload?.parts)
      args.resetInitialSnapshot()

      return
    }

    const updated = await updateDraft(args.typeCode.value, args.currentId.value!, { fields, parts })
    args.doc.value = updated
    setModelFromFields(args.model, updated.payload?.fields)
    ensureModelKeys(args.metadata.value!.form, args.model.value)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.metadata.value!.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    })
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
    args.leaseEditor.applyPersistedParts(updated.payload?.parts)
    args.resetInitialSnapshot()
    await args.onSaved()
  }

  return {
    loadEffectsSnapshot,
    load,
    save,
  }
}
