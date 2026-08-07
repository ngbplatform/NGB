import { computed, type ComputedRef } from 'vue';

import type {
  DocumentHeaderActionGroup,
  DocumentHeaderActionItem,
  DocumentHeaderActionKey,
  EditorKind,
  EditorMode,
} from './types';
import type { DocumentLifecycleHeaderActions } from './useConfiguredEntityEditorDocumentActions';

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
  canShareLink: ComputedRef<boolean>;
  onOpenCompactPage: ActionHandler;
  onOpenFullPage: ActionHandler;
  onCopyDocument: ActionHandler;
  onSave: ActionHandler;
  onCopyShareLink: ActionHandler;
  documentLifecycleActions?: ComputedRef<DocumentLifecycleHeaderActions>;
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
      if (group.key === 'history-and-share') existing.items.unshift(...group.items);
      else existing.items.push(...group.items);
      continue;
    }

    order.push(group.key);
    merged.set(group.key, { ...group, items: [...group.items] });
  }

  const canonicalOrder = ['create', 'related-views', 'output', 'history-and-share', 'actions', 'danger-zone'];
  return order
    .map((key) => merged.get(key)!)
    .sort((left, right) => {
      const leftIndex = canonicalOrder.indexOf(left.key);
      const rightIndex = canonicalOrder.indexOf(right.key);
      if (leftIndex === -1 && rightIndex === -1) return 0;
      if (leftIndex === -1) return 1;
      if (rightIndex === -1) return -1;
      return leftIndex - rightIndex;
    });
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

    const deletionAction = args.documentLifecycleActions?.value.deletion;
    if (deletionAction) actions.push(deletionAction);

    actions.push({
      key: 'save',
      title: !args.isNew.value && args.isMarkedForDeletion.value ? 'Restore to edit' : 'Save',
      icon: 'save',
      disabled: args.loading.value || args.saving.value || !args.canSave.value,
    });

    const postingAction = args.documentLifecycleActions?.value.posting;
    if (postingAction) actions.push(postingAction);

    return [...actions, ...(args.extraPrimaryActions?.value ?? [])];
  });

  const documentMoreActionGroups = computed<DocumentHeaderActionGroup[]>(() => {
    if (args.kind.value !== 'document') return [];

    const groups: DocumentHeaderActionGroup[] = [];
    const createActions: DocumentHeaderActionItem[] = [];
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
      case 'save':
        runAction(args.onSave);
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
