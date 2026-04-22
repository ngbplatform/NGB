import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const useCommandPalettePageContextMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../src/ngb/command-palette/useCommandPalettePageContext', () => ({
  useCommandPalettePageContext: useCommandPalettePageContextMock,
}))

import { useEntityEditorCommandPalette } from '../../../../src/ngb/editor/useEntityEditorCommandPalette'

type Resolver = () => ReturnType<NonNullable<Parameters<typeof useCommandPalettePageContextMock>[0]>>

function createHarness() {
  const mode = ref<'page' | 'drawer'>('page')
  const kind = ref<'catalog' | 'document'>('document')
  const typeCode = ref('pm.invoice')
  const currentId = ref<string | null>('doc-1')
  const title = ref('Invoice INV-001')
  const canOpenDocumentFlowPage = ref(true)
  const canOpenEffectsPage = ref(true)
  const canPrintDocument = ref(true)
  const canPost = ref(true)
  const canUnpost = ref(false)

  const handlers = {
    openDocumentFlowPage: vi.fn(),
    openDocumentEffectsPage: vi.fn(),
    openDocumentPrintPage: vi.fn(),
    post: vi.fn().mockResolvedValue(undefined),
    unpost: vi.fn().mockResolvedValue(undefined),
  }

  useEntityEditorCommandPalette({
    mode: computed(() => mode.value),
    kind: computed(() => kind.value),
    typeCode: computed(() => typeCode.value),
    currentId: computed(() => currentId.value),
    title: computed(() => title.value),
    canOpenDocumentFlowPage: computed(() => canOpenDocumentFlowPage.value),
    canOpenEffectsPage: computed(() => canOpenEffectsPage.value),
    canPrintDocument: computed(() => canPrintDocument.value),
    canPost: computed(() => canPost.value),
    canUnpost: computed(() => canUnpost.value),
    ...handlers,
  })

  const resolver = useCommandPalettePageContextMock.mock.calls.at(-1)?.[0] as Resolver

  return {
    state: {
      mode,
      kind,
      typeCode,
      currentId,
      title,
      canOpenDocumentFlowPage,
      canOpenEffectsPage,
      canPrintDocument,
      canPost,
      canUnpost,
    },
    handlers,
    resolver,
  }
}

describe('entity editor command palette integration', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('registers current document page context with flow/effects/print/post actions', async () => {
    const { handlers, resolver } = createHarness()
    const context = resolver()

    expect(context).toEqual({
      entityType: 'document',
      documentType: 'pm.invoice',
      catalogType: null,
      entityId: 'doc-1',
      title: 'Invoice INV-001',
      actions: [
        {
          key: 'current:flow:pm.invoice:doc-1',
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
          perform: handlers.openDocumentFlowPage,
        },
        {
          key: 'current:effects:pm.invoice:doc-1',
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
          perform: handlers.openDocumentEffectsPage,
        },
        {
          key: 'current:print:pm.invoice:doc-1',
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
          perform: handlers.openDocumentPrintPage,
        },
        {
          key: 'current:post:pm.invoice:doc-1',
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
          perform: handlers.post,
        },
      ],
    })
  })

  it('switches to unpost action when the document is posted and suppresses page context in drawer mode', () => {
    const { state, handlers, resolver } = createHarness()

    state.canPost.value = false
    state.canUnpost.value = true

    expect(resolver()).toEqual({
      entityType: 'document',
      documentType: 'pm.invoice',
      catalogType: null,
      entityId: 'doc-1',
      title: 'Invoice INV-001',
      actions: expect.arrayContaining([
        expect.objectContaining({
          key: 'current:unpost:pm.invoice:doc-1',
          title: 'Unpost document',
          perform: handlers.unpost,
        }),
      ]),
    })
    expect((resolver()?.actions ?? []).some((item) => item.key.includes(':post:'))).toBe(false)

    state.mode.value = 'drawer'
    expect(resolver()).toBeNull()
  })

  it('publishes catalog page context without document-only actions', () => {
    const { state, resolver } = createHarness()

    state.kind.value = 'catalog'
    state.typeCode.value = 'pm.property'
    state.currentId.value = 'property-1'
    state.title.value = 'Riverfront Tower'

    expect(resolver()).toEqual({
      entityType: 'catalog',
      documentType: null,
      catalogType: 'pm.property',
      entityId: 'property-1',
      title: 'Riverfront Tower',
      actions: [],
    })
  })
})
