import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h, ref, type PropType } from 'vue'

import {
  buildCatalogCompactPageUrl,
} from '../../../../src/ngb/editor/catalogNavigation'
import {
  buildDocumentCompactPageUrl,
  buildDocumentEffectsPageUrl,
  buildDocumentFlowPageUrl,
  buildDocumentFullPageUrl,
  buildDocumentPrintPageUrl,
} from '../../../../src/ngb/editor/documentNavigation'
import { useEntityEditorNavigationActions } from '../../../../src/ngb/editor/useEntityEditorNavigationActions'
import { buildPathWithQuery, encodeBackTarget, withBackTarget } from '../../../../src/ngb/router/backNavigation'

const mocks = vi.hoisted(() => ({
  copyAppLink: vi.fn(),
  saveDocumentCopyDraft: vi.fn(),
}))

vi.mock('../../../../src/ngb/router/shareLink', () => ({
  copyAppLink: mocks.copyAppLink,
}))

vi.mock('../../../../src/ngb/editor/documentCopyDraft', () => ({
  saveDocumentCopyDraft: mocks.saveDocumentCopyDraft,
}))

function createDocumentMetadata(hasTables = false) {
  return {
    documentType: 'pm.invoice',
    displayName: 'Customer Invoice',
    kind: 2,
    form: {
      sections: [
        {
          title: 'Main',
          rows: [
            {
              fields: [
                {
                  key: 'title',
                  label: 'Title',
                  dataType: 'String',
                  uiControl: 0,
                  isRequired: false,
                  isReadOnly: false,
                },
                {
                  key: 'read_only_note',
                  label: 'Read-only note',
                  dataType: 'String',
                  uiControl: 0,
                  isRequired: false,
                  isReadOnly: true,
                },
                {
                  key: 'notes',
                  label: 'Notes',
                  dataType: 'String',
                  uiControl: 0,
                  isRequired: false,
                  isReadOnly: false,
                },
                {
                  key: 'display',
                  label: 'Display',
                  dataType: 'String',
                  uiControl: 0,
                  isRequired: false,
                  isReadOnly: false,
                },
                {
                  key: 'number',
                  label: 'Number',
                  dataType: 'String',
                  uiControl: 0,
                  isRequired: false,
                  isReadOnly: false,
                },
              ],
            },
          ],
        },
      ],
    },
    parts: hasTables
      ? [
          {
            partCode: 'lines',
            title: 'Lines',
          },
        ]
      : [],
  }
}

function createCatalogMetadata() {
  return {
    catalogType: 'pm.property',
    displayName: 'Property',
    kind: 1,
  }
}

function text(locator: { element(): Element }): string {
  return locator.element().textContent?.trim() ?? ''
}

const NavigationActionsHarness = defineComponent({
  props: {
    kind: {
      type: String as PropType<'document' | 'catalog'>,
      default: 'document',
    },
    typeCode: {
      type: String,
      default: 'pm.invoice',
    },
    mode: {
      type: String as PropType<'page' | 'drawer'>,
      default: 'drawer',
    },
    currentId: {
      type: String,
      default: 'doc-1',
    },
    compactTo: {
      type: String,
      default: '/documents/pm.invoice?panel=edit&id=doc-1',
    },
    expandTo: {
      type: String,
      default: '/documents/pm.invoice/doc-1',
    },
    closeTo: {
      type: String,
      default: null,
    },
    routeFullPath: {
      type: String,
      default: '/documents/pm.invoice?panel=edit&id=doc-1',
    },
    routeQuery: {
      type: Object as PropType<Record<string, unknown>>,
      default: () => ({}),
    },
    canOpenAudit: {
      type: Boolean,
      default: true,
    },
    canPrintDocument: {
      type: Boolean,
      default: true,
    },
    canOpenDocumentFlowPage: {
      type: Boolean,
      default: true,
    },
    canOpenEffectsPage: {
      type: Boolean,
      default: true,
    },
    hasTables: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const currentId = ref<string | null>(props.currentId)
    const model = ref({
      title: 'Invoice INV-001',
      read_only_note: 'server managed',
      notes: {
        internal: 'retain this note',
      },
      display: 'Invoice INV-001',
      number: 'INV-001',
    })
    const requestNavigateCalls = ref<string[]>([])
    const routerPushCalls = ref<string[]>([])
    const requestCloseCount = ref(0)

    const router = {
      push: vi.fn((to: string) => {
        routerPushCalls.value = [...routerPushCalls.value, String(to)]
        return Promise.resolve()
      }),
      replace: vi.fn(),
      back: vi.fn(),
    }

    const actions = useEntityEditorNavigationActions({
      kind: computed(() => props.kind),
      typeCode: computed(() => props.typeCode),
      mode: computed(() => props.mode),
      compactTo: computed(() => props.compactTo),
      expandTo: computed(() => props.expandTo),
      closeTo: computed(() => props.closeTo),
      currentId,
      metadata: computed(() => (
        props.kind === 'document'
          ? createDocumentMetadata(props.hasTables)
          : createCatalogMetadata()
      )),
      docMeta: ref(props.kind === 'document' ? createDocumentMetadata(props.hasTables) : null),
      model,
      loading: ref(false),
      saving: ref(false),
      canOpenAudit: computed(() => props.canOpenAudit),
      canPrintDocument: computed(() => props.canPrintDocument),
      canOpenDocumentFlowPage: computed(() => props.canOpenDocumentFlowPage),
      canOpenEffectsPage: computed(() => props.canOpenEffectsPage),
      requestNavigate: (to) => {
        requestNavigateCalls.value = [...requestNavigateCalls.value, String(to ?? 'null')]
      },
      requestClose: () => {
        requestCloseCount.value += 1
      },
      router: router as never,
      route: {
        fullPath: props.routeFullPath,
        query: props.routeQuery,
      } as never,
      toasts: {
        push: vi.fn(),
      },
      buildCopyParts: () => ({
        lines: {
          rows: [{ amount: 1250 }],
        },
      }),
    })

    return () => h('div', { class: 'space-y-2' }, [
      h('button', { type: 'button', onClick: () => void actions.copyShareLink() }, 'Copy share link'),
      h('button', { type: 'button', onClick: () => actions.copyDocument() }, 'Copy document'),
      h('button', { type: 'button', onClick: () => actions.openDocumentPrintPage() }, 'Open print'),
      h('button', { type: 'button', onClick: () => actions.openAuditLog() }, 'Open audit'),
      h('button', { type: 'button', onClick: () => actions.closeAuditLog() }, 'Close audit'),
      h('button', { type: 'button', onClick: () => actions.openDocumentEffectsPage() }, 'Open effects'),
      h('button', { type: 'button', onClick: () => actions.openDocumentFlowPage() }, 'Open flow'),
      h('button', { type: 'button', onClick: () => actions.openFullPage() }, 'Open full'),
      h('button', { type: 'button', onClick: () => actions.openCompactPage() }, 'Open compact'),
      h('button', { type: 'button', onClick: () => actions.closePage() }, 'Close page'),
      h('div', { 'data-testid': 'audit-state' }, actions.auditOpen.value ? 'open' : 'closed'),
      h('div', { 'data-testid': 'fallback-close' }, actions.fallbackCloseTarget.value),
      h('div', { 'data-testid': 'request-navigate-calls' }, requestNavigateCalls.value.join('|') || 'none'),
      h('div', { 'data-testid': 'router-push-calls' }, routerPushCalls.value.join('|') || 'none'),
      h('div', { 'data-testid': 'request-close-count' }, String(requestCloseCount.value)),
    ])
  },
})

describe('useEntityEditorNavigationActions browser harness', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.saveDocumentCopyDraft.mockReturnValue('copy-token')
  })

  it('copies compact document share links and writable document drafts from a drawer editor', async () => {
    const view = await render(NavigationActionsHarness)

    await view.getByRole('button', { name: 'Copy share link' }).click()
    await vi.waitFor(() => {
      expect(mocks.copyAppLink).toHaveBeenCalledWith(
        expect.any(Object),
        expect.any(Object),
        buildDocumentCompactPageUrl('pm.invoice', 'doc-1'),
      )
    })

    await view.getByRole('button', { name: 'Copy document' }).click()

    expect(mocks.saveDocumentCopyDraft).toHaveBeenCalledWith({
      documentType: 'pm.invoice',
      fields: {
        title: 'Invoice INV-001',
        notes: {
          internal: 'retain this note',
        },
      },
      parts: {
        lines: {
          rows: [{ amount: 1250 }],
        },
      },
    })
    expect(text(view.getByTestId('request-navigate-calls'))).toBe('/documents/pm.invoice?panel=new&copyDraft=copy-token')
  })

  it('routes page-mode print, effects, flow, audit, and shell navigation while preserving the current back trail', async () => {
    const reportBackTarget = '/reports/pm.occupancy.summary?variant=audit-view'
    const encodedBack = encodeBackTarget(reportBackTarget)
    const routeFullPath = `/documents/pm.invoice/doc-1?back=${encodedBack}`

    const view = await render(NavigationActionsHarness, {
      props: {
        mode: 'page',
        routeFullPath,
        compactTo: '/documents/pm.invoice?panel=edit&id=doc-1&layout=compact',
        expandTo: '/documents/pm.invoice/doc-1?layout=full',
      },
    })

    await view.getByRole('button', { name: 'Open print' }).click()
    await view.getByRole('button', { name: 'Open effects' }).click()
    await view.getByRole('button', { name: 'Open flow' }).click()
    await view.getByRole('button', { name: 'Open audit' }).click()
    await expect.element(view.getByTestId('audit-state')).toHaveTextContent('open')
    await view.getByRole('button', { name: 'Close audit' }).click()
    await expect.element(view.getByTestId('audit-state')).toHaveTextContent('closed')
    await view.getByRole('button', { name: 'Open full' }).click()
    await view.getByRole('button', { name: 'Open compact' }).click()
    await view.getByRole('button', { name: 'Close page' }).click()

    expect(text(view.getByTestId('request-navigate-calls'))).toBe([
      withBackTarget(buildDocumentPrintPageUrl('pm.invoice', 'doc-1', { autoPrint: true }), routeFullPath),
      '/documents/pm.invoice/doc-1?layout=full',
      '/documents/pm.invoice?panel=edit&id=doc-1&layout=compact',
      '/documents/pm.invoice',
    ].join('|'))
    expect(text(view.getByTestId('router-push-calls'))).toBe([
      withBackTarget(buildDocumentEffectsPageUrl('pm.invoice', 'doc-1'), routeFullPath),
      withBackTarget(buildDocumentFlowPageUrl('pm.invoice', 'doc-1'), routeFullPath),
    ].join('|'))
    expect(text(view.getByTestId('request-close-count'))).toBe('0')
  })

  it('keeps compact-source context for related document views when a full page was opened from a drawer', async () => {
    const compactSource = '/documents/pm.invoice?search=late&panel=edit&id=doc-1&trash=deleted'
    const encodedBack = encodeBackTarget(compactSource)
    const routeFullPath = `/documents/pm.invoice/doc-1?back=${encodedBack}`

    const view = await render(NavigationActionsHarness, {
      props: {
        mode: 'page',
        routeFullPath,
        routeQuery: {
          back: encodedBack,
        },
        compactTo: '/documents/pm.invoice?panel=edit&id=doc-1',
        expandTo: '/documents/pm.invoice/doc-1',
      },
    })

    await view.getByRole('button', { name: 'Open print' }).click()
    await view.getByRole('button', { name: 'Open effects' }).click()
    await view.getByRole('button', { name: 'Open flow' }).click()

    expect(text(view.getByTestId('request-navigate-calls'))).toBe(
      withBackTarget(buildDocumentPrintPageUrl('pm.invoice', 'doc-1', { autoPrint: true }), compactSource),
    )
    expect(text(view.getByTestId('router-push-calls'))).toBe([
      withBackTarget(buildDocumentEffectsPageUrl('pm.invoice', 'doc-1'), compactSource),
      withBackTarget(buildDocumentFlowPageUrl('pm.invoice', 'doc-1'), compactSource),
    ].join('|'))
  })

  it('wraps drawer full-page navigation in an encoded back target so the source list route can be restored', async () => {
    const routeFullPath = '/catalogs/pm.property?search=river&trash=deleted&panel=edit&id=prop-1'

    const view = await render(NavigationActionsHarness, {
      props: {
        kind: 'catalog',
        mode: 'drawer',
        typeCode: 'pm.property',
        currentId: 'prop-1',
        compactTo: '/catalogs/pm.property?panel=edit&id=prop-1',
        expandTo: '/catalogs-full/pm.property/prop-1',
        routeFullPath,
      },
    })

    await view.getByRole('button', { name: 'Open full' }).click()

    expect(text(view.getByTestId('request-navigate-calls'))).toBe(
      withBackTarget('/catalogs-full/pm.property/prop-1', routeFullPath),
    )
  })

  it('restores drawer-origin document navigation by adding panel state back into the encoded back target', async () => {
    const routeFullPath = '/documents/pm.invoice?search=late&trash=deleted'
    const restorableDrawerTarget = buildPathWithQuery(routeFullPath, {
      panel: 'edit',
      id: 'doc-1',
    })

    const view = await render(NavigationActionsHarness, {
      props: {
        kind: 'document',
        mode: 'drawer',
        typeCode: 'pm.invoice',
        currentId: 'doc-1',
        compactTo: null,
        expandTo: '/documents/pm.invoice/doc-1',
        routeFullPath,
      },
    })

    await view.getByRole('button', { name: 'Open print' }).click()
    await view.getByRole('button', { name: 'Open effects' }).click()
    await view.getByRole('button', { name: 'Open flow' }).click()
    await view.getByRole('button', { name: 'Open full' }).click()

    expect(text(view.getByTestId('request-navigate-calls'))).toBe([
      withBackTarget(buildDocumentPrintPageUrl('pm.invoice', 'doc-1', { autoPrint: true }), restorableDrawerTarget),
      withBackTarget('/documents/pm.invoice/doc-1', restorableDrawerTarget),
    ].join('|'))
    expect(text(view.getByTestId('router-push-calls'))).toBe([
      withBackTarget(buildDocumentEffectsPageUrl('pm.invoice', 'doc-1'), restorableDrawerTarget),
      withBackTarget(buildDocumentFlowPageUrl('pm.invoice', 'doc-1'), restorableDrawerTarget),
    ].join('|'))
  })

  it('uses full-page share links for documents that open full page by default', async () => {
    const view = await render(NavigationActionsHarness, {
      props: {
        mode: 'page',
        hasTables: true,
      },
    })

    await view.getByRole('button', { name: 'Copy share link' }).click()

    await vi.waitFor(() => {
      expect(mocks.copyAppLink).toHaveBeenCalledWith(
        expect.any(Object),
        expect.any(Object),
        buildDocumentFullPageUrl('pm.invoice', 'doc-1'),
      )
    })
  })

  it('uses catalog share routes and fallback close targets when no explicit close route exists', async () => {
    const view = await render(NavigationActionsHarness, {
      props: {
        kind: 'catalog',
        mode: 'page',
        typeCode: 'pm.property',
        currentId: 'prop-1',
        compactTo: '/catalogs/pm.property?panel=edit&id=prop-1',
        expandTo: '/catalogs/pm.property/prop-1',
        routeFullPath: '/catalogs/pm.property?panel=edit&id=prop-1',
      },
    })

    await view.getByRole('button', { name: 'Copy share link' }).click()
    await view.getByRole('button', { name: 'Close page' }).click()

    await vi.waitFor(() => {
      expect(mocks.copyAppLink).toHaveBeenCalledWith(
        expect.any(Object),
        expect.any(Object),
        buildCatalogCompactPageUrl('pm.property', 'prop-1'),
      )
    })
    expect(text(view.getByTestId('fallback-close'))).toBe('/catalogs/pm.property')
    expect(text(view.getByTestId('request-navigate-calls'))).toBe('/catalogs/pm.property')
  })
})
