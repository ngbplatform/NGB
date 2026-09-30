import { defineStore } from 'pinia'
import { getConfiguredNgbMetadata } from './config'
import { normalizeCatalogTypeMetadata, normalizeDocumentTypeMetadata } from './normalization'
import type { CatalogTypeMetadata, DocumentTypeMetadata } from './types'

const catalogMetadataRequests = new Map<string, Promise<CatalogTypeMetadata>>()
const documentMetadataRequests = new Map<string, Promise<DocumentTypeMetadata>>()
let metadataGeneration = 0

export const useMetadataStore = defineStore('metadata', {
  state: () => ({
    catalogs: {} as Record<string, CatalogTypeMetadata>,
    documents: {} as Record<string, DocumentTypeMetadata>,
  }),
  actions: {
    async ensureCatalogType(catalogType: string): Promise<CatalogTypeMetadata> {
      const cached = this.catalogs[catalogType]
      if (cached) return cached

      const pending = catalogMetadataRequests.get(catalogType)
      if (pending) return await pending

      const generation = metadataGeneration
      const config = getConfiguredNgbMetadata()
      const request = config.loadCatalogTypeMetadata(catalogType)
        .then(normalizeCatalogTypeMetadata)
        .then((result) => {
          if (generation === metadataGeneration) this.catalogs[catalogType] = result
          return generation === metadataGeneration ? this.catalogs[catalogType]! : result
        })
      catalogMetadataRequests.set(catalogType, request)

      try {
        return await request
      } finally {
        if (catalogMetadataRequests.get(catalogType) === request) {
          catalogMetadataRequests.delete(catalogType)
        }
      }
    },

    async ensureDocumentType(documentType: string): Promise<DocumentTypeMetadata> {
      const cached = this.documents[documentType]
      if (cached) return cached

      const pending = documentMetadataRequests.get(documentType)
      if (pending) return await pending

      const generation = metadataGeneration
      const config = getConfiguredNgbMetadata()
      const request = config.loadDocumentTypeMetadata(documentType)
        .then(normalizeDocumentTypeMetadata)
        .then((result) => {
          if (generation === metadataGeneration) this.documents[documentType] = result
          return generation === metadataGeneration ? this.documents[documentType]! : result
        })
      documentMetadataRequests.set(documentType, request)

      try {
        return await request
      } finally {
        if (documentMetadataRequests.get(documentType) === request) {
          documentMetadataRequests.delete(documentType)
        }
      }
    },

    clear() {
      metadataGeneration += 1
      catalogMetadataRequests.clear()
      documentMetadataRequests.clear()
      this.catalogs = {}
      this.documents = {}
    },
  },
})
