import { computed, ref, watch, type ComputedRef } from 'vue';
import type { RouteLocationNormalizedLoaded, Router } from 'vue-router';

import type { EntityFormModel, RecordPayload } from '../metadata/types';
import { normalizeSingleQueryValue, replaceCleanRouteQuery } from '../router/queryParams';
import { readDocumentCopyDraft, type DocumentCopyDraftSnapshot } from './documentCopyDraft';
import { buildDocumentFullPageUrl } from './documentNavigation';

export const DOCUMENT_EDITOR_DRAWER_QUERY_KEYS = ['panel', 'id', 'copyDraft'] as const;

const DOCUMENT_EDITOR_DRAWER_QUERY_KEY_SET = new Set<string>(DOCUMENT_EDITOR_DRAWER_QUERY_KEYS);

export type DocumentEditorDrawerMode = 'closed' | 'new' | 'edit';

export type UseDocumentEditorDrawerStateArgs = {
  route: RouteLocationNormalizedLoaded;
  router: Router;
  documentType: ComputedRef<string>;
};

export function isDocumentEditorDrawerQueryKey(key: string): boolean {
  return DOCUMENT_EDITOR_DRAWER_QUERY_KEY_SET.has(String(key));
}

export function useDocumentEditorDrawerState(
  args: UseDocumentEditorDrawerStateArgs,
) {
  const drawerMode = ref<DocumentEditorDrawerMode>('closed');
  const drawerId = ref<string | null>(null);
  const drawerCopyDraftToken = ref<string | null>(null);

  const isPanelOpen = computed(() =>
    drawerMode.value === 'new' || (drawerMode.value === 'edit' && !!drawerId.value),
  );
  const currentEditorId = computed(() =>
    drawerMode.value === 'edit' ? drawerId.value : null,
  );
  const initialCopyDraft = computed<DocumentCopyDraftSnapshot | null>(() =>
    drawerMode.value === 'new' && args.documentType.value
      ? readDocumentCopyDraft(drawerCopyDraftToken.value, args.documentType.value)
      : null,
  );
  const initialFields = computed<EntityFormModel | null>(() => initialCopyDraft.value?.fields ?? null);
  const initialParts = computed<RecordPayload['parts'] | null>(() => initialCopyDraft.value?.parts ?? null);
  const expandTo = computed(() => {
    if (!args.documentType.value) return null;
    if (drawerMode.value === 'new') return buildDocumentFullPageUrl(args.documentType.value);
    if (currentEditorId.value) return buildDocumentFullPageUrl(args.documentType.value, currentEditorId.value);
    return null;
  });

  function setDrawerState(
    mode: DocumentEditorDrawerMode,
    id: string | null = null,
    copyDraftToken: string | null = null,
  ) {
    drawerMode.value = mode;
    drawerId.value = mode === 'edit' ? id : null;
    drawerCopyDraftToken.value = mode === 'new' ? copyDraftToken : null;
  }

  async function clearDrawerQueryIfPresent() {
    if (
      args.route.query.panel == null
      && args.route.query.id == null
      && args.route.query.copyDraft == null
    ) {
      return;
    }

    await replaceCleanRouteQuery(args.route, args.router, {
      panel: undefined,
      id: undefined,
      copyDraft: undefined,
    });
  }

  function openCreateDrawer(copyDraftToken: string | null = null) {
    setDrawerState('new', null, copyDraftToken);
  }

  function openEditDrawer(id: string) {
    const normalizedId = normalizeSingleQueryValue(id);
    if (!normalizedId) return;
    setDrawerState('edit', normalizedId);
  }

  function reopenCreatedDocument(id: string) {
    openEditDrawer(id);
  }

  async function closeDrawer() {
    setDrawerState('closed');
    await clearDrawerQueryIfPresent();
  }

  function resetDrawerState() {
    setDrawerState('closed');
  }

  watch(
    () => [args.route.query.panel, args.route.query.id, args.route.query.copyDraft, args.documentType.value] as const,
    ([panel, id, copyDraft]) => {
      const normalizedPanel = normalizeSingleQueryValue(panel).toLowerCase();
      const normalizedId = normalizeSingleQueryValue(id);
      const copyDraftToken = normalizeSingleQueryValue(copyDraft) || null;

      if (normalizedPanel === 'new') {
        openCreateDrawer(copyDraftToken);
        void clearDrawerQueryIfPresent();
        return;
      }

      if (normalizedPanel === 'edit' && normalizedId) {
        openEditDrawer(normalizedId);
        void clearDrawerQueryIfPresent();
      }
    },
    { immediate: true },
  );

  watch(
    () => args.documentType.value,
    () => {
      if (
        args.route.query.panel != null
        || args.route.query.id != null
        || args.route.query.copyDraft != null
      ) {
        return;
      }

      resetDrawerState();
    },
  );

  return {
    drawerMode,
    isPanelOpen,
    currentEditorId,
    initialCopyDraft,
    initialFields,
    initialParts,
    expandTo,
    openCreateDrawer,
    openEditDrawer,
    reopenCreatedDocument,
    closeDrawer,
    resetDrawerState,
    clearDrawerQueryIfPresent,
  };
}
