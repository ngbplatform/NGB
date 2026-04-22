import { computed, watch, type ComputedRef, type Ref } from 'vue';

import type { EntityFormModel } from '../metadata/types';
import {
  resolveNgbEditorEntityProfile,
  sanitizeNgbEditorModelForEditing,
  syncNgbEditorComputedDisplay,
} from './config';
import type { EntityEditorContext, EditorKind } from './types';

type DocumentMetaWithParts = {
  parts?: unknown[] | null;
};

export type UseEntityEditorBusinessContextArgs<
  TDocumentMeta extends DocumentMetaWithParts | null = DocumentMetaWithParts | null,
> = {
  kind: ComputedRef<EditorKind>;
  typeCode: ComputedRef<string>;
  model: Ref<EntityFormModel>;
  docMeta: Ref<TDocumentMeta>;
  loading: Ref<boolean>;
  isNew: ComputedRef<boolean>;
  isDraft: ComputedRef<boolean>;
  isMarkedForDeletion: ComputedRef<boolean>;
};

function watchedFieldValues(model: EntityFormModel, fieldKeys: string[] | undefined): unknown[] {
  return (fieldKeys ?? []).map((fieldKey) => model[fieldKey]);
}

export function useEntityEditorBusinessContext<
  TDocumentMeta extends DocumentMetaWithParts | null = DocumentMetaWithParts | null,
>(args: UseEntityEditorBusinessContextArgs<TDocumentMeta>) {
  const currentContext = computed<EntityEditorContext>(() => ({
    kind: args.kind.value,
    typeCode: args.typeCode.value,
  }));

  const entityProfile = computed(() => resolveNgbEditorEntityProfile(currentContext.value));
  const tags = computed(() => Array.from(new Set(entityProfile.value.tags ?? [])));

  const hasDocumentTables = computed(() =>
    args.kind.value === 'document' && (args.docMeta.value?.parts?.length ?? 0) > 0,
  );

  function currentEditorContext(): EntityEditorContext {
    return currentContext.value;
  }

  function hasTag(tag: string): boolean {
    const normalized = String(tag ?? '').trim();
    if (!normalized) return false;
    return tags.value.includes(normalized);
  }

  watch(
    () => [
      args.kind.value,
      args.typeCode.value,
      ...watchedFieldValues(args.model.value, entityProfile.value.sanitizeWatchFields),
    ],
    () => {
      if (!entityProfile.value.sanitizeModelForEditing) return;
      sanitizeNgbEditorModelForEditing(currentContext.value, args.model.value);
    },
    { deep: false },
  );

  watch(
    () => [
      args.kind.value,
      args.typeCode.value,
      args.loading.value,
      args.isNew.value,
      args.isDraft.value,
      args.isMarkedForDeletion.value,
      ...watchedFieldValues(args.model.value, entityProfile.value.computedDisplayWatchFields),
    ],
    () => {
      if (!entityProfile.value.syncComputedDisplay) return;
      if (args.loading.value) return;
      if (args.isMarkedForDeletion.value) return;
      if (entityProfile.value.computedDisplayMode === 'new_or_draft' && !args.isNew.value && !args.isDraft.value) return;
      syncNgbEditorComputedDisplay(currentContext.value, args.model.value);
    },
    { deep: false },
  );

  return {
    currentEditorContext,
    entityProfile,
    tags,
    hasTag,
    hasDocumentTables,
  };
}
