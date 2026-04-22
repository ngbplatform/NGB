import { computed, type ComputedRef } from 'vue';

import type {
  DocumentHeaderActionGroup,
  DocumentHeaderActionItem,
  DocumentHeaderActionKey,
  EditorKind,
  EditorMode,
} from './types';

type ActionHandler = () => void | Promise<void>;

type UseEntityEditorHeaderActionsArgs = {
  kind: ComputedRef<EditorKind>;
  mode: ComputedRef<EditorMode>;
  compactTo: ComputedRef<string | null>;
  expandTo: ComputedRef<string | null>;
  currentId: ComputedRef<string | null>;
  loading: ComputedRef<boolean>;
  saving: ComputedRef<boolean>;
  isNew: ComputedRef<boolean>;
  isMarkedForDeletion: ComputedRef<boolean>;
  canSave: ComputedRef<boolean>;
  canPost: ComputedRef<boolean>;
  canUnpost: ComputedRef<boolean>;
  canMarkForDeletion: ComputedRef<boolean>;
  canUnmarkForDeletion: ComputedRef<boolean>;
  canOpenEffectsPage: ComputedRef<boolean>;
  canOpenDocumentFlowPage: ComputedRef<boolean>;
  canPrintDocument: ComputedRef<boolean>;
  canOpenAudit: ComputedRef<boolean>;
  canShareLink: ComputedRef<boolean>;
  onOpenCompactPage: ActionHandler;
  onOpenFullPage: ActionHandler;
  onCopyDocument: ActionHandler;
  onPrintDocument: ActionHandler;
  onToggleMarkForDeletion: ActionHandler;
  onSave: ActionHandler;
  onTogglePost: ActionHandler;
  onOpenEffectsPage: ActionHandler;
  onOpenDocumentFlowPage: ActionHandler;
  onOpenAuditLog: ActionHandler;
  onCopyShareLink: ActionHandler;
  extraPrimaryActions?: ComputedRef<DocumentHeaderActionItem[]>;
  extraMoreActionGroups?: ComputedRef<DocumentHeaderActionGroup[]>;
  extraActionHandlers?: Record<string, ActionHandler>;
  onUnhandledAction?: (action: string) => void | Promise<void>;
};

function runAction(handler: ActionHandler | undefined): void {
  if (!handler) return;
  void Promise.resolve(handler());
}

function mergeActionGroups(
  baseGroups: DocumentHeaderActionGroup[],
  extraGroups: DocumentHeaderActionGroup[],
): DocumentHeaderActionGroup[] {
  if (extraGroups.length === 0) return baseGroups;

  const order = baseGroups.map((group) => group.key);
  const merged = new Map<string, DocumentHeaderActionGroup>(
    baseGroups.map((group) => [group.key, { ...group, items: [...group.items] }]),
  );

  for (const group of extraGroups) {
    const existing = merged.get(group.key);
    if (existing) {
      existing.items.push(...group.items);
      continue;
    }

    order.push(group.key);
    merged.set(group.key, { ...group, items: [...group.items] });
  }

  return order.map((key) => merged.get(key)!);
}

export function useEntityEditorHeaderActions(args: UseEntityEditorHeaderActionsArgs) {
  const documentPrimaryActions = computed<DocumentHeaderActionItem[]>(() => {
    if (args.kind.value !== 'document') return [];

    const actions: DocumentHeaderActionItem[] = [];

    if (args.mode.value === 'page' && args.compactTo.value) {
      actions.push({
        key: 'openCompactPage',
        title: 'Open compact page',
        icon: 'panel-right',
        disabled: args.loading.value || args.saving.value,
      });
    }

    if (args.mode.value === 'drawer' && args.expandTo.value) {
      actions.push({
        key: 'openFullPage',
        title: 'Open full page',
        icon: 'open-in-new',
        disabled: args.loading.value || args.saving.value,
      });
    }

    if (args.canMarkForDeletion.value || args.canUnmarkForDeletion.value) {
      actions.push({
        key: 'toggleMarkForDeletion',
        title: args.canUnmarkForDeletion.value ? 'Unmark for deletion' : 'Mark for deletion',
        icon: args.canUnmarkForDeletion.value ? 'trash-restore' : 'trash',
        disabled: args.loading.value || args.saving.value,
      });
    }

    actions.push({
      key: 'save',
      title: !args.isNew.value && args.isMarkedForDeletion.value ? 'Restore to edit' : 'Save',
      icon: 'save',
      disabled: args.loading.value || args.saving.value || !args.canSave.value,
    });

    if (args.canPost.value || args.canUnpost.value) {
      actions.push({
        key: 'togglePost',
        title: args.canUnpost.value ? 'Unpost' : 'Post',
        icon: args.canUnpost.value ? 'undo' : 'check',
        disabled: args.loading.value || args.saving.value,
      });
    }

    return [...actions, ...(args.extraPrimaryActions?.value ?? [])];
  });

  const documentMoreActionGroups = computed<DocumentHeaderActionGroup[]>(() => {
    if (args.kind.value !== 'document') return [];

    const groups: DocumentHeaderActionGroup[] = [];
    const createActions: DocumentHeaderActionItem[] = [];
    const relatedViews: DocumentHeaderActionItem[] = [];
    const outputActions: DocumentHeaderActionItem[] = [];
    const historyAndShare: DocumentHeaderActionItem[] = [];

    if (args.currentId.value) {
      createActions.push({
        key: 'copyDocument',
        title: 'Copy',
        icon: 'copy',
        disabled: args.loading.value || args.saving.value,
      });
    }

    if (createActions.length > 0) groups.push({ key: 'create', label: 'Create', items: createActions });

    if (args.canOpenEffectsPage.value) {
      relatedViews.push({
        key: 'openEffectsPage',
        title: 'Accounting entries / effects',
        icon: 'effects-flow',
        disabled: !args.canOpenEffectsPage.value,
      });
    }

    if (args.canOpenDocumentFlowPage.value) {
      relatedViews.push({
        key: 'openDocumentFlowPage',
        title: 'Document flow',
        icon: 'document-flow',
        disabled: !args.canOpenDocumentFlowPage.value,
      });
    }

    if (relatedViews.length > 0) groups.push({ key: 'related-views', label: 'Related views', items: relatedViews });

    if (args.canPrintDocument.value) {
      outputActions.push({
        key: 'printDocument',
        title: 'Print',
        icon: 'printer',
        disabled: !args.canPrintDocument.value,
      });
    }

    if (outputActions.length > 0) groups.push({ key: 'output', label: 'Output', items: outputActions });

    if (args.canOpenAudit.value) {
      historyAndShare.push({
        key: 'openAuditLog',
        title: 'Audit log',
        icon: 'history',
        disabled: args.loading.value || args.saving.value,
      });
    }

    if (args.canShareLink.value) {
      historyAndShare.push({
        key: 'copyShareLink',
        title: 'Share link',
        icon: 'share',
        disabled: args.loading.value || args.saving.value,
      });
    }

    if (historyAndShare.length > 0) {
      groups.push({ key: 'history-and-share', label: 'History & share', items: historyAndShare });
    }

    return mergeActionGroups(groups, args.extraMoreActionGroups?.value ?? []);
  });

  function handleDocumentHeaderAction(action: string) {
    switch (action as DocumentHeaderActionKey) {
      case 'openCompactPage':
        runAction(args.onOpenCompactPage);
        return;
      case 'openFullPage':
        runAction(args.onOpenFullPage);
        return;
      case 'copyDocument':
        runAction(args.onCopyDocument);
        return;
      case 'printDocument':
        runAction(args.onPrintDocument);
        return;
      case 'toggleMarkForDeletion':
        runAction(args.onToggleMarkForDeletion);
        return;
      case 'save':
        runAction(args.onSave);
        return;
      case 'togglePost':
        runAction(args.onTogglePost);
        return;
      case 'openEffectsPage':
        runAction(args.onOpenEffectsPage);
        return;
      case 'openDocumentFlowPage':
        runAction(args.onOpenDocumentFlowPage);
        return;
      case 'openAuditLog':
        runAction(args.onOpenAuditLog);
        return;
      case 'copyShareLink':
        runAction(args.onCopyShareLink);
        return;
      default:
        runAction(args.extraActionHandlers?.[action] ?? (args.onUnhandledAction ? () => args.onUnhandledAction?.(action) : undefined));
    }
  }

  return {
    documentPrimaryActions,
    documentMoreActionGroups,
    handleDocumentHeaderAction,
  };
}
