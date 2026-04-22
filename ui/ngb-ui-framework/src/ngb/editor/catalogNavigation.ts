import { resolveNgbEditorRouting } from './config';

export function buildCatalogListUrl(catalogType: string): string {
  return resolveNgbEditorRouting().buildCatalogListUrl(catalogType);
}

export function buildCatalogFullPageUrl(catalogType: string, id?: string | null): string {
  return resolveNgbEditorRouting().buildCatalogFullPageUrl(catalogType, id);
}

export function buildCatalogCompactPageUrl(catalogType: string, id?: string | null): string {
  return resolveNgbEditorRouting().buildCatalogCompactPageUrl(catalogType, id);
}
