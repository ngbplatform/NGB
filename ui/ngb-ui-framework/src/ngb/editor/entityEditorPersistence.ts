import type { ComputedRef, Ref } from 'vue';

import type {
  CatalogItemDto,
  CatalogTypeMetadataDto,
  DocumentDto,
  DocumentEffectsDto,
  DocumentTypeMetadataDto,
} from '../api/contracts';
import {
  createCatalog,
  deleteCatalog,
  getCatalogById,
  markCatalogForDeletion,
  unmarkCatalogForDeletion,
  updateCatalog,
} from '../api/catalogs';
import {
  createDraft,
  getDocumentEditorState,
  getDocumentEffects,
  updateDraft,
} from '../api/documents';
import type {
  EntityFormModel,
  LookupStoreApi,
  PartMetadata,
  RecordPayload,
} from '../metadata/types';
import { buildFieldsPayload, ensureModelKeys } from '../metadata/entityForm';
import { hydrateEntityReferenceFieldsForEditing } from '../metadata/referenceHydration';
import { clonePlainData } from '../utils/clone';
import { sanitizeNgbEditorModelForEditing, syncNgbEditorComputedDisplay } from './config';
import type { EditorChangeReason, EditorKind, EntityEditorContext } from './types';
import type { EditorErrorState } from './entityEditorErrors';

type EntityEditorMetadataWithForm = {
  form?: unknown | null;
};

export type EntityEditorMetadataStoreLike<TCatalogMeta, TDocumentMeta> = {
  ensureCatalogType: (typeCode: string) => Promise<TCatalogMeta>;
  ensureDocumentType: (typeCode: string) => Promise<TDocumentMeta>;
};

/**
 * State and application ports shared by vertical persistence adapters.
 *
 * Deliberately excludes router and presentation services: adapters may load,
 * hydrate and persist editor state, while navigation and user feedback remain
 * owned by the configured editor orchestration layer.
 */
export type ConfiguredEntityEditorPersistenceContext = {
  kind: ComputedRef<EditorKind>;
  typeCode: ComputedRef<string>;
  currentId: Ref<string | null>;
  isNew: ComputedRef<boolean>;
  metadata: ComputedRef<CatalogTypeMetadataDto | DocumentTypeMetadataDto | null>;
  catalogMeta: Ref<CatalogTypeMetadataDto | null>;
  docMeta: Ref<DocumentTypeMetadataDto | null>;
  catalogItem: Ref<CatalogItemDto | null>;
  doc: Ref<DocumentDto | null>;
  docEffects: Ref<DocumentEffectsDto | null>;
  model: Ref<EntityFormModel>;
  partsModel: Ref<RecordPayload['parts'] | null>;
  lookupStore: LookupStoreApi;
  initialFields: ComputedRef<EntityFormModel | null | undefined>;
  initialParts: ComputedRef<RecordPayload['parts'] | null | undefined>;
  ensureCatalogMetadata: (typeCode: string) => Promise<CatalogTypeMetadataDto>;
  ensureDocumentMetadata: (typeCode: string) => Promise<DocumentTypeMetadataDto>;
  currentEditorContext: () => EntityEditorContext;
  resetInitialSnapshot: () => void;
  setEditorError: (value: EditorErrorState | null) => void;
  onCreated: (id: string) => void | Promise<void>;
  onSaved: () => void | Promise<void>;
};

export type CatalogEntityPersistenceAdapter = {
  load: () => Promise<void>;
  save: () => Promise<void>;
  markForDeletion: () => Promise<void>;
  unmarkForDeletion: () => Promise<void>;
  deleteEntity: () => Promise<void>;
};

export type DocumentEntityPersistenceAdapter = {
  load: () => Promise<void>;
  save: () => Promise<void>;
  loadEffectsSnapshot?: (documentType: string, id: string) => Promise<void>;
};

export type ConfiguredDocumentPartsPersistenceStrategy = {
  buildPayload: (args: {
    documentType: string;
    partsMeta: PartMetadata[] | null | undefined;
    partsModel: RecordPayload['parts'] | null;
  }) => RecordPayload['parts'] | null;
  hydrate: (args: {
    entityTypeCode: string;
    partsMeta: PartMetadata[] | null | undefined;
    partsModel: RecordPayload['parts'] | null;
    lookupStore: LookupStoreApi;
  }) => void | Promise<void>;
  synchronize: (args: {
    documentType: string;
    partsMeta: PartMetadata[] | null | undefined;
    partsModel: RecordPayload['parts'] | null;
    model: EntityFormModel;
  }) => void;
};

export type UseEntityEditorPersistenceArgs<
  TMetadata extends EntityEditorMetadataWithForm = EntityEditorMetadataWithForm,
> = {
  kind: ComputedRef<EditorKind>;
  typeCode: ComputedRef<string>;
  metadata: ComputedRef<TMetadata | null>;
  loading: Ref<boolean>;
  saving: Ref<boolean>;
  canSave: ComputedRef<boolean>;
  canMarkForDeletion: ComputedRef<boolean>;
  canUnmarkForDeletion: ComputedRef<boolean>;
  canDelete: ComputedRef<boolean>;
  isNew: ComputedRef<boolean>;
  setEditorError: (value: EditorErrorState | null) => void;
  normalizeEditorError: (cause: unknown) => EditorErrorState;
  emitChanged: (reason?: EditorChangeReason) => void;
  emitDeleted: () => void;
  onMarkedForDeletion?: () => void;
  onUnmarkedForDeletion?: () => void;
  adapters: {
    catalog: CatalogEntityPersistenceAdapter;
    document: DocumentEntityPersistenceAdapter;
  };
};

export function applyInitialFieldValues(target: EntityFormModel, source: EntityFormModel | null) {
  if (!source) return;

  for (const [key, value] of Object.entries(source)) {
    if (value === undefined) continue;
    target[key] = clonePlainData(value);
  }
}

export function setModelFromFields(
  target: Ref<EntityFormModel>,
  fields: RecordPayload['fields'] | null | undefined,
) {
  target.value = { ...((fields ?? {}) as EntityFormModel) };
}

/**
 * Standard catalog persistence used by metadata-driven vertical editors.
 *
 * Vertical packages own composition only; transport, hydration and lifecycle
 * sequencing stay centralized so every vertical receives identical fixes.
 */
export function createConfiguredCatalogEntityEditorPersistence(
  args: ConfiguredEntityEditorPersistenceContext,
): CatalogEntityPersistenceAdapter {
  async function load() {
    const typeCode = args.typeCode.value;
    const id = args.currentId.value;
    const isNew = args.isNew.value;
    const isCurrent = () => args.typeCode.value === typeCode
      && args.currentId.value === id
      && args.isNew.value === isNew;

    args.docMeta.value = null;
    args.doc.value = null;
    args.docEffects.value = null;
    args.partsModel.value = null;
    const metadataPromise = args.ensureCatalogMetadata(typeCode);

    if (isNew) {
      const metadata = await metadataPromise;
      if (!isCurrent()) return;
      const nextModel: EntityFormModel = {};
      ensureModelKeys(metadata.form, nextModel);
      applyInitialFieldValues(nextModel, args.initialFields.value ?? null);
      await hydrateEntityReferenceFieldsForEditing({
        entityTypeCode: typeCode,
        form: metadata.form,
        model: nextModel,
        lookupStore: args.lookupStore,
      });
      if (!isCurrent()) return;

      args.catalogMeta.value = metadata;
      args.catalogItem.value = null;
      args.model.value = nextModel;
      sanitizeNgbEditorModelForEditing(args.currentEditorContext(), args.model.value);
      syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value);
      args.resetInitialSnapshot();
      return;
    }

    const [metadata, item] = await Promise.all([
      metadataPromise,
      getCatalogById(typeCode, id!),
    ]);
    const nextModel: EntityFormModel = { ...((item.payload?.fields ?? {}) as EntityFormModel) };
    ensureModelKeys(metadata.form, nextModel);
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: typeCode,
      form: metadata.form,
      model: nextModel,
      lookupStore: args.lookupStore,
    });
    if (!isCurrent()) return;

    args.catalogMeta.value = metadata;
    args.catalogItem.value = item;
    args.model.value = nextModel;
    sanitizeNgbEditorModelForEditing(args.currentEditorContext(), args.model.value);
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value);
    args.resetInitialSnapshot();
  }

  async function save() {
    const fields = buildFieldsPayload(args.metadata.value!.form!, args.model.value);

    if (args.isNew.value) {
      const created = await createCatalog(args.typeCode.value, { fields });
      args.currentId.value = created.id;
      args.catalogItem.value = created;
      await args.onCreated(created.id);
      args.resetInitialSnapshot();
      return;
    }

    const updated = await updateCatalog(args.typeCode.value, args.currentId.value!, { fields });
    args.catalogItem.value = updated;
    setModelFromFields(args.model, updated.payload?.fields);
    ensureModelKeys(args.metadata.value!.form, args.model.value);
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.metadata.value!.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    });
    sanitizeNgbEditorModelForEditing(args.currentEditorContext(), args.model.value);
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value);
    args.resetInitialSnapshot();
    await args.onSaved();
  }

  return {
    load,
    save,
    markForDeletion: async () => {
      await markCatalogForDeletion(args.typeCode.value, args.currentId.value!);
    },
    unmarkForDeletion: async () => {
      await unmarkCatalogForDeletion(args.typeCode.value, args.currentId.value!);
    },
    deleteEntity: async () => {
      await deleteCatalog(args.typeCode.value, args.currentId.value!);
    },
  };
}

/** Standard document persistence with vertical document-part policies as ports. */
export function createConfiguredDocumentEntityEditorPersistence(
  args: ConfiguredEntityEditorPersistenceContext,
  strategy: ConfiguredDocumentPartsPersistenceStrategy,
): DocumentEntityPersistenceAdapter {
  async function hydrateParts(partsMeta: PartMetadata[] | null | undefined) {
    await strategy.hydrate({
      entityTypeCode: args.typeCode.value,
      partsMeta,
      partsModel: args.partsModel.value,
      lookupStore: args.lookupStore,
    });
  }

  function synchronize(partsMeta: PartMetadata[] | null | undefined) {
    strategy.synchronize({
      documentType: args.typeCode.value,
      partsMeta,
      partsModel: args.partsModel.value,
      model: args.model.value,
    });
  }

  async function loadEffectsSnapshot(documentType: string, id: string) {
    try {
      args.docEffects.value = await getDocumentEffects(documentType, id);
    } catch {
      args.docEffects.value = null;
    }
  }

  async function load() {
    const typeCode = args.typeCode.value;
    const id = args.currentId.value;
    const isNew = args.isNew.value;
    const isCurrent = () => args.typeCode.value === typeCode
      && args.currentId.value === id
      && args.isNew.value === isNew;

    args.catalogMeta.value = null;
    args.catalogItem.value = null;
    const metadataPromise = args.ensureDocumentMetadata(typeCode);

    if (isNew) {
      const metadata = await metadataPromise;
      if (!isCurrent()) return;
      const nextModel: EntityFormModel = {};
      const nextParts = clonePlainData(args.initialParts.value ?? null);
      ensureModelKeys(metadata.form, nextModel);
      applyInitialFieldValues(nextModel, args.initialFields.value ?? null);
      await Promise.all([
        hydrateEntityReferenceFieldsForEditing({
          entityTypeCode: typeCode,
          form: metadata.form,
          model: nextModel,
          lookupStore: args.lookupStore,
        }),
        strategy.hydrate({
          entityTypeCode: typeCode,
          partsMeta: metadata.parts,
          partsModel: nextParts,
          lookupStore: args.lookupStore,
        }),
      ]);
      if (!isCurrent()) return;

      strategy.synchronize({
        documentType: typeCode,
        partsMeta: metadata.parts,
        partsModel: nextParts,
        model: nextModel,
      });
      args.docMeta.value = metadata;
      args.doc.value = null;
      args.docEffects.value = null;
      args.model.value = nextModel;
      args.partsModel.value = nextParts;
      syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value);
      args.resetInitialSnapshot();
      return;
    }

    const [metadata, editorState] = await Promise.all([
      metadataPromise,
      getDocumentEditorState(typeCode, id!),
    ]);
    const { document } = editorState;
    const nextParts = clonePlainData(document.payload?.parts ?? null);
    const nextModel: EntityFormModel = { ...((document.payload?.fields ?? {}) as EntityFormModel) };
    ensureModelKeys(metadata.form, nextModel);
    await Promise.all([
      hydrateEntityReferenceFieldsForEditing({
        entityTypeCode: typeCode,
        form: metadata.form,
        model: nextModel,
        lookupStore: args.lookupStore,
      }),
      strategy.hydrate({
        entityTypeCode: typeCode,
        partsMeta: metadata.parts,
        partsModel: nextParts,
        lookupStore: args.lookupStore,
      }),
    ]);
    if (!isCurrent()) return;

    strategy.synchronize({
      documentType: typeCode,
      partsMeta: metadata.parts,
      partsModel: nextParts,
      model: nextModel,
    });
    args.docMeta.value = metadata;
    args.doc.value = document;
    args.partsModel.value = nextParts;
    args.model.value = nextModel;
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value);
    args.resetInitialSnapshot();
  }

  async function save() {
    synchronize(args.docMeta.value?.parts);
    const fields = buildFieldsPayload(args.metadata.value!.form!, args.model.value);
    const parts = strategy.buildPayload({
      documentType: args.typeCode.value,
      partsMeta: args.docMeta.value?.parts,
      partsModel: args.partsModel.value,
    });

    if (args.isNew.value) {
      const created = await createDraft(args.typeCode.value, { fields, parts });
      args.currentId.value = created.id;
      args.doc.value = created;
      args.partsModel.value = clonePlainData(created.payload?.parts ?? parts);
      await hydrateParts(args.docMeta.value?.parts);
      await args.onCreated(created.id);
      args.resetInitialSnapshot();
      return;
    }

    const updated = await updateDraft(args.typeCode.value, args.currentId.value!, { fields, parts });
    args.doc.value = updated;
    args.partsModel.value = clonePlainData(updated.payload?.parts ?? parts);
    setModelFromFields(args.model, updated.payload?.fields);
    ensureModelKeys(args.metadata.value!.form, args.model.value);
    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: args.typeCode.value,
      form: args.metadata.value!.form,
      model: args.model.value,
      lookupStore: args.lookupStore,
    });
    await hydrateParts(args.docMeta.value?.parts);
    synchronize(args.docMeta.value?.parts);
    syncNgbEditorComputedDisplay(args.currentEditorContext(), args.model.value);
    args.resetInitialSnapshot();
    await args.onSaved();
  }

  return { loadEffectsSnapshot, load, save };
}

export function useEntityEditorPersistence<
  TMetadata extends EntityEditorMetadataWithForm = EntityEditorMetadataWithForm,
>(args: UseEntityEditorPersistenceArgs<TMetadata>) {
  let loadSequence = 0;

  async function load() {
    if (!args.typeCode.value) return;

    const sequence = ++loadSequence;
    args.loading.value = true;
    args.setEditorError(null);

    try {
      if (args.kind.value === 'catalog') {
        await args.adapters.catalog.load();
        return;
      }

      await args.adapters.document.load();
    } catch (cause) {
      if (sequence !== loadSequence) return;
      args.setEditorError(args.normalizeEditorError(cause));
    } finally {
      if (sequence === loadSequence) args.loading.value = false;
    }
  }

  async function save() {
    if (!args.metadata.value?.form) return;
    if (!args.canSave.value) return;

    args.saving.value = true;
    args.setEditorError(null);

    try {
      if (args.kind.value === 'catalog') {
        await args.adapters.catalog.save();
        return;
      }

      await args.adapters.document.save();
    } catch (cause) {
      args.setEditorError(args.normalizeEditorError(cause));
    } finally {
      args.saving.value = false;
    }
  }

  async function markForDeletion() {
    if (args.isNew.value || !args.canMarkForDeletion.value) return;

    args.saving.value = true;
    args.setEditorError(null);

    try {
      if (args.kind.value !== 'catalog') return;
      await args.adapters.catalog.markForDeletion();

      await load();
      args.onMarkedForDeletion?.();
      args.emitChanged('markForDeletion');
    } catch (cause) {
      args.setEditorError(args.normalizeEditorError(cause));
    } finally {
      args.saving.value = false;
    }
  }

  async function unmarkForDeletion() {
    if (args.isNew.value || !args.canUnmarkForDeletion.value) return;

    args.saving.value = true;
    args.setEditorError(null);

    try {
      if (args.kind.value !== 'catalog') return;
      await args.adapters.catalog.unmarkForDeletion();

      await load();
      args.onUnmarkedForDeletion?.();
      args.emitChanged('unmarkForDeletion');
    } catch (cause) {
      args.setEditorError(args.normalizeEditorError(cause));
    } finally {
      args.saving.value = false;
    }
  }

  async function deleteEntity() {
    if (args.kind.value !== 'catalog' || args.isNew.value || !args.canDelete.value) return;

    args.saving.value = true;
    args.setEditorError(null);

    try {
      await args.adapters.catalog.deleteEntity();
      await load();
      args.emitDeleted();
    } catch (cause) {
      args.setEditorError(args.normalizeEditorError(cause));
    } finally {
      args.saving.value = false;
    }
  }

  async function loadDocumentEffectsSnapshot(documentType: string, id: string) {
    await args.adapters.document.loadEffectsSnapshot?.(documentType, id);
  }

  return {
    load,
    save,
    markForDeletion,
    unmarkForDeletion,
    deleteEntity,
    loadDocumentEffectsSnapshot,
  };
}
