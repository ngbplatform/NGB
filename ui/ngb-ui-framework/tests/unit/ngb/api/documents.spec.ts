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
  getDocumentById,
  getDocumentDerivationActions,
  getDocumentEffects,
  getDocumentGraph,
  getDocumentLookupByIds,
  getDocumentPage,
  getDocumentTypeMetadata,
  lookupDocumentsAcrossTypes,
  markDocumentForDeletion,
  postDocument,
  unmarkDocumentForDeletion,
  unpostDocument,
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

  it('loads derive actions for a specific document', async () => {
    httpMocks.httpGet.mockResolvedValueOnce([
      {
        code: 'ab.generate_invoice_draft',
        name: 'Generate Invoice Draft',
        fromTypeCode: 'ab.timesheet',
        toTypeCode: 'ab.sales_invoice',
        relationshipCodes: ['created_from'],
      },
    ])

    await expect(getDocumentDerivationActions('ab.timesheet', 'doc/1')).resolves.toEqual([
      {
        code: 'ab.generate_invoice_draft',
        name: 'Generate Invoice Draft',
        fromTypeCode: 'ab.timesheet',
        toTypeCode: 'ab.sales_invoice',
        relationshipCodes: ['created_from'],
      },
    ])

    expect(httpMocks.httpGet).toHaveBeenCalledWith('/api/documents/ab.timesheet/doc%2F1/derive-actions')
  })

  it('normalizes document statuses across draft, posting, and deletion mutations', async () => {
    const payload = {
      fields: {
        memo: 'Test',
      },
    }

    httpMocks.httpPost
      .mockResolvedValueOnce(createDocument('draft'))
      .mockResolvedValueOnce(createDocument('posted'))
      .mockResolvedValueOnce(createDocument('draft'))
      .mockResolvedValueOnce(createDocument('marked_for_deletion'))
      .mockResolvedValueOnce(createDocument(1))
    httpMocks.httpPut.mockResolvedValueOnce(createDocument('posted'))
    httpMocks.httpDelete.mockResolvedValueOnce(undefined)

    expect((await createDraft('pm.invoice', payload)).status).toBe(1)
    expect((await updateDraft('pm.invoice', 'doc/1', payload)).status).toBe(2)
    expect((await postDocument('pm.invoice', 'doc/1')).status).toBe(2)
    expect((await unpostDocument('pm.invoice', 'doc/1')).status).toBe(1)
    expect((await markDocumentForDeletion('pm.invoice', 'doc/1')).status).toBe(3)
    expect((await unmarkDocumentForDeletion('pm.invoice', 'doc/1')).status).toBe(1)
    await deleteDraft('pm.invoice', 'doc/1')

    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(1, '/api/documents/pm.invoice', payload)
    expect(httpMocks.httpPut).toHaveBeenCalledWith('/api/documents/pm.invoice/doc%2F1', payload)
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(2, '/api/documents/pm.invoice/doc%2F1/post')
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(3, '/api/documents/pm.invoice/doc%2F1/unpost')
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(4, '/api/documents/pm.invoice/doc%2F1/mark-for-deletion')
    expect(httpMocks.httpPost).toHaveBeenNthCalledWith(5, '/api/documents/pm.invoice/doc%2F1/unmark-for-deletion')
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
