import { page } from 'vitest/browser'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

import { encodeBackTarget, withBackTarget } from '../../../../src/ngb/router/backNavigation'
import {
  StubBadge,
  StubIcon,
  StubPageHeader,
  StubStatusIcon,
} from './stubs'

const mocks = vi.hoisted(() => ({
  route: {
    params: {
      documentType: 'pm.invoice',
      id: 'doc-1',
    },
    query: {},
    fullPath: '/documents/pm.invoice/doc-1/flow',
  },
  router: {
    push: vi.fn(),
    replace: vi.fn(),
    back: vi.fn(),
  },
  toasts: {
    push: vi.fn(),
  },
  editorConfig: {
    loadDocumentGraph: vi.fn(),
  },
  copyAppLink: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => mocks.route,
  useRouter: () => mocks.router,
}))

vi.mock('../../../../src/ngb/primitives/toast', () => ({
  useToasts: () => mocks.toasts,
}))

vi.mock('../../../../src/ngb/editor/config', async () => {
  const actual = await vi.importActual('../../../../src/ngb/editor/config')
  return {
    ...actual,
    getConfiguredNgbEditor: () => mocks.editorConfig,
  }
})

vi.mock('../../../../src/ngb/router/shareLink', () => ({
  copyAppLink: mocks.copyAppLink,
}))

vi.mock('../../../../src/ngb/site/NgbPageHeader.vue', () => ({
  default: StubPageHeader,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/primitives/NgbStatusIcon.vue', () => ({
  default: StubStatusIcon,
}))

import NgbDocumentFlowPage from '../../../../src/ngb/editor/NgbDocumentFlowPage.vue'

function top(locator: { element(): Element }): number {
  return locator.element().getBoundingClientRect().top
}

describe('NgbDocumentFlowPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    mocks.route.params.documentType = 'pm.invoice'
    mocks.route.params.id = 'doc-1'
    mocks.route.query = {}
    mocks.route.fullPath = '/documents/pm.invoice/doc-1/flow'

    mocks.editorConfig.loadDocumentGraph.mockResolvedValue({
      nodes: [
        {
          nodeId: 'root',
          kind: 2,
          typeCode: 'pm.invoice',
          entityId: 'doc-1',
          title: 'Invoice INV-001',
          subtitle: '2026-04-08T12:00:00.000Z',
          documentStatus: 2,
          amount: 1250,
        },
        {
          nodeId: 'child',
          kind: 2,
          typeCode: 'pm.receipt',
          entityId: 'doc-2',
          title: 'Receipt RCPT-001',
          subtitle: '2026-04-09T09:00:00.000Z',
          documentStatus: 2,
          amount: 1250,
        },
      ],
      edges: [
        {
          fromNodeId: 'root',
          toNodeId: 'child',
          relationshipType: 'created_from',
        },
      ],
    })
  })

  it('renders a document flow tree and opens related documents with a back trail', async () => {
    await page.viewport(1280, 900)

    const view = await render(NgbDocumentFlowPage)
    const relatedNode = view.getByTitle('Receipt RCPT-001')

    await expect.element(view.getByRole('heading', { name: 'Invoice INV-001' })).toBeVisible()
    await expect.element(relatedNode).toBeVisible()
    expect(relatedNode.element().textContent ?? '').toMatch(/1,250.00/)

    await relatedNode.click()
    expect(mocks.router.push).toHaveBeenCalledWith(
      withBackTarget('/documents/pm.receipt/doc-2', mocks.route.fullPath),
    )

    await view.getByRole('button', { name: 'Share link' }).click()
    expect(mocks.copyAppLink).toHaveBeenCalledWith(
      mocks.router,
      mocks.toasts,
      '/documents/pm.invoice/doc-1/flow',
      {
        title: 'Document flow link copied',
        message: 'Shareable document flow page link copied to clipboard.',
      },
    )
  })

  it('shows an empty state when there are no related documents', async () => {
    mocks.editorConfig.loadDocumentGraph.mockResolvedValueOnce({
      nodes: [],
      edges: [],
    })

    const view = await render(NgbDocumentFlowPage)

    await expect.element(view.getByText('This document has no related documents yet.')).toBeVisible()
  })

  it('prefers the explicit back target when reopening the source document from the flow page', async () => {
    const reportBackTarget = '/reports/pm.occupancy.summary?variant=review'
    const encodedBack = encodeBackTarget(reportBackTarget)

    mocks.route.query = {
      back: encodedBack,
    }
    mocks.route.fullPath = `/documents/pm.invoice/doc-1/flow?back=${encodedBack}`

    const view = await render(NgbDocumentFlowPage)

    await expect.element(view.getByRole('heading', { name: 'Invoice INV-001' })).toBeVisible()
    await view.getByRole('button', { name: 'Open document' }).click()

    expect(mocks.router.push).toHaveBeenCalledWith(reportBackTarget)
  })

  it('unwraps a compact-source target from nested full-page back trails for both back and edit actions', async () => {
    const compactSource = '/documents/pm.invoice?search=late&panel=edit&id=doc-1&trash=deleted'
    const nestedDocumentRoute = withBackTarget('/documents/pm.invoice/doc-1', compactSource)
    const encodedBack = encodeBackTarget(nestedDocumentRoute)

    mocks.route.query = {
      back: encodedBack,
    }
    mocks.route.fullPath = `/documents/pm.invoice/doc-1/flow?back=${encodedBack}`

    const view = await render(NgbDocumentFlowPage)

    await expect.element(view.getByRole('heading', { name: 'Invoice INV-001' })).toBeVisible()
    await view.getByRole('button', { name: 'Back' }).click()
    await view.getByRole('button', { name: 'Open document' }).click()

    expect(mocks.router.replace).toHaveBeenCalledWith(compactSource)
    expect(mocks.router.push).toHaveBeenCalledWith(compactSource)
  })

  it('renders sibling flow nodes in date order so the tree stays stable across refreshes', async () => {
    await page.viewport(1280, 900)

    mocks.editorConfig.loadDocumentGraph.mockResolvedValueOnce({
      nodes: [
        {
          nodeId: 'root',
          kind: 2,
          typeCode: 'pm.invoice',
          entityId: 'doc-1',
          title: 'Invoice INV-001',
          subtitle: '2026-04-08T12:00:00.000Z',
          documentStatus: 2,
          amount: 1250,
        },
        {
          nodeId: 'late',
          kind: 2,
          typeCode: 'pm.credit-note',
          entityId: 'doc-3',
          title: 'Credit Note CN-001',
          subtitle: '2026-04-11T08:30:00.000Z',
          documentStatus: 2,
          amount: 75,
        },
        {
          nodeId: 'early',
          kind: 2,
          typeCode: 'pm.receipt',
          entityId: 'doc-2',
          title: 'Receipt RCPT-001',
          subtitle: '2026-04-07T09:00:00.000Z',
          documentStatus: 2,
          amount: 1250,
        },
      ],
      edges: [
        {
          fromNodeId: 'root',
          toNodeId: 'late',
          relationshipType: 'created_from',
        },
        {
          fromNodeId: 'root',
          toNodeId: 'early',
          relationshipType: 'created_from',
        },
      ],
    })

    const view = await render(NgbDocumentFlowPage)
    const earlyNode = view.getByTitle('Receipt RCPT-001')
    const lateNode = view.getByTitle('Credit Note CN-001')

    await expect.element(earlyNode).toBeVisible()
    await expect.element(lateNode).toBeVisible()
    expect(top(earlyNode)).toBeLessThan(top(lateNode))
  })

  it('shows a recoverable error state when the flow request fails', async () => {
    mocks.editorConfig.loadDocumentGraph.mockRejectedValueOnce(new Error('Graph service offline'))

    const view = await render(NgbDocumentFlowPage)

    await expect.element(view.getByText('Graph service offline')).toBeVisible()

    await view.getByRole('button', { name: 'Refresh' }).click()
    await expect.element(view.getByRole('heading', { name: 'Invoice INV-001' })).toBeVisible()
    expect(mocks.editorConfig.loadDocumentGraph).toHaveBeenCalledTimes(2)
  })

  it('stays stable with cyclic duplicate graph edges and falls back to the first available root when the current document is missing', async () => {
    await page.viewport(1280, 900)

    mocks.route.params.id = 'missing-doc'
    mocks.route.fullPath = '/documents/pm.invoice/missing-doc/flow'

    mocks.editorConfig.loadDocumentGraph.mockResolvedValueOnce({
      nodes: [
        {
          nodeId: 'fallback-root',
          kind: 2,
          typeCode: 'pm.invoice',
          entityId: 'doc-9',
          title: 'Fallback Invoice INV-099',
          subtitle: '2026-04-08T12:00:00.000Z',
          documentStatus: 2,
          amount: 1250,
        },
        {
          nodeId: 'late',
          kind: 2,
          typeCode: 'pm.credit-note',
          entityId: 'doc-3',
          title: 'Credit Note CN-001',
          subtitle: '2026-04-11T08:30:00.000Z',
          documentStatus: 2,
          amount: 75,
        },
        {
          nodeId: 'early',
          kind: 2,
          typeCode: 'pm.receipt',
          entityId: 'doc-2',
          title: 'Receipt RCPT-001',
          subtitle: '2026-04-07T09:00:00.000Z',
          documentStatus: 2,
          amount: 1250,
        },
      ],
      edges: [
        {
          fromNodeId: 'fallback-root',
          toNodeId: 'late',
          relationshipType: 'created_from',
        },
        {
          fromNodeId: 'fallback-root',
          toNodeId: 'late',
          relationshipType: 'created_from',
        },
        {
          fromNodeId: 'fallback-root',
          toNodeId: 'early',
          relationshipType: 'created_from',
        },
        {
          fromNodeId: 'late',
          toNodeId: 'early',
          relationshipType: 'related_to',
        },
        {
          fromNodeId: 'early',
          toNodeId: 'fallback-root',
          relationshipType: 'supersedes',
        },
        {
          fromNodeId: 'ghost',
          toNodeId: 'early',
          relationshipType: 'created_from',
        },
      ],
    })

    const view = await render(NgbDocumentFlowPage)
    const earlyNode = view.getByTitle('Receipt RCPT-001')
    const lateNode = view.getByTitle('Credit Note CN-001')

    await expect.element(view.getByRole('heading', { name: 'Fallback Invoice INV-099' })).toBeVisible()
    await expect.element(earlyNode).toBeVisible()
    await expect.element(lateNode).toBeVisible()
    expect(top(earlyNode)).toBeLessThan(top(lateNode))
    expect(document.querySelectorAll('button[title="Receipt RCPT-001"]').length).toBe(1)
    expect(document.querySelectorAll('button[title="Credit Note CN-001"]').length).toBe(1)
  })
})
