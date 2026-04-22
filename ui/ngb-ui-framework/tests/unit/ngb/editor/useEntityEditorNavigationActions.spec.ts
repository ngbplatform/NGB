import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import type { Router } from 'vue-router'

import {
  buildDocumentEffectsPageUrl,
  buildDocumentFlowPageUrl,
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
})
