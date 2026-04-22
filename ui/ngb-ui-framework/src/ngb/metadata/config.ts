import type { CatalogTypeMetadata, DocumentTypeMetadata, MetadataFormBehavior } from './types'

export type MetadataFrameworkConfig = {
  loadCatalogTypeMetadata: (catalogType: string) => Promise<CatalogTypeMetadata>
  loadDocumentTypeMetadata: (documentType: string) => Promise<DocumentTypeMetadata>
  formBehavior?: MetadataFormBehavior
}

let metadataFrameworkConfig: MetadataFrameworkConfig | null = null

export function configureNgbMetadata(config: MetadataFrameworkConfig): void {
  metadataFrameworkConfig = config
}

export function getConfiguredNgbMetadata(): MetadataFrameworkConfig {
  if (!metadataFrameworkConfig) {
    throw new Error('NGB metadata framework is not configured. Call configureNgbMetadata(...) during app bootstrap.')
  }

  return metadataFrameworkConfig
}

export function resolveNgbMetadataFormBehavior(override?: MetadataFormBehavior): MetadataFormBehavior {
  return {
    ...(metadataFrameworkConfig?.formBehavior ?? {}),
    ...(override ?? {}),
  }
}
