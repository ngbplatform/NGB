import type { ComputedRef, Ref } from 'vue';

import type { EntityFormModel, RecordPayload } from '../metadata/types';
import type { ToastApi as SharedToastApi } from '../primitives/toast';
import { clonePlainData } from '../utils/clone';
import type { EditorChangeReason, EditorKind } from './types';
import type { EditorErrorState } from './entityEditorErrors';

type EntityEditorMetadataWithForm = {
  form?: unknown | null;
};

export type EntityEditorToastApi = Pick<SharedToastApi, 'push'>;

export type EntityEditorMetadataStoreLike<TCatalogMeta, TDocumentMeta> = {
  ensureCatalogType: (typeCode: string) => Promise<TCatalogMeta>;
  ensureDocumentType: (typeCode: string) => Promise<TDocumentMeta>;
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
  markForDeletion: () => Promise<void>;
  unmarkForDeletion: () => Promise<void>;
  post: () => Promise<void>;
  unpost: () => Promise<void>;
  loadEffectsSnapshot?: (documentType: string, id: string) => Promise<void>;
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
  canPost: ComputedRef<boolean>;
  canUnpost: ComputedRef<boolean>;
  isNew: ComputedRef<boolean>;
  isDirty: ComputedRef<boolean>;
  error: Ref<EditorErrorState | null>;
  setEditorError: (value: EditorErrorState | null) => void;
  normalizeEditorError: (cause: unknown) => EditorErrorState;
  emitChanged: (reason?: EditorChangeReason) => void;
  emitDeleted: () => void;
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

export function useEntityEditorPersistence<
  TMetadata extends EntityEditorMetadataWithForm = EntityEditorMetadataWithForm,
>(args: UseEntityEditorPersistenceArgs<TMetadata>) {
  async function load() {
    if (!args.typeCode.value) return;

    args.loading.value = true;
    args.setEditorError(null);

    try {
      if (args.kind.value === 'catalog') {
        await args.adapters.catalog.load();
        return;
      }

      await args.adapters.document.load();
    } catch (cause) {
      args.setEditorError(args.normalizeEditorError(cause));
    } finally {
      args.loading.value = false;
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
      if (args.kind.value === 'catalog') {
        await args.adapters.catalog.markForDeletion();
      } else {
        await args.adapters.document.markForDeletion();
      }

      await load();
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
      if (args.kind.value === 'catalog') {
        await args.adapters.catalog.unmarkForDeletion();
      } else {
        await args.adapters.document.unmarkForDeletion();
      }

      await load();
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

  async function post() {
    if (!args.canPost.value) return;

    args.setEditorError(null);

    if (args.isDirty.value) {
      await save();
      if (args.error.value) return;
      if (!args.canPost.value) return;
    }

    args.saving.value = true;

    try {
      await args.adapters.document.post();
      await load();
      args.emitChanged('post');
    } catch (cause) {
      args.setEditorError(args.normalizeEditorError(cause));
    } finally {
      args.saving.value = false;
    }
  }

  async function unpost() {
    if (!args.canUnpost.value) return;

    args.saving.value = true;
    args.setEditorError(null);

    try {
      await args.adapters.document.unpost();
      await load();
      args.emitChanged('unpost');
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
    post,
    unpost,
    loadDocumentEffectsSnapshot,
  };
}
