import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Router } from 'vue-router'

import {
  buildDocumentEffectsPageUrl,
  buildDocumentFlowPageUrl,
  buildDocumentFullPageUrl,
  buildDocumentPrintPageUrl,
} from '../../../../src/ngb/editor/documentNavigation'
import { useEntityEditorNavigationActions } from '../../../../src/ngb/editor/useEntityEditorNavigationActions'
import { buildPathWithQuery, currentRouteBackTarget, withBackTarget } from '../../../../src/ngb/router/backNavigation'

const saveDocumentCopyDraftMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../src/ngb/editor/documentCopyDraft', () => ({
  saveDocumentCopyDraft: saveDocumentCopyDraftMock,
}))

function createDocumentMetadata() {
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
                {
                  key: 'notes',
                  label: 'Notes',
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
    parts: [],
  }
}

function createArgs(overrides: Partial<Parameters<typeof useEntityEditorNavigationActions>[0]> = {}) {
  const route = {
    fullPath: '/documents/pm.invoice?panel=edit&id=doc-1',
  } as Parameters<typeof useEntityEditorNavigationActions>[0]['route']

  const router = {
    push: vi.fn(),
    replace: vi.fn(),
    back: vi.fn(),
  } as unknown as Router

  return {
    args: {
      kind: computed(() => 'document' as const),
      typeCode: computed(() => 'pm.invoice'),
      mode: computed(() => 'drawer' as const),
      compactTo: computed(() => '/documents/pm.invoice?panel=edit&id=doc-1'),
      expandTo: computed(() => '/documents/pm.invoice/doc-1'),
      closeTo: computed(() => null),
      currentId: ref('doc-1'),
      metadata: computed(() => createDocumentMetadata()),
      docMeta: ref(createDocumentMetadata()),
      model: ref({
        title: 'Invoice INV-001',
        read_only_note: 'server managed',
        display: 'Invoice INV-001',
        number: 'INV-001',
        notes: {
          internal: 'retain this note',
        },
      }),
      loading: ref(false),
      saving: ref(false),
      canOpenAudit: computed(() => true),
      canPrintDocument: computed(() => true),
      canOpenDocumentFlowPage: computed(() => true),
      canOpenEffectsPage: computed(() => true),
      requestNavigate: vi.fn(),
      requestClose: vi.fn(),
      router,
      route,
      toasts: {
        push: vi.fn(),
      },
      buildCopyParts: vi.fn(() => ({
        lines: {
          rows: [{ amount: 1250 }],
        },
      })),
      ...overrides,
    },
    route,
    router,
  }
}

describe('entity editor navigation actions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('copies writable document fields into a drawer copy target', () => {
    saveDocumentCopyDraftMock.mockReturnValueOnce('copy-token')

    const { args } = createArgs()
    const actions = useEntityEditorNavigationActions(args)

    actions.copyDocument()

    expect(saveDocumentCopyDraftMock).toHaveBeenCalledWith({
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
    expect(args.requestNavigate).toHaveBeenCalledWith('/documents/pm.invoice?panel=new&copyDraft=copy-token')
  })

  it('shows a toast when a document copy token cannot be created', () => {
    saveDocumentCopyDraftMock.mockReturnValueOnce(null)

    const { args } = createArgs({
      mode: computed(() => 'page' as const),
    })
    const actions = useEntityEditorNavigationActions(args)

    actions.copyDocument()

    expect(args.requestNavigate).not.toHaveBeenCalled()
    expect(args.toasts.push).toHaveBeenCalledWith({
      title: 'Could not copy',
      message: 'The document copy could not be prepared.',
      tone: 'danger',
    })
  })

  it('routes print, effects, flow, audit, and close actions through editor navigation helpers', () => {
    const { args, route, router } = createArgs({
      mode: computed(() => 'page' as const),
      closeTo: computed(() => null),
    })
    const actions = useEntityEditorNavigationActions(args)
    const backTarget = currentRouteBackTarget(route)

    actions.openDocumentPrintPage()
    actions.openDocumentEffectsPage()
    actions.openDocumentFlowPage()
    actions.openAuditLog()
    actions.closeAuditLog()
    actions.closePage()

    expect(args.requestNavigate).toHaveBeenNthCalledWith(
      1,
      withBackTarget(
        buildDocumentPrintPageUrl('pm.invoice', 'doc-1', { autoPrint: true }),
        backTarget,
      ),
    )
    expect(router.push).toHaveBeenNthCalledWith(
      1,
      withBackTarget(buildDocumentEffectsPageUrl('pm.invoice', 'doc-1'), backTarget),
    )
    expect(router.push).toHaveBeenNthCalledWith(
      2,
      withBackTarget(buildDocumentFlowPageUrl('pm.invoice', 'doc-1'), backTarget),
    )
    expect(actions.auditOpen.value).toBe(false)
    expect(args.requestNavigate).toHaveBeenNthCalledWith(2, '/documents/pm.invoice')
    expect(args.requestClose).not.toHaveBeenCalled()
  })

  it('builds a restorable compact back target for drawer-origin navigation even when the list url has no panel query', () => {
    const { args, router } = createArgs({
      mode: computed(() => 'drawer' as const),
      route: {
        fullPath: '/documents/pm.invoice?search=late&trash=deleted',
      } as Parameters<typeof useEntityEditorNavigationActions>[0]['route'],
      expandTo: computed(() => '/documents/pm.invoice/doc-1'),
    })
    const actions = useEntityEditorNavigationActions(args)
    const restorableDrawerTarget = buildPathWithQuery('/documents/pm.invoice?search=late&trash=deleted', {
      panel: 'edit',
      id: 'doc-1',
    })

    actions.openDocumentPrintPage()
    actions.openDocumentEffectsPage()
    actions.openDocumentFlowPage()
    actions.openFullPage()

    expect(args.requestNavigate).toHaveBeenNthCalledWith(
      1,
      withBackTarget(
        buildDocumentPrintPageUrl('pm.invoice', 'doc-1', { autoPrint: true }),
        restorableDrawerTarget,
      ),
    )
    expect(router.push).toHaveBeenNthCalledWith(
      1,
      withBackTarget(buildDocumentEffectsPageUrl('pm.invoice', 'doc-1'), restorableDrawerTarget),
    )
    expect(router.push).toHaveBeenNthCalledWith(
      2,
      withBackTarget(buildDocumentFlowPageUrl('pm.invoice', 'doc-1'), restorableDrawerTarget),
    )
    expect(args.requestNavigate).toHaveBeenNthCalledWith(
      2,
      withBackTarget('/documents/pm.invoice/doc-1', restorableDrawerTarget),
    )
  })

  it('handles absent targets, disabled capabilities, and explicit close navigation without side effects', async () => {
    const { args, router } = createArgs({
      currentId: ref(null),
      mode: computed(() => 'page' as const),
      compactTo: computed(() => null),
      expandTo: computed(() => null),
      closeTo: computed(() => '/custom-close'),
      canOpenAudit: computed(() => false),
      canPrintDocument: computed(() => false),
      canOpenDocumentFlowPage: computed(() => false),
      canOpenEffectsPage: computed(() => false),
    })
    const actions = useEntityEditorNavigationActions(args)

    await actions.copyShareLink()
    actions.copyDocument()
    actions.openDocumentPrintPage()
    actions.openAuditLog()
    actions.openDocumentEffectsPage()
    actions.openDocumentFlowPage()
    actions.openFullPage()
    actions.openCompactPage()
    actions.closePage()

    expect(saveDocumentCopyDraftMock).not.toHaveBeenCalled()
    expect(router.push).not.toHaveBeenCalled()
    expect(actions.auditOpen.value).toBe(false)
    expect(args.requestNavigate).toHaveBeenNthCalledWith(1, null)
    expect(args.requestNavigate).toHaveBeenNthCalledWith(2, null)
    expect(args.requestNavigate).toHaveBeenNthCalledWith(3, '/custom-close')
  })

  it('blocks copying independently while loading or saving and rejects catalog copies', () => {
    const loading = createArgs({ loading: ref(true) }).args
    useEntityEditorNavigationActions(loading).copyDocument()

    const saving = createArgs({ saving: ref(true) }).args
    useEntityEditorNavigationActions(saving).copyDocument()

    const catalog = createArgs({ kind: computed(() => 'catalog' as const) }).args
    useEntityEditorNavigationActions(catalog).copyDocument()

    expect(saveDocumentCopyDraftMock).not.toHaveBeenCalled()
  })

  it('copies model keys without metadata or a parts builder into a full-page draft', () => {
    saveDocumentCopyDraftMock.mockReturnValueOnce('page token')
    const { args } = createArgs({
      mode: computed(() => 'page' as const),
      metadata: computed(() => null),
      model: ref({
        title: 'Invoice INV-001',
        display: 'Generated display',
        number: 'INV-001',
      }),
      buildCopyParts: undefined,
    })
    const actions = useEntityEditorNavigationActions(args)

    actions.copyDocument()

    expect(saveDocumentCopyDraftMock).toHaveBeenLastCalledWith({
      documentType: 'pm.invoice',
      fields: { title: 'Invoice INV-001' },
      parts: null,
    })
    expect(args.requestNavigate).toHaveBeenCalledWith(
      `${buildDocumentFullPageUrl('pm.invoice')}?copyDraft=page%20token`,
    )
  })

  it('ignores blank and absent metadata keys plus fields missing from the model', () => {
    saveDocumentCopyDraftMock.mockReturnValueOnce('filtered-token')
    const metadata = createDocumentMetadata()
    metadata.form.sections[0]!.rows[0]!.fields.unshift(
      { isReadOnly: false } as never,
      { key: ' ', isReadOnly: false } as never,
      { key: 'missing', isReadOnly: false } as never,
    )
    const { args } = createArgs({ metadata: computed(() => metadata) })

    useEntityEditorNavigationActions(args).copyDocument()

    expect(saveDocumentCopyDraftMock).toHaveBeenLastCalledWith(expect.objectContaining({
      fields: {
        title: 'Invoice INV-001',
        notes: { internal: 'retain this note' },
      },
    }))
  })

  it('builds related-view back targets for catalog page and drawer modes', () => {
    const pageCase = createArgs({
      kind: computed(() => 'catalog' as const),
      mode: computed(() => 'page' as const),
      typeCode: computed(() => 'pm.property'),
      currentId: ref('property-1'),
      route: { fullPath: '/catalogs/pm.property?search=river' } as never,
    })
    const pageActions = useEntityEditorNavigationActions(pageCase.args)
    pageActions.openDocumentPrintPage()

    const drawerCase = createArgs({
      kind: computed(() => 'catalog' as const),
      mode: computed(() => 'drawer' as const),
      typeCode: computed(() => 'pm.property'),
      currentId: ref('property-1'),
      route: { fullPath: '/catalogs/pm.property?search=river' } as never,
    })
    const drawerActions = useEntityEditorNavigationActions(drawerCase.args)
    drawerActions.openDocumentEffectsPage()

    expect(pageCase.args.requestNavigate).toHaveBeenCalledWith(expect.stringContaining('back='))
    expect(drawerCase.router.push).toHaveBeenCalledWith(
      withBackTarget(
        buildDocumentEffectsPageUrl('pm.property', 'property-1'),
        buildPathWithQuery('/catalogs/pm.property?search=river', {
          panel: 'edit',
          id: 'property-1',
        }),
      ),
    )
  })

  it('restores a new drawer and closes it through the shell callback when no id exists', () => {
    const { args } = createArgs({
      currentId: ref(null),
      expandTo: computed(() => '/documents/pm.invoice/new'),
    })
    const actions = useEntityEditorNavigationActions(args)

    actions.openFullPage()
    actions.closePage()

    expect(args.requestNavigate).toHaveBeenCalledWith(
      withBackTarget(
        '/documents/pm.invoice/new',
        buildPathWithQuery('/documents/pm.invoice?panel=edit&id=doc-1', {
          panel: 'new',
          id: null,
        }),
      ),
    )
    expect(args.requestClose).toHaveBeenCalledOnce()
  })
})
