import {
  applyInitialFieldValues,
  buildFieldsPayload,
  createCatalog,
  deleteCatalog,
  ensureModelKeys,
  getCatalogById,
  hydrateEntityReferenceFieldsForEditing,
  markCatalogForDeletion,
  setModelFromFields,
  sanitizeNgbEditorModelForEditing,
  syncNgbEditorComputedDisplay,
  unmarkCatalogForDeletion,
  updateCatalog,
  type CatalogEntityPersistenceAdapter,
} from '@ngbplatform/ui'
import type { PmEntityEditorPersistenceContext } from './pmEntityEditorPersistenceContext'

export function useCatalogEntityEditorPersistence(args: PmEntityEditorPersistenceContext): CatalogEntityPersistenceAdapter {
  async function load() {
    args.docMeta.value = null
    args.doc.value = null
    args.docEffects.value = null
    args.catalogMeta.value = await args.ensureCatalogMetadata(args.typeCode.value)

    if (args.isNew.value) {
      args.catalogItem.value = null
      args.model.value = {}
      ensureModelKeys(args.catalogMeta.value.form, args.model.value)
      applyInitialFieldValues(args.model.value, args.initialFields.value)
      await hydrateEntityReferenceFieldsForEditing({
        entityTypeCode: args.typeCode.value,
        form: args.catalogMeta.value.form,
        model: args.model.value,
        lookupStore: args.lookupStore,
      })
      sanitizeNgbEditorModelForEditing(args.currentEditorContext(), args.model.value)
      syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
      args.leaseEditor.applyInitialParts(null)
      args.resetInitialSnapshot()
      return
    }

    const item = await getCatalogById(args.typeCode.value, args.currentId.value!)
    args.catalogItem.value = item
    setModelFromFields(args.model, item.payload?.fields)
    ensureModelKeys(args.catalogMeta.value.form, args.model.value)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.catalogMeta.value.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    })
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
    args.leaseEditor.applyPersistedParts(null)
    args.resetInitialSnapshot()
  }

  async function save() {
    const fields = buildFieldsPayload(args.metadata.value!.form!, args.model.value)

    if (args.isNew.value) {
      const created = await createCatalog(args.typeCode.value, { fields })
      args.currentId.value = created.id
      args.catalogItem.value = created
      await args.onCreated(created.id)
      args.resetInitialSnapshot()
      return
    }

    const updated = await updateCatalog(args.typeCode.value, args.currentId.value!, { fields })
    args.catalogItem.value = updated
    setModelFromFields(args.model, updated.payload?.fields)
    ensureModelKeys(args.metadata.value!.form, args.model.value)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.metadata.value!.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    })
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
    args.resetInitialSnapshot()
    await args.onSaved()
  }

  async function markForDeletion() {
    await markCatalogForDeletion(args.typeCode.value, args.currentId.value!)
  }

  async function unmarkForDeletion() {
    await unmarkCatalogForDeletion(args.typeCode.value, args.currentId.value!)
  }

  async function deleteEntity() {
    await deleteCatalog(args.typeCode.value, args.currentId.value!)
  }

  return {
    load,
    save,
    markForDeletion,
    unmarkForDeletion,
    deleteEntity,
  }
}
