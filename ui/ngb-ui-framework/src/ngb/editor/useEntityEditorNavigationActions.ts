import { computed, ref, type ComputedRef, type Ref } from 'vue';
import type { RouteLocationNormalizedLoaded, Router } from 'vue-router';

import { copyAppLink } from '../router/shareLink';
import { buildPathWithQuery, currentRouteBackTarget, withBackTarget } from '../router/backNavigation';
import { clonePlainData } from '../utils/clone';
import type { ToastApi } from '../primitives/toast';
import type {
  CatalogTypeMetadata,
  DocumentTypeMetadata,
  EntityFormModel,
  RecordFields,
  RecordPayload,
} from '../metadata/types';
import {
  buildCatalogCompactPageUrl,
} from './catalogNavigation';
import {
  buildDocumentCompactPageUrl,
  buildDocumentEffectsPageUrl,
  buildDocumentFlowPageUrl,
  buildDocumentFullPageUrl,
  buildDocumentPrintPageUrl,
  buildEntityFallbackCloseTarget,
  listFormFields,
  resolveCompactDocumentSourceTarget,
  shouldOpenDocumentInFullPageByDefault,
} from './documentNavigation';
import { saveDocumentCopyDraft } from './documentCopyDraft';
import type { EditorKind, EditorMode } from './types';

type UseEntityEditorNavigationActionsArgs = {
  kind: ComputedRef<EditorKind>;
  typeCode: ComputedRef<string>;
  mode: ComputedRef<EditorMode>;
  compactTo: ComputedRef<string | null>;
  expandTo: ComputedRef<string | null>;
  closeTo: ComputedRef<string | null>;
  currentId: Ref<string | null>;
  metadata: ComputedRef<CatalogTypeMetadata | DocumentTypeMetadata | null>;
  docMeta: Ref<DocumentTypeMetadata | null>;
  model: Ref<EntityFormModel>;
  loading: Ref<boolean>;
  saving: Ref<boolean>;
  canOpenAudit: ComputedRef<boolean>;
  canPrintDocument: ComputedRef<boolean>;
  canOpenDocumentFlowPage: ComputedRef<boolean>;
  canOpenEffectsPage: ComputedRef<boolean>;
  requestNavigate: (to: string | null | undefined) => void;
  requestClose: () => void;
  router: Router;
  route: RouteLocationNormalizedLoaded;
  toasts: Pick<ToastApi, 'push'>;
  buildCopyParts?: () => RecordPayload['parts'] | null;
};

export function useEntityEditorNavigationActions(args: UseEntityEditorNavigationActionsArgs) {
  const auditOpen = ref(false);
  const currentRouteTarget = computed(() => currentRouteBackTarget(args.route));

  const fallbackCloseTarget = computed(() =>
    buildEntityFallbackCloseTarget(args.kind.value, args.typeCode.value),
  );

  const shareLinkTarget = computed(() => {
    if (!args.currentId.value) return null;

    if (args.kind.value === 'catalog') {
      return buildCatalogCompactPageUrl(args.typeCode.value, args.currentId.value);
    }

    if (shouldOpenDocumentInFullPageByDefault(args.docMeta.value)) {
      return buildDocumentFullPageUrl(args.typeCode.value, args.currentId.value);
    }

    return buildDocumentCompactPageUrl(args.typeCode.value, args.currentId.value);
  });

  const drawerBackTarget = computed(() => {
    if (args.mode.value !== 'drawer') return currentRouteTarget.value;

    return buildPathWithQuery(currentRouteTarget.value, {
      panel: args.currentId.value ? 'edit' : 'new',
      id: args.currentId.value,
    });
  });

  const relatedViewBackTarget = computed(() => {
    if (args.kind.value !== 'document') {
      return args.mode.value === 'drawer' ? drawerBackTarget.value : currentRouteTarget.value;
    }

    if (args.mode.value !== 'page') {
      return drawerBackTarget.value;
    }

    return resolveCompactDocumentSourceTarget(args.route, args.compactTo.value)
      ?? currentRouteTarget.value;
  });

  async function copyShareLink() {
    const target = shareLinkTarget.value;
    if (!target) return;
    await copyAppLink(args.router, args.toasts, target);
  }

  function buildDocumentCopyFields(): RecordFields {
    const result: RecordFields = {};
    const formFields = args.metadata.value?.form
      ? listFormFields(args.metadata.value.form).filter((field) => !field?.isReadOnly)
      : Object.keys(args.model.value).map((key) => ({ key, isReadOnly: false }));

    for (const field of formFields) {
      const key = String(field?.key ?? '').trim();
      if (!key || key === 'display' || key === 'number') continue;
      if (!(key in args.model.value)) continue;
      result[key] = clonePlainData(args.model.value[key]) as RecordFields[string];
    }

    return result;
  }

  function buildDocumentCopyTarget(token: string): string {
    if (args.mode.value === 'drawer') {
      return `${buildDocumentCompactPageUrl(args.typeCode.value)}&copyDraft=${encodeURIComponent(token)}`;
    }

    return `${buildDocumentFullPageUrl(args.typeCode.value)}?copyDraft=${encodeURIComponent(token)}`;
  }

  function copyDocument() {
    if (args.kind.value !== 'document') return;
    if (!args.currentId.value || args.loading.value || args.saving.value) return;

    const token = saveDocumentCopyDraft({
      documentType: args.typeCode.value,
      fields: buildDocumentCopyFields(),
      parts: args.buildCopyParts?.() ?? null,
    });

    if (!token) {
      args.toasts.push({
        title: 'Could not copy',
        message: 'The document copy could not be prepared.',
        tone: 'danger',
      });
      return;
    }

    args.requestNavigate(buildDocumentCopyTarget(token));
  }

  function openDocumentPrintPage() {
    if (!args.canPrintDocument.value || !args.currentId.value) return;

    const target = withBackTarget(
      buildDocumentPrintPageUrl(args.typeCode.value, args.currentId.value, { autoPrint: true }),
      relatedViewBackTarget.value,
    );

    args.requestNavigate(target);
  }

  function openAuditLog() {
    if (!args.canOpenAudit.value) return;
    auditOpen.value = true;
  }

  function closeAuditLog() {
    auditOpen.value = false;
  }

  function openDocumentEffectsPage() {
    if (!args.canOpenEffectsPage.value || !args.currentId.value) return;
    void args.router.push(
      withBackTarget(
        buildDocumentEffectsPageUrl(args.typeCode.value, args.currentId.value),
        relatedViewBackTarget.value,
      ),
    );
  }

  function openDocumentFlowPage() {
    if (!args.canOpenDocumentFlowPage.value || !args.currentId.value) return;
    void args.router.push(
      withBackTarget(
        buildDocumentFlowPageUrl(args.typeCode.value, args.currentId.value),
        relatedViewBackTarget.value,
      ),
    );
  }

  function openFullPage() {
    const target = args.expandTo.value;
    if (!target) {
      args.requestNavigate(target);
      return;
    }

    if (args.mode.value === 'drawer') {
      args.requestNavigate(withBackTarget(target, drawerBackTarget.value));
      return;
    }

    args.requestNavigate(target);
  }

  function openCompactPage() {
    args.requestNavigate(args.compactTo.value);
  }

  function closePage() {
    if (args.mode.value === 'drawer') {
      args.requestClose();
      return;
    }

    args.requestNavigate(args.closeTo.value ?? fallbackCloseTarget.value);
  }

  return {
    auditOpen,
    fallbackCloseTarget,
    copyShareLink,
    copyDocument,
    openDocumentPrintPage,
    openAuditLog,
    closeAuditLog,
    openDocumentEffectsPage,
    openDocumentFlowPage,
    openFullPage,
    openCompactPage,
    closePage,
  };
}
