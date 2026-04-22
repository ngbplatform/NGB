import { computed, type ComputedRef } from 'vue';

import { useCommandPalettePageContext } from '../command-palette/useCommandPalettePageContext';
import type { CommandPaletteItemSeed } from '../command-palette/types';
import type { EditorKind, EditorMode } from './types';

type UseEntityEditorCommandPaletteArgs = {
  mode: ComputedRef<EditorMode>;
  kind: ComputedRef<EditorKind>;
  typeCode: ComputedRef<string>;
  currentId: ComputedRef<string | null>;
  title: ComputedRef<string>;
  canOpenDocumentFlowPage: ComputedRef<boolean>;
  canOpenEffectsPage: ComputedRef<boolean>;
  canPrintDocument: ComputedRef<boolean>;
  canPost: ComputedRef<boolean>;
  canUnpost: ComputedRef<boolean>;
  openDocumentFlowPage: () => void;
  openDocumentEffectsPage: () => void;
  openDocumentPrintPage: () => void;
  post: () => Promise<void>;
  unpost: () => Promise<void>;
};

export function useEntityEditorCommandPalette(args: UseEntityEditorCommandPaletteArgs) {
  const commandPaletteActions = computed<CommandPaletteItemSeed[]>(() => {
    if (args.mode.value !== 'page') return [];

    const actions: CommandPaletteItemSeed[] = [];

    if (args.kind.value === 'document' && args.currentId.value && args.canOpenDocumentFlowPage.value) {
      actions.push({
        key: `current:flow:${args.typeCode.value}:${args.currentId.value}`,
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: 'Open document flow',
        subtitle: 'Open workflow for this document',
        icon: 'document-flow',
        badge: 'Flow',
        hint: null,
        route: null,
        commandCode: 'document-flow',
        status: null,
        openInNewTabSupported: false,
        keywords: ['flow', 'document flow'],
        defaultRank: 988,
        isCurrentContext: true,
        perform: args.openDocumentFlowPage,
      });
    }

    if (args.kind.value === 'document' && args.currentId.value && args.canOpenEffectsPage.value) {
      actions.push({
        key: `current:effects:${args.typeCode.value}:${args.currentId.value}`,
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: 'Open accounting effects',
        subtitle: 'Review ledger impact for this document',
        icon: 'effects-flow',
        badge: 'Effects',
        hint: null,
        route: null,
        commandCode: 'accounting-effects',
        status: null,
        openInNewTabSupported: false,
        keywords: ['effects', 'accounting effects', 'posting'],
        defaultRank: 986,
        isCurrentContext: true,
        perform: args.openDocumentEffectsPage,
      });
    }

    if (args.kind.value === 'document' && args.currentId.value && args.canPrintDocument.value) {
      actions.push({
        key: `current:print:${args.typeCode.value}:${args.currentId.value}`,
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: 'Print document',
        subtitle: 'Open a print-friendly version of this document',
        icon: 'printer',
        badge: 'Print',
        hint: null,
        route: null,
        commandCode: 'print-document',
        status: null,
        openInNewTabSupported: false,
        keywords: ['print', 'print document', 'paper'],
        defaultRank: 985,
        isCurrentContext: true,
        perform: args.openDocumentPrintPage,
      });
    }

    if (args.kind.value === 'document' && args.canPost.value) {
      actions.push({
        key: `current:post:${args.typeCode.value}:${args.currentId.value ?? 'new'}`,
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: 'Post document',
        subtitle: 'Post this document to the ledger',
        icon: 'check',
        badge: 'Post',
        hint: null,
        route: null,
        commandCode: 'post-document',
        status: null,
        openInNewTabSupported: false,
        keywords: ['post', 'post document'],
        defaultRank: 984,
        isCurrentContext: true,
        perform: args.post,
      });
    } else if (args.kind.value === 'document' && args.canUnpost.value) {
      actions.push({
        key: `current:unpost:${args.typeCode.value}:${args.currentId.value ?? 'new'}`,
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: 'Unpost document',
        subtitle: 'Reverse the posted ledger impact',
        icon: 'undo',
        badge: 'Unpost',
        hint: null,
        route: null,
        commandCode: 'unpost-document',
        status: null,
        openInNewTabSupported: false,
        keywords: ['unpost', 'unpost document'],
        defaultRank: 984,
        isCurrentContext: true,
        perform: args.unpost,
      });
    }

    return actions;
  });

  useCommandPalettePageContext(() => {
    if (args.mode.value !== 'page') return null;

    return {
      entityType: args.kind.value,
      documentType: args.kind.value === 'document' ? args.typeCode.value : null,
      catalogType: args.kind.value === 'catalog' ? args.typeCode.value : null,
      entityId: args.currentId.value,
      title: args.title.value,
      actions: commandPaletteActions.value,
    };
  });
}
