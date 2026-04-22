import { computed } from 'vue';

import type { EntityHeaderIconAction } from './types';
import type { UseEntityEditorPageActionsArgs } from './extensions';

export function useEntityEditorPageActions(args: UseEntityEditorPageActionsArgs) {
  return computed<EntityHeaderIconAction[]>(() => {
    if (args.kind.value === 'document' || args.mode.value !== 'page') return [];

    const actions: EntityHeaderIconAction[] = [];

    if (args.compactTo.value) {
      actions.push({
        key: 'openCompactPage',
        title: 'Open compact page',
        icon: 'panel-right',
        disabled: args.loading.value || args.saving.value,
      });
    }

    if (args.canShareLink.value) {
      actions.push({
        key: 'copyShareLink',
        title: 'Share link',
        icon: 'share',
        disabled: args.loading.value || args.saving.value,
      });
    }

    if (args.canOpenAudit.value) {
      actions.push({
        key: 'openAuditLog',
        title: 'Audit log',
        icon: 'history',
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

    return [...actions, ...(args.extraActions?.value ?? [])];
  });
}
