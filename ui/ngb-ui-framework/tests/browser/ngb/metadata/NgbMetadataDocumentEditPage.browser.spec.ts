import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h, markRaw, ref, type PropType } from 'vue'
import { createMemoryHistory, createRouter, useRoute, useRouter } from 'vue-router'
import { encodeBackTarget } from '../../../../src/ngb/router/backNavigation'

const documentEditMocks = vi.hoisted(() => ({
  buildCompactPageUrl: vi.fn((documentType: string, id?: string | null) =>
    id ? `/documents-compact/${documentType}/${id}` : `/documents-compact/${documentType}/new`),
  copyAppLink: vi.fn(),
  readCopyDraft: vi.fn(),
  saveCopyDraft: vi.fn(),
}))

vi.mock('../../../../src/ngb/editor/documentCopyDraft', () => ({
  readDocumentCopyDraft: documentEditMocks.readCopyDraft,
  saveDocumentCopyDraft: documentEditMocks.saveCopyDraft,
}))

vi.mock('../../../../src/ngb/editor/documentNavigation', async () => {
  const actual = await vi.importActual<typeof import('../../../../src/ngb/editor/documentNavigation')>(
    '../../../../src/ngb/editor/documentNavigation'
  )

  return {
    ...actual,
    buildDocumentCompactPageUrl: documentEditMocks.buildCompactPageUrl,
  }
})

vi.mock('../../../../src/ngb/router/shareLink', () => ({
  copyAppLink: documentEditMocks.copyAppLink,
}))

import NgbMetadataDocumentEditPage from '../../../../src/ngb/metadata/NgbMetadataDocumentEditPage.vue'
import { useEntityEditorNavigationActions } from '../../../../src/ngb/editor/useEntityEditorNavigationActions'

const DocumentEditorPageStub = defineComponent({
  props: {
    kind: {
      type: String,
      default: '',
    },
    typeCode: {
      type: String,
      default: '',
    },
    id: {
      type: String,
      default: undefined,
    },
    mode: {
      type: String,
      default: '',
    },
    canBack: {
      type: Boolean,
      default: false,
    },
    compactTo: {
      type: String,
      default: null,
    },
    closeTo: {
      type: String,
      default: null,
    },
    initialFields: {
      type: Object as PropType<Record<string, unknown> | null>,
      default: null,
    },
    initialParts: {
      type: Object as PropType<Record<string, unknown> | null>,
      default: null,
    },
  },
  setup(props) {
    return () => h('div', { 'data-testid': 'document-edit-editor' }, [
      h('div', `kind:${props.kind}`),
      h('div', `type:${props.typeCode}`),
      h('div', `id:${props.id ?? 'new'}`),
      h('div', `mode:${props.mode}`),
      h('div', `can-back:${String(props.canBack)}`),
      h('div', `compact:${props.compactTo ?? 'none'}`),
      h('div', `close:${props.closeTo ?? 'none'}`),
      h('div', `fields:${JSON.stringify(props.initialFields ?? null)}`),
      h('div', `parts:${JSON.stringify(props.initialParts ?? null)}`),
    ])
  },
})

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
    parts: [
      {
        partCode: 'lines',
        title: 'Lines',
      },
    ],
  }
}

const DocumentEditorNavigationStub = defineComponent({
  props: {
    kind: {
      type: String,
      default: '',
    },
    typeCode: {
      type: String,
      default: '',
    },
    id: {
      type: String,
      default: undefined,
    },
    mode: {
      type: String,
      default: '',
    },
    compactTo: {
      type: String,
      default: null,
    },
    closeTo: {
      type: String,
      default: null,
    },
    initialFields: {
      type: Object as PropType<Record<string, unknown> | null>,
      default: null,
    },
    initialParts: {
      type: Object as PropType<Record<string, unknown> | null>,
      default: null,
    },
  },
  setup(props) {
    const router = useRouter()
    const route = useRoute()
    const metadata = createDocumentMetadata()
    const model = ref({
      title: 'Invoice INV-001',
      notes: {
        internal: 'retain this note',
      },
      display: 'Invoice INV-001',
      number: 'INV-001',
    })
    const requestNavigateCalls = ref<string[]>([])

    const actions = useEntityEditorNavigationActions({
      kind: computed(() => 'document'),
      typeCode: computed(() => props.typeCode),
      mode: computed(() => props.mode as 'page' | 'drawer'),
      compactTo: computed(() => props.compactTo),
      expandTo: computed(() => null),
      closeTo: computed(() => props.closeTo),
      currentId: computed(() => props.id ?? null),
      metadata: computed(() => metadata),
      docMeta: ref(metadata),
      model,
      loading: ref(false),
      saving: ref(false),
      canOpenAudit: computed(() => true),
      canPrintDocument: computed(() => false),
      canOpenDocumentFlowPage: computed(() => false),
      canOpenEffectsPage: computed(() => false),
      requestNavigate: (to) => {
        requestNavigateCalls.value = [...requestNavigateCalls.value, String(to ?? 'null')]
        if (to) void router.push(String(to))
      },
      requestClose: () => {},
      router,
      route,
      toasts: {
        push: vi.fn(),
      },
      buildCopyParts: () => ({
        lines: [{ description: 'Base rent' }],
      }),
    })

    return () => h('div', { 'data-testid': 'document-edit-navigation-editor' }, [
      h('div', `nav-type:${props.typeCode}`),
      h('div', `nav-id:${props.id ?? 'new'}`),
      h('div', `nav-route:${route.fullPath}`),
      h('div', `nav-compact:${props.compactTo ?? 'none'}`),
      h('div', `nav-close:${props.closeTo ?? 'none'}`),
      h('div', `nav-fields:${JSON.stringify(props.initialFields ?? null)}`),
      h('div', `nav-parts:${JSON.stringify(props.initialParts ?? null)}`),
      h('button', {
        type: 'button',
        onClick: () => {
          void actions.copyShareLink()
        },
      }, 'Copy share link'),
      h('button', {
        type: 'button',
        onClick: () => {
          actions.copyDocument()
        },
      }, 'Copy document'),
      h('button', {
        type: 'button',
        onClick: () => {
          actions.closePage()
        },
      }, 'Close page'),
      h('div', { 'data-testid': 'request-navigate-calls' }, requestNavigateCalls.value.join('|') || 'none'),
    ])
  },
})

async function renderDocumentEditPage(initial: string | { path: string; query?: Record<string, unknown> }, props?: Record<string, unknown>) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/documents/:documentType/:id?',
        component: {
          template: '<div />',
        },
      },
      {
        path: '/documents-compact/:documentType/:id?',
        component: {
          template: '<div />',
        },
      },
    ],
  })

  await router.push(initial)
  await router.isReady()

  const view = await render(NgbMetadataDocumentEditPage, {
    props: {
      editorComponent: markRaw(DocumentEditorPageStub),
      ...props,
    },
    global: {
      plugins: [router],
    },
  })

  return {
    router,
    view,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  documentEditMocks.readCopyDraft.mockReturnValue(null)
  documentEditMocks.saveCopyDraft.mockReturnValue('copy-token')
})

test('hydrates new document pages from copy-draft snapshots and default navigation targets', async () => {
  await page.viewport(1280, 900)
  documentEditMocks.readCopyDraft.mockReturnValue({
    fields: {
      number: 'INV-001',
      amount: 1250,
    },
    parts: {
      lines: [{ description: 'Base rent' }],
    },
  })

  const { view } = await renderDocumentEditPage({
    path: '/documents/%20pm.invoice%20',
    query: {
      copyDraft: [' draft-token ', 'ignored'],
    },
  })

  await expect.element(view.getByTestId('document-edit-editor')).toBeVisible()
  await expect.element(view.getByText('kind:document')).toBeVisible()
  await expect.element(view.getByText('type:pm.invoice')).toBeVisible()
  await expect.element(view.getByText('id:new')).toBeVisible()
  await expect.element(view.getByText('mode:page')).toBeVisible()
  await expect.element(view.getByText('can-back:true')).toBeVisible()
  await expect.element(view.getByText('compact:/documents-compact/pm.invoice/new')).toBeVisible()
  await expect.element(view.getByText('close:/documents/pm.invoice')).toBeVisible()
  await expect.element(view.getByText('fields:{"number":"INV-001","amount":1250}')).toBeVisible()
  await expect.element(view.getByText('parts:{"lines":[{"description":"Base rent"}]}')).toBeVisible()

  expect(documentEditMocks.readCopyDraft).toHaveBeenCalledWith('draft-token', 'pm.invoice')
  expect(documentEditMocks.buildCompactPageUrl).toHaveBeenCalledWith('pm.invoice', undefined)
})

test('uses resolver overrides for existing documents and skips copy-draft hydration', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderDocumentEditPage('/documents/%20pm.invoice%20/%20doc-1%20?copyDraft=ignored', {
    canBack: false,
    resolveCompactTo: (documentType: string, id?: string | null) => `/custom-doc-compact/${documentType}/${id ?? 'draft'}`,
    resolveCloseTo: (documentType: string) => `/custom-doc-close/${documentType}`,
  })

  await expect.element(view.getByText('type:pm.invoice')).toBeVisible()
  await expect.element(view.getByText('id:doc-1')).toBeVisible()
  await expect.element(view.getByText('can-back:false')).toBeVisible()
  await expect.element(view.getByText('compact:/custom-doc-compact/pm.invoice/doc-1')).toBeVisible()
  await expect.element(view.getByText('close:/custom-doc-close/pm.invoice')).toBeVisible()
  await expect.element(view.getByText('fields:null')).toBeVisible()
  await expect.element(view.getByText('parts:null')).toBeVisible()

  expect(documentEditMocks.readCopyDraft).not.toHaveBeenCalled()
  expect(documentEditMocks.buildCompactPageUrl).not.toHaveBeenCalled()
})

test('reuses the exact compact source route when the full page was opened from a drawer context', async () => {
  await page.viewport(1280, 900)

  const compactSource = '/documents-compact/pm.invoice/doc-1?search=late&panel=edit&id=doc-1&trash=deleted'
  const encodedBack = encodeBackTarget(compactSource)

  const { view } = await renderDocumentEditPage(`/documents/pm.invoice/doc-1?back=${encodedBack}`)

  await expect.element(view.getByText(`compact:${compactSource}`)).toBeVisible()
  expect(documentEditMocks.buildCompactPageUrl).toHaveBeenCalledWith('pm.invoice', 'doc-1')
})

test('treats blank copy-draft values as empty snapshots for new documents', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderDocumentEditPage({
    path: '/documents/%20pm.invoice%20',
    query: {
      copyDraft: '   ',
    },
  })

  await expect.element(view.getByText('type:pm.invoice')).toBeVisible()
  await expect.element(view.getByText('id:new')).toBeVisible()
  await expect.element(view.getByText('compact:/documents-compact/pm.invoice/new')).toBeVisible()
  await expect.element(view.getByText('close:/documents/pm.invoice')).toBeVisible()
  await expect.element(view.getByText('fields:null')).toBeVisible()
  await expect.element(view.getByText('parts:null')).toBeVisible()

  expect(documentEditMocks.buildCompactPageUrl).toHaveBeenCalledWith('pm.invoice', undefined)
})

test('falls back to safe navigation targets when the document type normalizes to empty', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderDocumentEditPage({
    path: '/documents/%20%20',
    query: {
      copyDraft: 'draft-token',
    },
  })

  await expect.element(view.getByTestId('document-edit-editor')).toBeVisible()
  await expect.element(view.getByText('id:new')).toBeVisible()
  await expect.element(view.getByText('compact:none')).toBeVisible()
  await expect.element(view.getByText('close:/')).toBeVisible()
  await expect.element(view.getByText('fields:null')).toBeVisible()
  await expect.element(view.getByText('parts:null')).toBeVisible()

  expect(documentEditMocks.readCopyDraft).not.toHaveBeenCalled()
  expect(documentEditMocks.buildCompactPageUrl).not.toHaveBeenCalled()
})

test('drops stale copy-draft props when the full-page route is reused for an existing document', async () => {
  await page.viewport(1280, 900)
  documentEditMocks.readCopyDraft.mockReturnValue({
    fields: {
      number: 'INV-DRAFT',
      amount: 900,
    },
    parts: {
      lines: [{ description: 'Draft line' }],
    },
  })

  const { router, view } = await renderDocumentEditPage({
    path: '/documents/pm.invoice',
    query: {
      copyDraft: 'draft-token',
    },
  })

  await expect.element(view.getByText('id:new')).toBeVisible()
  await expect.element(view.getByText('fields:{"number":"INV-DRAFT","amount":900}')).toBeVisible()
  await expect.element(view.getByText('parts:{"lines":[{"description":"Draft line"}]}')).toBeVisible()

  await router.push('/documents/pm.invoice/doc-2')

  await expect.element(view.getByText('id:doc-2')).toBeVisible()
  await expect.element(view.getByText('compact:/documents-compact/pm.invoice/doc-2')).toBeVisible()
  await expect.element(view.getByText('close:/documents/pm.invoice')).toBeVisible()
  await expect.element(view.getByText('fields:null')).toBeVisible()
  await expect.element(view.getByText('parts:null')).toBeVisible()

  expect(documentEditMocks.readCopyDraft).toHaveBeenCalledTimes(1)
  expect(documentEditMocks.buildCompactPageUrl).toHaveBeenLastCalledWith('pm.invoice', 'doc-2')
})

test('treats the full-page /new route as a create page and hydrates copy drafts', async () => {
  await page.viewport(1280, 900)
  documentEditMocks.readCopyDraft.mockReturnValue({
    fields: {
      title: 'Copied invoice',
    },
    parts: {
      lines: [{ description: 'Copied line' }],
    },
  })

  const { view } = await renderDocumentEditPage('/documents/pm.invoice/new?copyDraft=copy-token')

  await expect.element(view.getByTestId('document-edit-editor')).toBeVisible()
  await expect.element(view.getByText('type:pm.invoice')).toBeVisible()
  await expect.element(view.getByText('id:new')).toBeVisible()
  await expect.element(view.getByText('compact:/documents-compact/pm.invoice/new')).toBeVisible()
  await expect.element(view.getByText('close:/documents/pm.invoice')).toBeVisible()
  await expect.element(view.getByText('fields:{"title":"Copied invoice"}')).toBeVisible()
  await expect.element(view.getByText('parts:{"lines":[{"description":"Copied line"}]}')).toBeVisible()

  expect(documentEditMocks.readCopyDraft).toHaveBeenCalledWith('copy-token', 'pm.invoice')
  expect(documentEditMocks.buildCompactPageUrl).toHaveBeenCalledWith('pm.invoice', undefined)
})

test('runs an integrated full-page copy-draft flow through page navigation actions', async () => {
  await page.viewport(1280, 900)
  documentEditMocks.readCopyDraft.mockImplementation((token: string | null) => {
    if (token !== 'copy-token') return null
    return {
      fields: {
        title: 'Copied invoice',
      },
      parts: {
        lines: [{ description: 'Copied line' }],
      },
    }
  })

  const { router, view } = await renderDocumentEditPage('/documents/pm.invoice/doc-1', {
    editorComponent: markRaw(DocumentEditorNavigationStub),
  })

  await expect.element(view.getByTestId('document-edit-navigation-editor')).toBeVisible()
  await expect.element(view.getByText('nav-id:doc-1')).toBeVisible()
  await expect.element(view.getByText('nav-fields:null')).toBeVisible()

  await view.getByRole('button', { name: 'Copy share link' }).click()
  await vi.waitFor(() => {
    expect(documentEditMocks.copyAppLink).toHaveBeenCalledWith(
      expect.any(Object),
      expect.any(Object),
      '/documents/pm.invoice/doc-1',
    )
  })

  await view.getByRole('button', { name: 'Copy document' }).click()
  await vi.waitFor(() => {
    expect(router.currentRoute.value.fullPath).toBe('/documents/pm.invoice/new?copyDraft=copy-token')
  })

  expect(documentEditMocks.saveCopyDraft).toHaveBeenCalledWith({
    documentType: 'pm.invoice',
    fields: {
      title: 'Invoice INV-001',
      notes: {
        internal: 'retain this note',
      },
    },
    parts: {
      lines: [{ description: 'Base rent' }],
    },
  })

  await expect.element(view.getByText('nav-id:new')).toBeVisible()
  await expect.element(view.getByText('nav-fields:{"title":"Copied invoice"}')).toBeVisible()
  await expect.element(view.getByText('nav-parts:{"lines":[{"description":"Copied line"}]}')).toBeVisible()
  await expect.element(view.getByText('nav-close:/documents/pm.invoice')).toBeVisible()

  await view.getByRole('button', { name: 'Close page' }).click()
  await vi.waitFor(() => {
    expect(router.currentRoute.value.fullPath).toBe('/documents/pm.invoice')
  })

  await expect.element(view.getByText('nav-id:new')).toBeVisible()
  await expect.element(view.getByText('nav-fields:null')).toBeVisible()
  await expect.element(view.getByText('nav-parts:null')).toBeVisible()
})
