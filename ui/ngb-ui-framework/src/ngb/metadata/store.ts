import { defineStore } from 'pinia'
import { getConfiguredNgbMetadata } from './config'
import { normalizeCatalogTypeMetadata, normalizeDocumentTypeMetadata } from './normalization'
import type { CatalogTypeMetadata, DocumentTypeMetadata } from './types'

export const useMetadataStore = defineStore('metadata', {
  state: () => ({
    catalogs: {} as Record<string, CatalogTypeMetadata>,
    documents: {} as Record<string, DocumentTypeMetadata>,
  }),
  actions: {
    async ensureCatalogType(catalogType: string): Promise<CatalogTypeMetadata> {
      if (!this.catalogs[catalogType]) {
        const config = getConfiguredNgbMetadata()
        this.catalogs[catalogType] = normalizeCatalogTypeMetadata(await config.loadCatalogTypeMetadata(catalogType))
      }

      return this.catalogs[catalogType]
    },

    async ensureDocumentType(documentType: string): Promise<DocumentTypeMetadata> {
      if (!this.documents[documentType]) {
        const config = getConfiguredNgbMetadata()
        this.documents[documentType] = normalizeDocumentTypeMetadata(await config.loadDocumentTypeMetadata(documentType))
      }

      return this.documents[documentType]
    },

    clear() {
      this.catalogs = {}
      this.documents = {}
    },
  },
})
