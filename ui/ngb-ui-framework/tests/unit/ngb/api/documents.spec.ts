import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpMocks = vi.hoisted(() => ({
  httpDelete: vi.fn(),
  httpGet: vi.fn(),
  httpPost: vi.fn(),
  httpPut: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpDelete: httpMocks.httpDelete,
  httpGet: httpMocks.httpGet,
  httpPost: httpMocks.httpPost,
  httpPut: httpMocks.httpPut,
}))

import {
  createDraft,
  deleteDraft,
  executeDocumentAction,
  getDocumentById,
  getDocumentEffects,
  getDocumentEditorState,
  getDocumentGraph,
  getDocumentLookupByIds,
  getDocumentPage,
  getDocumentTypeMetadata,
  lookupDocumentsAcrossTypes,
  updateDraft,
} from '../../../../src/ngb/api/documents'

function createDocument(status: number | string) {
  return {
    id: 'doc-1',
    display: 'Invoice 1',
    payload: {
      fields: {
        memo: 'Test',
      },
    },
    status,
    isMarkedForDeletion: false,
  }
}

describe('documents api', () => {
  beforeEach(() => {
    httpMocks.httpDelete.mockReset()
    httpMocks.httpGet.mockReset()
    httpMocks.httpPost.mockReset()
    httpMocks.httpPut.mockReset()
  })

  it('loads metadata, document pages, and records while normalizing document statuses', async () => {
    httpMocks.httpGet
      .mockResolvedValueOnce({ code: 'pm.invoice' })
      .mockResolvedValueOnce({
        items: [
          createDocument('posted'),
          createDocument('marked-for-deletion'),
        ],
        offset: 10,
        limit: 20,
      })
      .mockResolvedValueOnce(createDocument('draft'))

    await getDocumentTypeMetadata('pm.invoice')
    const page = await getDocumentPage('pm.invoice', {
      offset: 10,
      limit: 20,
      search: 'INV',
      filters: {
        trash: 'all',
      },
    })
    const document = await getDocumentById('pm.invoice', 'doc/1')

    expect(page.items.map((item) => item.status)).toEqual([2, 3])
    expect(document.status).toBe(1)
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      1,
      '/api/documents/pm.invoice/metadata',
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      2,
      '/api/documents/pm.invoice',
      {
        offset: 10,
        limit: 20,
        search: 'INV',
        trash: 'all',
      },
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      3,
      '/api/documents/pm.invoice/doc%2F1',
    )
  })

  it('preserves canonical statuses and tolerates omitted paging data from the transport', async () => {
    const canonicalDocument = createDocument(2)
    const canonicalLookup = {
      id: 'doc-2',
      display: 'Invoice 2',
      documentType: 'pm.invoice',
      status: 2,
      isMarkedForDeletion: false,
      number: 'INV-002',
    }
    httpMocks.httpGet
      .mockResolvedValueOnce({ items: undefined, offset: 0, limit: 20 })
      .mockResolvedValueOnce({ items: [canonicalDocument], offset: 0, limit: 1 })
      .mockResolvedValueOnce(canonicalDocument)
    httpMocks.httpPost.mockResolvedValueOnce([canonicalLookup])

    const emptyPage = await getDocumentPage('pm.invoice', null as never)
    const page = await getDocumentPage('pm.invoice', { offset: 0, limit: 1 })
    const document = await getDocumentById('pm.invoice', 'doc-1')
    const lookups = await getDocumentLookupByIds({
      documentTypes: ['pm.invoice'],
      ids: ['doc-2'],
    })

    expect(emptyPage.items).toEqual([])
    expect(page.items[0]).toBe(canonicalDocument)
    expect(document).toBe(canonicalDocument)
    expect(lookups[0]).toBe(canonicalLookup)
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(1, '/api/documents/pm.invoice', undefined)
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(2, '/api/documents/pm.invoice', {
      offset: 0,
      limit: 1,
      search: undefined,
    })
  })

  it('loads unified editor state and sends idempotent action commands with the expected version', async () => {
    httpMocks.httpGet.mockResolvedValueOnce({
      document: createDocument('posted'),
      documentVersion: 4,
      actions: [],
    })
    httpMocks.httpPost.mockResolvedValueOnce({
      executionId: 'execution-1',
      actionCode: 'crm.create_qualification',
      document: createDocument('posted'),
      documentVersion: 5,
      actions: [],
      workCenterMayChange: true,
      createdDocument: createDocument('draft'),
    })

    const state = await getDocumentEditorState('crm.lead_intake', 'doc/1')
    const result = await executeDocumentAction(
      'crm.lead_intake',
      'doc/1',
      'crm.create_qualification',
      { expectedVersion: state.documentVersion },
      'idem-1',
    )

    expect(state.document.status).toBe(2)
    expect(result.createdDocument?.status).toBe(1)
    expect(httpMocks.httpGet).toHaveBeenCalledWith(
      '/api/documents/crm.lead_intake/doc%2F1/editor-state',
    )
    expect(httpMocks.httpPost).toHaveBeenCalledWith(
      '/api/documents/crm.lead_intake/doc%2F1/actions/crm.create_qualification',
      { expectedVersion: 4 },
      { headers: { 'Idempotency-Key': 'idem-1' } },
    )
  })

  it('deduplicates concurrent editor-state reads and releases failed requests for retry', async () => {
    let resolveRequest!: (value: ReturnType<typeof createDocument> & Record<string, never>) => void
    const pendingDocument = new Promise<ReturnType<typeof createDocument> & Record<string, never>>((resolve) => {
      resolveRequest = resolve
    })
    httpMocks.httpGet.mockReturnValueOnce(
      pendingDocument.then((document) => ({
        document,
        documentVersion: 1,
        actions: [],
      })),
    )

    const first = getDocumentEditorState('pm.invoice', 'same-id')
    const second = getDocumentEditorState('pm.invoice', 'same-id')
    expect(httpMocks.httpGet).toHaveBeenCalledTimes(1)

    resolveRequest(createDocument('draft') as never)
    await expect(first).resolves.toMatchObject({ documentVersion: 1 })
    await expect(second).resolves.toMatchObject({ documentVersion: 1 })

    httpMocks.httpGet
      .mockRejectedValueOnce(new Error('temporary failure'))
      .mockResolvedValueOnce({
        document: createDocument('draft'),
        documentVersion: 2,
        actions: [],
      })
    await expect(getDocumentEditorState('pm.invoice', 'retry-id')).rejects.toThrow('temporary failure')
    await expect(getDocumentEditorState('pm.invoice', 'retry-id')).resolves.toMatchObject({ documentVersion: 2 })
  })

  it('uses a generated idempotency key and preserves an absent created document', async () => {
    httpMocks.httpPost.mockResolvedValueOnce({
      executionId: 'execution-2',
      actionCode: 'post',
      document: createDocument('posted'),
      documentVersion: 2,
      actions: [],
      workCenterMayChange: false,
      createdDocument: null,
    })

    const result = await executeDocumentAction(
      'pm.invoice',
      'doc-1',
      'post',
      { expectedVersion: 1, reason: null },
    )

    expect(result.createdDocument).toBeNull()
    expect(httpMocks.httpPost).toHaveBeenCalledWith(
      '/api/documents/pm.invoice/doc-1/actions/post',
      { expectedVersion: 1, reason: null },
      { headers: { 'Idempotency-Key': expect.any(String) } },
    )
  })

  it('normalizes document statuses across draft persistence operations', async () => {
    const payload = {
      fields: {
        memo: 'Test',
      },
    }

    httpMocks.httpPost
      .mockResolvedValueOnce(createDocument('draft'))
    httpMocks.httpPut.mockResolvedValueOnce(createDocument('posted'))
    httpMocks.httpDelete.mockResolvedValueOnce(undefined)

    expect((await createDraft('pm.invoice', payload)).status).toBe(1)
    expect((await updateDraft('pm.invoice', 'doc/1', payload)).status).toBe(2)
    await deleteDraft('pm.invoice', 'doc/1')

    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(1, '/api/documents/pm.invoice', payload)
    expect(httpMocks.httpPut).toHaveBeenCalledWith('/api/documents/pm.invoice/doc%2F1', payload)
    expect(httpMocks.httpDelete).toHaveBeenCalledWith('/api/documents/pm.invoice/doc%2F1')
  })

  it('posts bulk lookup requests for cross-type search and by-id resolution while normalizing statuses', async () => {
    httpMocks.httpPost
      .mockResolvedValueOnce([{
        id: 'doc-1',
        display: 'Invoice 1',
        documentType: 'pm.invoice',
        status: 'posted',
        isMarkedForDeletion: false,
        number: 'INV-001',
      }])
      .mockResolvedValueOnce([{
        id: 'doc-2',
        display: 'Credit Memo 2',
        documentType: 'pm.credit_note',
        status: 'marked-for-deletion',
        isMarkedForDeletion: true,
        number: 'CM-002',
      }])

    const searchItems = await lookupDocumentsAcrossTypes({
      documentTypes: ['pm.invoice', 'pm.credit_note'],
      query: 'invoice',
      perTypeLimit: 25,
      activeOnly: true,
    })
    const byIdItems = await getDocumentLookupByIds({
      documentTypes: ['pm.invoice', 'pm.credit_note'],
      ids: ['doc-2'],
    })

    expect(searchItems).toEqual([{
      id: 'doc-1',
      display: 'Invoice 1',
      documentType: 'pm.invoice',
      status: 2,
      isMarkedForDeletion: false,
      number: 'INV-001',
    }])
    expect(byIdItems).toEqual([{
      id: 'doc-2',
      display: 'Credit Memo 2',
      documentType: 'pm.credit_note',
      status: 3,
      isMarkedForDeletion: true,
      number: 'CM-002',
    }])

    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      1,
      '/api/documents/lookup',
      {
        documentTypes: ['pm.invoice', 'pm.credit_note'],
        query: 'invoice',
        perTypeLimit: 25,
        activeOnly: true,
      },
    )
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(
      2,
      '/api/documents/lookup/by-ids',
      {
        documentTypes: ['pm.invoice', 'pm.credit_note'],
        ids: ['doc-2'],
      },
    )
  })

  it('loads effects and relationship graphs with explicit paging defaults', async () => {
    httpMocks.httpGet
      .mockResolvedValueOnce({ accountingEntries: [], operationalRegisterMovements: [], referenceRegisterWrites: [] })
      .mockResolvedValueOnce({ nodes: [], edges: [] })

    await getDocumentEffects('pm.invoice', 'doc/1', 750)
    await getDocumentGraph('pm.invoice', 'doc/1', 6, 150)

    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      1,
      '/api/documents/pm.invoice/doc%2F1/effects',
      { limit: 750 },
    )
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(
      2,
      '/api/documents/pm.invoice/doc%2F1/graph',
      { depth: 6, maxNodes: 150 },
    )
  })
})
