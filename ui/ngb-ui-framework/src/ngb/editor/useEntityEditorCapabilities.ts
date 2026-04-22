import { computed, type ComputedRef, type Ref } from 'vue';

import type { EntityFormModel, DocumentStatusValue } from '../metadata/types';
import { documentStatusLabel, documentStatusTone } from './documentStatus';
import type { EditorKind } from './types';

type EntityEditorCapabilitiesMetadata = {
  displayName?: string | null;
  form?: unknown | null;
};

export type UseEntityEditorCapabilitiesArgs<
  TMetadata extends EntityEditorCapabilitiesMetadata = EntityEditorCapabilitiesMetadata,
> = {
  kind: ComputedRef<EditorKind>;
  currentId: Ref<string | null>;
  metadata: ComputedRef<TMetadata | null>;
  model: Ref<EntityFormModel>;
  loading: Ref<boolean>;
  saving: Ref<boolean>;
  isNew: ComputedRef<boolean>;
  isDraft: ComputedRef<boolean>;
  isMarkedForDeletion: ComputedRef<boolean>;
  status: ComputedRef<DocumentStatusValue>;
};

export function useEntityEditorCapabilities<
  TMetadata extends EntityEditorCapabilitiesMetadata = EntityEditorCapabilitiesMetadata,
>(args: UseEntityEditorCapabilitiesArgs<TMetadata>) {
  const canOpenAudit = computed(() => !args.isNew.value && !!args.currentId.value);
  const canShareLink = computed(() => !args.isNew.value && !!args.currentId.value);

  const canOpenEffectsPage = computed(() =>
    args.kind.value === 'document' && !args.isNew.value && !!args.currentId.value && !args.loading.value && !args.saving.value,
  );

  const canOpenDocumentFlowPage = computed(() =>
    args.kind.value === 'document' && !args.isNew.value && !!args.currentId.value && !args.loading.value && !args.saving.value,
  );

  const canPrintDocument = computed(() =>
    args.kind.value === 'document' && !args.isNew.value && !!args.currentId.value && !args.loading.value && !args.saving.value,
  );

  const canMarkForDeletion = computed(() => {
    if (args.isNew.value) return false;
    if (args.loading.value || args.saving.value) return false;
    if (args.isMarkedForDeletion.value) return false;
    if (args.kind.value === 'document' && !args.isDraft.value) return false;
    return true;
  });

  const canUnmarkForDeletion = computed(() => {
    if (args.isNew.value) return false;
    if (args.loading.value || args.saving.value) return false;
    if (!args.isMarkedForDeletion.value) return false;
    return true;
  });

  const canDelete = computed(() =>
    args.kind.value === 'catalog'
    && !args.isNew.value
    && !args.loading.value
    && !args.saving.value
    && !args.isMarkedForDeletion.value,
  );

  const canPost = computed(() => {
    if (args.kind.value !== 'document') return false;
    if (args.isNew.value) return false;
    if (args.loading.value || args.saving.value) return false;
    return args.isDraft.value && !args.isMarkedForDeletion.value;
  });

  const canUnpost = computed(() => {
    if (args.kind.value !== 'document') return false;
    if (args.isNew.value) return false;
    if (args.loading.value || args.saving.value) return false;
    return args.status.value === 2;
  });

  const canSave = computed(() => {
    if (!args.metadata.value?.form) return false;
    if (!args.isNew.value && args.isMarkedForDeletion.value) return false;
    if (args.kind.value !== 'document') return true;
    if (args.isNew.value) return true;
    return args.isDraft.value;
  });

  const documentDisplayTitle = computed(() => {
    if (args.kind.value !== 'document') return '';

    const display = String(args.model.value.display ?? '').trim();
    if (display) return display;

    const base = args.metadata.value?.displayName ?? 'Document';
    return args.isNew.value ? `New ${base}` : base;
  });

  const resolvedDocumentStatusLabel = computed(() => {
    if (args.kind.value !== 'document') return 'Draft';
    if (args.isNew.value) return 'Draft';
    return documentStatusLabel(args.status.value);
  });

  const resolvedDocumentStatusTone = computed(() => {
    if (args.isNew.value) return 'neutral' as const;
    return documentStatusTone(args.status.value);
  });

  const title = computed(() => {
    if (args.kind.value === 'document') return documentDisplayTitle.value;

    const base = args.metadata.value?.displayName ?? 'Catalog record';
    return args.isNew.value ? `New ${base}` : base;
  });

  const subtitle = computed(() => {
    if (args.kind.value === 'document') return resolvedDocumentStatusLabel.value;

    const display = String(args.model.value.display ?? '').trim();
    if (display) return display;

    return args.isNew.value ? 'New record' : undefined;
  });

  const auditEntityKind = computed(() => (args.kind.value === 'catalog' ? 2 : 1));
  const auditEntityId = computed(() => args.currentId.value);
  const auditEntityTitle = computed(() => String(args.model.value.display ?? title.value ?? '').trim());

  const isReadOnly = computed(() =>
    (!args.isNew.value && args.isMarkedForDeletion.value)
    || (args.kind.value === 'document' && !args.isNew.value && !args.isDraft.value),
  );

  return {
    canOpenAudit,
    canShareLink,
    canOpenEffectsPage,
    canOpenDocumentFlowPage,
    canPrintDocument,
    canMarkForDeletion,
    canUnmarkForDeletion,
    canDelete,
    canPost,
    canUnpost,
    canSave,
    documentStatusLabel: resolvedDocumentStatusLabel,
    documentStatusTone: resolvedDocumentStatusTone,
    title,
    subtitle,
    auditEntityKind,
    auditEntityId,
    auditEntityTitle,
    isReadOnly,
  };
}
