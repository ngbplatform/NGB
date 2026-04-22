import {
  applyInitialFieldValues,
  buildDocumentFullPageUrl,
  buildFieldsPayload,
  clonePlainData,
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

import type { TradeEntityEditorPersistenceContext } from './tradeEntityEditorPersistenceContext'
import {
  buildTradeDocumentPartsPayload,
  hydrateTradeDocumentPartLookupRows,
  syncTradeDocumentAmountField,
} from './documentParts'

export function useDocumentEntityEditorPersistence(
  args: TradeEntityEditorPersistenceContext,
): DocumentEntityPersistenceAdapter {
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
      args.partsModel.value = clonePlainData(args.initialParts.value ?? null)
      ensureModelKeys(args.docMeta.value.form, args.model.value)
      applyInitialFieldValues(args.model.value, args.initialFields.value ?? null)
      await hydrateEntityReferenceFieldsForEditing({
        entityTypeCode: args.typeCode.value,
        form: args.docMeta.value.form,
        model: args.model.value,
        lookupStore: args.lookupStore,
      })
      await hydrateTradeDocumentPartLookupRows({
        entityTypeCode: args.typeCode.value,
        partsMeta: args.docMeta.value.parts,
        partsModel: args.partsModel.value,
        lookupStore: args.lookupStore,
      })
      syncTradeDocumentAmountField({
        partsMeta: args.docMeta.value.parts,
        partsModel: args.partsModel.value,
        model: args.model.value,
      })
      syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
      args.resetInitialSnapshot()
      return
    }

    const document = await getDocumentById(args.typeCode.value, args.currentId.value!)
    args.doc.value = document
    args.partsModel.value = clonePlainData(document.payload?.parts ?? null)
    await loadEffectsSnapshot(args.typeCode.value, args.currentId.value!)
    setModelFromFields(args.model, document.payload?.fields)
    ensureModelKeys(args.docMeta.value.form, args.model.value)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.docMeta.value.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    })
    await hydrateTradeDocumentPartLookupRows({
      entityTypeCode: args.typeCode.value,
      partsMeta: args.docMeta.value.parts,
      partsModel: args.partsModel.value,
      lookupStore: args.lookupStore,
    })
    syncTradeDocumentAmountField({
      partsMeta: args.docMeta.value.parts,
      partsModel: args.partsModel.value,
      model: args.model.value,
    })
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
    args.resetInitialSnapshot()
  }

  async function save() {
    syncTradeDocumentAmountField({
      partsMeta: args.docMeta.value?.parts,
      partsModel: args.partsModel.value,
      model: args.model.value,
    })

    const fields = buildFieldsPayload(args.metadata.value!.form!, args.model.value)
    const parts = buildTradeDocumentPartsPayload(args.docMeta.value?.parts, args.partsModel.value)
    const shouldNavigateOnCreate = resolveNavigateOnCreate(args.navigateOnCreate.value, args.mode.value)

    if (args.isNew.value) {
      const created = await createDraft(args.typeCode.value, { fields, parts })
      args.currentId.value = created.id
      args.doc.value = created
      args.partsModel.value = clonePlainData(created.payload?.parts ?? parts)
      await hydrateTradeDocumentPartLookupRows({
        entityTypeCode: args.typeCode.value,
        partsMeta: args.docMeta.value?.parts,
        partsModel: args.partsModel.value,
        lookupStore: args.lookupStore,
      })
      args.emitCreated(created.id)
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
    args.partsModel.value = clonePlainData(updated.payload?.parts ?? parts)
    setModelFromFields(args.model, updated.payload?.fields)
    ensureModelKeys(args.metadata.value!.form, args.model.value)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.metadata.value!.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    })
    await hydrateTradeDocumentPartLookupRows({
      entityTypeCode: args.typeCode.value,
      partsMeta: args.docMeta.value?.parts,
      partsModel: args.partsModel.value,
      lookupStore: args.lookupStore,
    })
    syncTradeDocumentAmountField({
      partsMeta: args.docMeta.value?.parts,
      partsModel: args.partsModel.value,
      model: args.model.value,
    })
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
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
