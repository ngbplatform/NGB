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
    const typeCode = args.typeCode.value
    const id = args.currentId.value
    const isNew = args.isNew.value
    const isCurrent = () => args.typeCode.value === typeCode
      && args.currentId.value === id
      && args.isNew.value === isNew

    args.docMeta.value = null
    args.doc.value = null
    args.docEffects.value = null
    const metadataPromise = args.ensureCatalogMetadata(typeCode)

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

      args.catalogMeta.value = metadata
      args.catalogItem.value = null
      args.model.value = nextModel
      sanitizeNgbEditorModelForEditing(args.currentEditorContext(), args.model.value)
      syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value)
      args.leaseEditor.applyInitialParts(null)
      args.resetInitialSnapshot()
      return
    }

    const [metadata, item] = await Promise.all([
      metadataPromise,
      getCatalogById(typeCode, id!),
    ])
    const nextModel: typeof args.model.value = { ...(item.payload?.fields ?? {}) }
    ensureModelKeys(metadata.form, nextModel)
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: typeCode,
      form: metadata.form,
      model: nextModel,
      lookupStore: args.lookupStore,
    })
    if (!isCurrent()) return

    args.catalogMeta.value = metadata
    args.catalogItem.value = item
    args.model.value = nextModel
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
