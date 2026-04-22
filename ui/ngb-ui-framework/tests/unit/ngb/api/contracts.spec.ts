import { describe, expect, it } from 'vitest'

import type {
  ActionMetadataDto,
  DocumentDto,
  PageRequest,
  RelationshipGraphDto,
} from '../../../../src/ngb/api/contracts'

describe('api contracts', () => {
  it('supports document, graph, and page request dto contracts', () => {
    const action: ActionMetadataDto = {
      code: 'post',
      label: 'Post',
      kind: 1,
      requiresConfirm: true,
      visibleWhenStatusIn: [1, 2],
    }
    const document: DocumentDto = {
      id: 'doc-1',
      number: 'INV-001',
      display: 'Invoice INV-001',
      payload: {
        fields: {
          total: 1250,
        },
      },
      status: 1,
      isMarkedForDeletion: false,
    }
    const graph: RelationshipGraphDto = {
      nodes: [
        {
          nodeId: 'node-1',
          kind: 2,
          typeCode: 'pm.invoice',
          entityId: 'doc-1',
          title: 'Invoice INV-001',
        },
      ],
      edges: [
        {
          fromNodeId: 'node-1',
          toNodeId: 'node-2',
          relationshipType: 'payment',
        },
      ],
    }
    const pageRequest: PageRequest = {
      offset: 0,
      limit: 25,
      search: 'riverfront',
      filters: {
        status: 'open',
      },
    }

    expect(action.requiresConfirm).toBe(true)
    expect(document.payload.fields?.total).toBe(1250)
    expect(graph.edges[0]?.relationshipType).toBe('payment')
    expect(pageRequest.filters?.status).toBe('open')
  })
})
