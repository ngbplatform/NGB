import { computed, type ComputedRef } from 'vue'

import { useCommandPalettePageContext } from '../command-palette/useCommandPalettePageContext'
import type { CommandPaletteItemSeed } from '../command-palette/types'
import type { EditorKind, EditorMode } from './types'

type UseEntityEditorCommandPaletteArgs = {
  mode: ComputedRef<EditorMode>
  kind: ComputedRef<EditorKind>
  typeCode: ComputedRef<string>
  currentId: ComputedRef<string | null>
  title: ComputedRef<string>
  isDocumentActionAllowed: (actionCode: string) => boolean
  requestDocumentAction: (actionCode: string) => boolean
}

type CommandDefinition = {
  actionCode: string
  title: string
  subtitle: string
  icon: CommandPaletteItemSeed['icon']
  badge: string
  keywords: string[]
  rank: number
}

const commands: CommandDefinition[] = [
  {
    actionCode: 'view_flow',
    title: 'Open document flow',
    subtitle: 'Open workflow for this document',
    icon: 'document-flow',
    badge: 'Flow',
    keywords: ['flow', 'document flow'],
    rank: 988,
  },
  {
    actionCode: 'view_effects',
    title: 'Open accounting effects',
    subtitle: 'Review ledger impact for this document',
    icon: 'effects-flow',
    badge: 'Effects',
    keywords: ['effects', 'accounting effects', 'posting'],
    rank: 986,
  },
  {
    actionCode: 'print',
    title: 'Print document',
    subtitle: 'Open a print-friendly version of this document',
    icon: 'printer',
    badge: 'Print',
    keywords: ['print', 'print document', 'paper'],
    rank: 985,
  },
  {
    actionCode: 'post',
    title: 'Post document',
    subtitle: 'Post this document',
    icon: 'check',
    badge: 'Post',
    keywords: ['post', 'post document'],
    rank: 984,
  },
  {
    actionCode: 'unpost',
    title: 'Unpost document',
    subtitle: 'Reverse this posted document',
    icon: 'undo',
    badge: 'Unpost',
    keywords: ['unpost', 'unpost document'],
    rank: 984,
  },
]

export function useEntityEditorCommandPalette(args: UseEntityEditorCommandPaletteArgs) {
  const commandPaletteActions = computed<CommandPaletteItemSeed[]>(() => {
    if (args.mode.value !== 'page' || args.kind.value !== 'document' || !args.currentId.value) return []

    return commands
      .filter((command) => args.isDocumentActionAllowed(command.actionCode))
      .map((command) => ({
        key: `current:${command.actionCode}:${args.typeCode.value}:${args.currentId.value}`,
        group: 'actions',
        kind: 'command',
        scope: 'commands',
        title: command.title,
        subtitle: command.subtitle,
        icon: command.icon,
        badge: command.badge,
        hint: null,
        route: null,
        commandCode: `document-${command.actionCode}`,
        status: null,
        openInNewTabSupported: false,
        keywords: command.keywords,
        defaultRank: command.rank,
        isCurrentContext: true,
        perform: () => { args.requestDocumentAction(command.actionCode) },
      }))
  })

  useCommandPalettePageContext(() => {
    if (args.mode.value !== 'page') return null

    return {
      entityType: args.kind.value,
      documentType: args.kind.value === 'document' ? args.typeCode.value : null,
      catalogType: args.kind.value === 'catalog' ? args.typeCode.value : null,
      entityId: args.currentId.value,
      title: args.title.value,
      actions: commandPaletteActions.value,
    }
  })
}
