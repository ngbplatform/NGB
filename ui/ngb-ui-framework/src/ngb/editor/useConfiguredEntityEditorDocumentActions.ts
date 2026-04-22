import { computed, type ComputedRef, type Ref, ref, watch } from 'vue';

import { getDocumentDerivationActions, deriveDocument } from '../api/documents';
import type { DocumentDerivationActionDto } from '../api/contracts';
import type { EntityFormModel } from '../metadata/types';
import type { DocumentTypeMetadata } from '../metadata/types';
import { resolveNgbEditorDocumentActions, resolveNgbEditorRouting } from './config';
import type { EditorErrorState } from './entityEditorErrors';
import type { DocumentHeaderActionGroup, DocumentHeaderActionItem, DocumentUiEffects, EditorKind } from './types';

type DocumentMetadataStoreLike = {
  ensureDocumentType: (documentType: string) => Promise<DocumentTypeMetadata>;
};

type UseConfiguredEntityEditorDocumentActionsArgs = {
  kind: ComputedRef<EditorKind>;
  typeCode: ComputedRef<string>;
  currentId: ComputedRef<string | null>;
  model: Ref<EntityFormModel>;
  uiEffects: ComputedRef<DocumentUiEffects | null>;
  loading: ComputedRef<boolean>;
  saving: ComputedRef<boolean>;
  requestNavigate: (to: string | null | undefined) => void;
  metadataStore: DocumentMetadataStoreLike;
  setEditorError: (value: EditorErrorState | null) => void;
  normalizeEditorError: (cause: unknown) => EditorErrorState;
  loadDerivationActions?: (documentType: string, id: string) => Promise<DocumentDerivationActionDto[]>;
};

export function useConfiguredEntityEditorDocumentActions(
  args: UseConfiguredEntityEditorDocumentActionsArgs,
) {
  const configuredActions = computed(() => {
    if (args.kind.value !== 'document') return [];
    if (!args.currentId.value) return [];

    return resolveNgbEditorDocumentActions({
      context: {
        kind: 'document',
        typeCode: args.typeCode.value,
      },
      documentId: args.currentId.value,
      model: args.model.value,
      uiEffects: args.uiEffects.value,
      loading: args.loading.value,
      saving: args.saving.value,
      navigate: args.requestNavigate,
    });
  });

  const derivationActions = ref<DocumentDerivationActionDto[]>([]);
  const derivationTitles = ref<Record<string, string>>({});
  const loadDerivationActions = args.loadDerivationActions ?? getDocumentDerivationActions;

  watch(
    [args.kind, args.typeCode, args.currentId],
    ([kind, typeCode, documentId], _, onCleanup) => {
      derivationActions.value = [];
      derivationTitles.value = {};

      if (kind !== 'document' || !documentId) return;

      let cancelled = false;
      onCleanup(() => {
        cancelled = true;
      });

      void (async () => {
        try {
          const actions = await loadDerivationActions(typeCode, documentId);
          const titleEntries = await Promise.all(actions.map(async (action) => {
            const title = await resolveDerivationTitle(args.metadataStore, action);
            return [action.code, title] as const;
          }));

          if (cancelled) return;

          derivationActions.value = actions;
          derivationTitles.value = Object.fromEntries(titleEntries);
        } catch {
          if (cancelled) return;
          derivationActions.value = [];
          derivationTitles.value = {};
        }
      })();
    },
    { immediate: true },
  );

  const derivedCreateActions = computed(() =>
    derivationActions.value
      .map((action) => ({
        action,
        title: derivationTitles.value[action.code] ?? action.name,
      }))
      .sort((left, right) => left.title.localeCompare(right.title, undefined, { sensitivity: 'base' }))
      .map(({ action, title }) => ({
        item: {
          key: `derive:${action.code}`,
          title,
          icon: 'file-text' as const,
          disabled: args.loading.value || args.saving.value,
        },
        group: {
          key: 'create',
          label: 'Create',
        },
        run: async () => {
          const relationshipType = action.relationshipCodes
            .map((value) => String(value ?? '').trim())
            .find((value) => value.length > 0);

          if (!relationshipType || !args.currentId.value) return;

          const document = await deriveDocument(action.toTypeCode, {
            sourceDocumentId: args.currentId.value,
            relationshipType,
          });

          args.requestNavigate(resolveNgbEditorRouting().buildDocumentFullPageUrl(action.toTypeCode, document.id));
        },
      })));

  const actions = computed(() => [...configuredActions.value, ...derivedCreateActions.value]);

  const extraPrimaryActions = computed<DocumentHeaderActionItem[]>(() =>
    actions.value
      .filter((action) => !action.group)
      .map((action) => action.item),
  );

  const extraMoreActionGroups = computed<DocumentHeaderActionGroup[]>(() => {
    const buckets = new Map<string, DocumentHeaderActionGroup>();

    for (const action of actions.value) {
      if (!action.group) continue;

      const current = buckets.get(action.group.key) ?? {
        key: action.group.key,
        label: action.group.label,
        items: [],
      };

      current.items.push(action.item);
      buckets.set(action.group.key, current);
    }

    return Array.from(buckets.values());
  });

  function handleConfiguredAction(actionKey: string): boolean {
    const match = actions.value.find((action) => action.item.key === actionKey);
    if (!match) return false;
    if (match.item.disabled) return true;

    args.setEditorError(null);
    void Promise.resolve(match.run()).catch((cause) => {
      args.setEditorError(args.normalizeEditorError(cause));
    });
    return true;
  }

  return {
    extraPrimaryActions,
    extraMoreActionGroups,
    handleConfiguredAction,
  };
}

async function resolveDerivationTitle(
  metadataStore: DocumentMetadataStoreLike,
  action: DocumentDerivationActionDto,
): Promise<string> {
  try {
    const metadata = await metadataStore.ensureDocumentType(action.toTypeCode);
    const title = String(metadata.displayName ?? '').trim();
    if (title) return title;
  } catch {
    // Ignore metadata lookup failures and fall back to the derivation definition name.
  }

  return String(action.name ?? '').trim() || action.toTypeCode;
}
