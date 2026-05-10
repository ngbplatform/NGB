import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';
import type { PageQuery } from './catalogsClient.ts';
import { toPageQuery } from './catalogsClient.ts';

export class DocumentsClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  listMetadata(): NgbHttpResponse {
    return this.http.get('/api/documents/metadata', {
      tags: {
        vertical: this.env.vertical,
        area: 'metadata',
        operation: 'platform.documents.metadata.list',
      },
    });
  }

  getDocumentMetadata(documentType: string): NgbHttpResponse {
    return this.http.get(`/api/documents/${encodeURIComponent(documentType)}/metadata`, {
      tags: {
        vertical: this.env.vertical,
        area: 'metadata',
        operation: 'platform.documents.metadata.get',
        documentType,
      },
    });
  }

  listDocuments(documentType: string, query: PageQuery = {}): NgbHttpResponse {
    return this.http.get(`/api/documents/${encodeURIComponent(documentType)}`, {
      query: toPageQuery(query),
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.list',
        documentType,
      },
    });
  }

  openDocument(documentType: string, documentId: string): NgbHttpResponse {
    return this.http.get(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}`, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.open',
        documentType,
      },
    });
  }

  createDocument(documentType: string, payload: Record<string, unknown>): NgbHttpResponse {
    return this.http.post(`/api/documents/${encodeURIComponent(documentType)}`, payload, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.create',
        documentType,
      },
    });
  }

  postDocument(documentType: string, documentId: string): NgbHttpResponse {
    return this.http.post(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/post`, undefined, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.post',
        documentType,
      },
    });
  }

  unpostDocument(documentType: string, documentId: string): NgbHttpResponse {
    return this.http.post(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/unpost`, undefined, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.unpost',
        documentType,
      },
    });
  }

  getAccountingEffects(documentType: string, documentId: string, limit = 500): NgbHttpResponse {
    return this.http.get(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/effects`, {
      query: { limit },
      tags: {
        vertical: this.env.vertical,
        area: 'accounting',
        operation: 'platform.accounting_effects.read',
        documentType,
      },
    });
  }

  getDocumentFlow(documentType: string, documentId: string, depth = 5, maxNodes = 100): NgbHttpResponse {
    return this.http.get(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/graph`, {
      query: { depth, maxNodes },
      tags: {
        vertical: this.env.vertical,
        area: 'document-flow',
        operation: 'platform.document_flow.read',
        documentType,
      },
    });
  }
}
