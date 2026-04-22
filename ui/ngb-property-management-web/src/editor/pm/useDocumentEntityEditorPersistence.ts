import {
  applyInitialFieldValues,
  buildDocumentFullPageUrl,
  buildFieldsPayload,
  createDraft,
  ensureModelKeys,
  getDocumentById,
  getDocumentEffects,
  hydrateEntityReferenceFieldsForEditing,
  markDocumentForDeletion,
  postDocument,
  resolveNavigateOnCreate,
  setModelFromFields,
  syncNgbEditorComputedDisplay,
  unmarkDocumentForDeletion,
  unpostDocument,
  updateDraft,
  type DocumentEntityPersistenceAdapter,
} from 'ngb-ui-framework'
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
    args.docMeta.value = await args.metaStore.ensureDocumentType(args.typeCode.value)

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

    const document = await getDocumentById(args.typeCode.value, args.currentId.value!)
    args.doc.value = document
    await loadEffectsSnapshot(args.typeCode.value, args.currentId.value!)
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
        args.toasts.push({ title: 'Invalid tenants', message: validationError, tone: 'danger' })
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

    const shouldNavigateOnCreate = resolveNavigateOnCreate(args.navigateOnCreate.value, args.mode.value)

    if (args.isNew.value) {
      const created = await createDraft(args.typeCode.value, { fields, parts })
      args.currentId.value = created.id
      args.doc.value = created
      args.emitCreated(created.id)
      args.leaseEditor.applyPersistedParts(created.payload?.parts)
      args.resetInitialSnapshot()

      if (!shouldNavigateOnCreate) {
        await loadEffectsSnapshot(args.typeCode.value, created.id)
      }

      if (shouldNavigateOnCreate) {
        await args.router.replace(buildDocumentFullPageUrl(args.typeCode.value, created.id))
      }
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
    await loadEffectsSnapshot(args.typeCode.value, args.currentId.value!)
    args.emitSaved()
  }

  async function markForDeletion() {
    args.doc.value = await markDocumentForDeletion(args.typeCode.value, args.currentId.value!)
    args.toasts.push({ title: 'Deleted', message: 'Document marked for deletion.', tone: 'warn' })
  }

  async function unmarkForDeletion() {
    args.doc.value = await unmarkDocumentForDeletion(args.typeCode.value, args.currentId.value!)
    args.toasts.push({ title: 'Restored', message: 'Document restored.', tone: 'success' })
  }

  async function post() {
    args.doc.value = await postDocument(args.typeCode.value, args.currentId.value!)
  }

  async function unpost() {
    args.doc.value = await unpostDocument(args.typeCode.value, args.currentId.value!)
  }

  return {
    loadEffectsSnapshot,
    load,
    save,
    markForDeletion,
    unmarkForDeletion,
    post,
    unpost,
  }
}
