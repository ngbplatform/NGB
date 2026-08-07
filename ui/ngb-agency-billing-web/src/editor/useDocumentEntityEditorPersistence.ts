import {
  applyInitialFieldValues,
  buildDocumentFullPageUrl,
  buildFieldsPayload,
  clonePlainData,
  createDraft,
  ensureModelKeys,
  getDocumentEditorState,
  getDocumentEffects,
  hydrateEntityReferenceFieldsForEditing,
  resolveNavigateOnCreate,
  setModelFromFields,
  syncNgbEditorComputedDisplay,
  updateDraft,
  type DocumentEntityPersistenceAdapter,
} from '@ngbplatform/ui'

import type { AgencyBillingEntityEditorPersistenceContext } from './agencyBillingEntityEditorPersistenceContext'
import {
  buildAgencyBillingDocumentPartsPayload,
  hydrateAgencyBillingDocumentPartLookupRows,
  syncAgencyBillingDocumentComputedFields,
} from './documentParts'

export function useDocumentEntityEditorPersistence(
  args: AgencyBillingEntityEditorPersistenceContext,
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
      await hydrateAgencyBillingDocumentPartLookupRows({
        entityTypeCode: args.typeCode.value,
        partsMeta: args.docMeta.value.parts,
        partsModel: args.partsModel.value,
        lookupStore: args.lookupStore,
      })
      syncAgencyBillingDocumentComputedFields({
        documentType: args.typeCode.value,
        partsMeta: args.docMeta.value.parts,
        partsModel: args.partsModel.value,
        model: args.model.value,
      })
      syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
      args.resetInitialSnapshot()
      return
    }

    const { document } = await getDocumentEditorState(args.typeCode.value, args.currentId.value!)
    args.doc.value = document
    args.partsModel.value = clonePlainData(document.payload?.parts ?? null)
    setModelFromFields(args.model, document.payload?.fields)
    ensureModelKeys(args.docMeta.value.form, args.model.value)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.docMeta.value.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    })
    await hydrateAgencyBillingDocumentPartLookupRows({
      entityTypeCode: args.typeCode.value,
      partsMeta: args.docMeta.value.parts,
      partsModel: args.partsModel.value,
      lookupStore: args.lookupStore,
    })
    syncAgencyBillingDocumentComputedFields({
      documentType: args.typeCode.value,
      partsMeta: args.docMeta.value.parts,
      partsModel: args.partsModel.value,
      model: args.model.value,
    })
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
    args.resetInitialSnapshot()
  }

  async function save() {
    syncAgencyBillingDocumentComputedFields({
      documentType: args.typeCode.value,
      partsMeta: args.docMeta.value?.parts,
      partsModel: args.partsModel.value,
      model: args.model.value,
    })

    const fields = buildFieldsPayload(args.metadata.value!.form!, args.model.value)
    const parts = buildAgencyBillingDocumentPartsPayload(args.typeCode.value, args.docMeta.value?.parts, args.partsModel.value)
    const shouldNavigateOnCreate = resolveNavigateOnCreate(args.navigateOnCreate.value, args.mode.value)

    if (args.isNew.value) {
      const created = await createDraft(args.typeCode.value, { fields, parts })
      args.currentId.value = created.id
      args.doc.value = created
      args.partsModel.value = clonePlainData(created.payload?.parts ?? parts)
      await hydrateAgencyBillingDocumentPartLookupRows({
        entityTypeCode: args.typeCode.value,
        partsMeta: args.docMeta.value?.parts,
        partsModel: args.partsModel.value,
        lookupStore: args.lookupStore,
      })
      args.emitCreated(created.id)
      args.resetInitialSnapshot()

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
    await hydrateAgencyBillingDocumentPartLookupRows({
      entityTypeCode: args.typeCode.value,
      partsMeta: args.docMeta.value?.parts,
      partsModel: args.partsModel.value,
      lookupStore: args.lookupStore,
    })
    syncAgencyBillingDocumentComputedFields({
      documentType: args.typeCode.value,
      partsMeta: args.docMeta.value?.parts,
      partsModel: args.partsModel.value,
      model: args.model.value,
    })
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
    args.resetInitialSnapshot()
    args.emitSaved()
  }

  return {
    loadEffectsSnapshot,
    load,
    save,
  }
}
