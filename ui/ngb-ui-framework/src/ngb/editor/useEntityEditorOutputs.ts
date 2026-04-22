import { computed, watch, type ComputedRef } from 'vue';

import type { EntityEditorFlags } from './types';

type UseEntityEditorOutputsArgs = {
  emit: {
    (event: 'state', value: { title: string; subtitle?: string }): void;
    (event: 'flags', value: EntityEditorFlags): void;
  };
  title: ComputedRef<string>;
  subtitle: ComputedRef<string | undefined>;
  isDirty: ComputedRef<boolean>;
  loading: ComputedRef<boolean>;
  saving: ComputedRef<boolean>;
  canExpand: ComputedRef<boolean>;
  canDelete: ComputedRef<boolean>;
  canMarkForDeletion: ComputedRef<boolean>;
  canUnmarkForDeletion: ComputedRef<boolean>;
  canPost: ComputedRef<boolean>;
  canUnpost: ComputedRef<boolean>;
  canOpenAudit: ComputedRef<boolean>;
  canShareLink: ComputedRef<boolean>;
  canSave: ComputedRef<boolean>;
  extraFlags?: ComputedRef<Record<string, boolean | undefined> | null | undefined>;
};

export function useEntityEditorOutputs(args: UseEntityEditorOutputsArgs) {
  const flags = computed<EntityEditorFlags>(() => ({
    canSave: !!args.canSave.value,
    isDirty: !!args.isDirty.value,
    loading: !!args.loading.value,
    saving: !!args.saving.value,
    canExpand: !!args.canExpand.value,
    canDelete: !!args.canDelete.value,
    canMarkForDeletion: !!args.canMarkForDeletion.value,
    canUnmarkForDeletion: !!args.canUnmarkForDeletion.value,
    canPost: !!args.canPost.value,
    canUnpost: !!args.canUnpost.value,
    canShowAudit: !!args.canOpenAudit.value,
    canShareLink: !!args.canShareLink.value,
    extras: args.extraFlags?.value ? { ...args.extraFlags.value } : undefined,
  }));

  watch(
    () => [args.title.value, args.subtitle.value] as const,
    () => args.emit('state', { title: args.title.value, subtitle: args.subtitle.value }),
    { immediate: true },
  );

  watch(
    flags,
    (value) => args.emit('flags', value),
    { immediate: true },
  );

  return {
    flags,
  };
}
