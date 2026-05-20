import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';
import type { NgbRequestTags } from '../core/requestTags.ts';
import type { PageQuery } from './catalogsClient.ts';
import { toPageQuery } from './catalogsClient.ts';

export interface DocumentLookupRequest {
  readonly documentTypes: readonly string[];
  readonly query?: string | null;
  readonly perTypeLimit?: number;
  readonly activeOnly?: boolean;
}

export interface DocumentLookupByIdsRequest {
  readonly documentTypes: readonly string[];
  readonly ids: readonly string[];
}

export interface DocumentDeriveRequest {
  readonly sourceDocumentId: string;
  readonly relationshipType: string;
  readonly initialPayload?: Record<string, unknown> | null;
}

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

  updateDocument(documentType: string, documentId: string, payload: Record<string, unknown>): NgbHttpResponse {
    return this.http.put(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}`, payload, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.update',
        documentType,
      },
    });
  }

  deleteDraft(documentType: string, documentId: string): NgbHttpResponse {
    return this.http.delete(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}`, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.delete_draft',
        documentType,
      },
      expectedStatuses: [200, 202, 204],
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

  repostDocument(documentType: string, documentId: string): NgbHttpResponse {
    return this.http.post(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/repost`, undefined, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.repost',
        documentType,
      },
    });
  }

  markForDeletion(documentType: string, documentId: string): NgbHttpResponse {
    return this.http.post(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/mark-for-deletion`, undefined, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.mark_for_deletion',
        documentType,
      },
    });
  }

  unmarkForDeletion(documentType: string, documentId: string): NgbHttpResponse {
    return this.http.post(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/unmark-for-deletion`, undefined, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.unmark_for_deletion',
        documentType,
      },
    });
  }

  executeAction(documentType: string, documentId: string, actionCode: string): NgbHttpResponse {
    return this.http.post(
      `/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/actions/${encodeURIComponent(actionCode)}`,
      undefined,
      {
        tags: {
          vertical: this.env.vertical,
          area: 'documents',
          operation: 'platform.documents.action',
          documentType,
        },
      },
    );
  }

  getDerivationActions(documentType: string, documentId: string): NgbHttpResponse {
    return this.http.get(`/api/documents/${encodeURIComponent(documentType)}/${encodeURIComponent(documentId)}/derive-actions`, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.derive_actions',
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

  lookupAcrossTypes(request: DocumentLookupRequest, tags: NgbRequestTags = {}): NgbHttpResponse {
    return this.http.post('/api/documents/lookup', {
      documentTypes: request.documentTypes,
      query: request.query ?? null,
      perTypeLimit: request.perTypeLimit ?? 5,
      activeOnly: request.activeOnly ?? true,
    }, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.lookup',
        ...tags,
      },
    });
  }

  getByIdsAcrossTypes(request: DocumentLookupByIdsRequest, tags: NgbRequestTags = {}): NgbHttpResponse {
    return this.http.post('/api/documents/lookup/by-ids', {
      documentTypes: request.documentTypes,
      ids: request.ids,
    }, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.lookup_by_ids',
        ...tags,
      },
    });
  }

  deriveDocument(targetDocumentType: string, request: DocumentDeriveRequest): NgbHttpResponse {
    return this.http.post(`/api/documents/${encodeURIComponent(targetDocumentType)}/derive`, {
      sourceDocumentId: request.sourceDocumentId,
      relationshipType: request.relationshipType,
      initialPayload: request.initialPayload ?? null,
    }, {
      tags: {
        vertical: this.env.vertical,
        area: 'documents',
        operation: 'platform.documents.derive',
        documentType: targetDocumentType,
      },
    });
  }
}
