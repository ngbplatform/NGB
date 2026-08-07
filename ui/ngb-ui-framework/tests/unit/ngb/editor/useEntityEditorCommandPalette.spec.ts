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
  const allowedActionCodes = ref(['view_flow', 'view_effects', 'print', 'post'])
  const requestDocumentAction = vi.fn(() => true)

  useEntityEditorCommandPalette({
    mode: computed(() => mode.value),
    kind: computed(() => kind.value),
    typeCode: computed(() => typeCode.value),
    currentId: computed(() => currentId.value),
    title: computed(() => title.value),
    isDocumentActionAllowed: (actionCode) => allowedActionCodes.value.includes(actionCode),
    requestDocumentAction,
  })

  const resolver = useCommandPalettePageContextMock.mock.calls.at(-1)?.[0] as Resolver

  return {
    state: {
      mode,
      kind,
      typeCode,
      currentId,
      title,
      allowedActionCodes,
    },
    requestDocumentAction,
    resolver,
  }
}

describe('entity editor command palette integration', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('registers current document page context with flow/effects/print/post actions', async () => {
    const { requestDocumentAction, resolver } = createHarness()
    const context = resolver()

    expect(context).toMatchObject({
      entityType: 'document',
      documentType: 'pm.invoice',
      catalogType: null,
      entityId: 'doc-1',
      title: 'Invoice INV-001',
    })
    expect(context?.actions.map((action) => action.key)).toEqual([
      'current:view_flow:pm.invoice:doc-1',
      'current:view_effects:pm.invoice:doc-1',
      'current:print:pm.invoice:doc-1',
      'current:post:pm.invoice:doc-1',
    ])
    expect(context?.actions[0]).toMatchObject({
      title: 'Open document flow',
      commandCode: 'document-view_flow',
      isCurrentContext: true,
    })
    context?.actions[0]?.perform?.()
    expect(requestDocumentAction).toHaveBeenCalledWith('view_flow')
  })

  it('switches to unpost action when the document is posted and suppresses page context in drawer mode', () => {
    const { state, requestDocumentAction, resolver } = createHarness()

    state.allowedActionCodes.value = ['view_flow', 'view_effects', 'print', 'unpost']

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
        }),
      ]),
    })
    expect((resolver()?.actions ?? []).some((item) => item.key.includes(':post:'))).toBe(false)
    resolver()?.actions.find((item) => item.key.includes(':unpost:'))?.perform?.()
    expect(requestDocumentAction).toHaveBeenCalledWith('unpost')

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
