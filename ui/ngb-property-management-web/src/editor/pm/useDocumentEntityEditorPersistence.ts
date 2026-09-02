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
    const typeCode = args.typeCode.value
    const id = args.currentId.value
    const isNew = args.isNew.value
    const isCurrent = () => args.typeCode.value === typeCode
      && args.currentId.value === id
      && args.isNew.value === isNew

    args.catalogMeta.value = null
    args.catalogItem.value = null
    const metadataPromise = args.ensureDocumentMetadata(typeCode)

    if (isNew) {
      const metadata = await metadataPromise
      if (!isCurrent()) return
      const nextModel: typeof args.model.value = {}
      ensureModelKeys(metadata.form, nextModel)
      applyInitialFieldValues(nextModel, args.initialFields.value)
      await hydrateEntityReferenceFieldsForEditing({
        entityTypeCode: typeCode,
        form: metadata.form,
        model: nextModel,
        lookupStore: args.lookupStore,
      })
      if (!isCurrent()) return

      args.docMeta.value = metadata
      args.doc.value = null
      args.docEffects.value = null
      args.model.value = nextModel
      syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
      args.leaseEditor.applyInitialParts(args.initialParts.value)
      args.resetInitialSnapshot()
      return
    }

    const [metadata, editorState] = await Promise.all([
      metadataPromise,
      getDocumentEditorState(typeCode, id!),
    ])
    const { document } = editorState
    const nextModel: typeof args.model.value = { ...(document.payload?.fields ?? {}) }
    ensureModelKeys(metadata.form, nextModel)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: typeCode,
      form: metadata.form,
      model: nextModel,
      lookupStore: args.lookupStore,
    })
    if (!isCurrent()) return

    args.docMeta.value = metadata
    args.doc.value = document
    args.model.value = nextModel
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
