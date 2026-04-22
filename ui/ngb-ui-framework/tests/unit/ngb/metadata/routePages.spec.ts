import { describe, expect, it, vi } from 'vitest'
import type { Component } from 'vue'

import type {
  MetadataCatalogListPageProps,
  MetadataDocumentCreateOverrideArgs,
  MetadataDocumentListPageProps,
} from '../../../../src/ngb/metadata/routePages'

describe('metadata route page types', () => {
  it('supports catalog and document page props with create override arguments', async () => {
    const editorComponent = {} as Component
    const catalogProps: MetadataCatalogListPageProps = {
      editorComponent,
      loadPage: async (args) => ({ offset: args.offset, limit: args.limit } as never),
      resolveTitle: (catalogType, displayName) => `${displayName} (${catalogType})`,
      resolveStorageKey: (catalogType) => `catalog:${catalogType}`,
    }
    const documentProps: MetadataDocumentListPageProps = {
      editorComponent,
      loadPage: async (args) => ({ offset: args.offset, limit: args.limit } as never),
      resolveTitle: (documentType, displayName) => `${displayName} (${documentType})`,
      isCreateDisabled: (_documentType, metadata) => metadata === null,
    }

    const calls: string[] = []
    const createArgs: MetadataDocumentCreateOverrideArgs = {
      documentType: 'pm.invoice',
      metadata: null,
      preferFullPage: false,
      route: {
        fullPath: '/documents/pm.invoice',
        params: {},
        query: {},
      },
      router: {
        push: vi.fn(async () => undefined),
      } as never,
      openCreateDrawer: (copyDraftToken) => {
        calls.push(`drawer:${copyDraftToken ?? ''}`)
      },
      openFullPage: async () => {
        calls.push('full')
      },
    }

    expect(catalogProps.resolveTitle?.('pm.property', 'Properties')).toBe('Properties (pm.property)')
    expect(documentProps.resolveStorageKey).toBeUndefined()
    expect(documentProps.isCreateDisabled?.('pm.invoice', null)).toBe(true)

    createArgs.openCreateDrawer('copy-1')
    await createArgs.openFullPage()

    expect(calls).toEqual(['drawer:copy-1', 'full'])
  })
})
