export type NgbRecordRouting = {
  buildCatalogListUrl?: (catalogType: string) => string;
  buildCatalogFullPageUrl?: (catalogType: string, id?: string | null) => string;
  buildCatalogCompactPageUrl?: (catalogType: string, id?: string | null) => string;
  buildDocumentFullPageUrl?: (documentType: string, id?: string | null) => string;
  buildDocumentCompactPageUrl?: (documentType: string, id?: string | null) => string;
  buildDocumentEffectsPageUrl?: (documentType: string, id: string) => string;
  buildDocumentFlowPageUrl?: (documentType: string, id: string) => string;
  buildDocumentPrintPageUrl?: (documentType: string, id: string, options?: { autoPrint?: boolean }) => string;
};

let routingProvider: () => NgbRecordRouting | undefined = () => undefined;

function normalizePathSegment(value: string | null | undefined): string {
  return String(value ?? '').trim();
}

function appendQuery(path: string, query: Record<string, string>): string {
  return `${path}?${new URLSearchParams(query).toString()}`;
}

function defaultBuildCatalogListUrl(catalogType: string): string {
  const type = encodeURIComponent(normalizePathSegment(catalogType));
  return `/catalogs/${type}`;
}

function defaultBuildCatalogFullPageUrl(catalogType: string, id?: string | null): string {
  const normalizedId = normalizePathSegment(id);
  if (!normalizedId) return `${defaultBuildCatalogListUrl(catalogType)}/new`;
  return `${defaultBuildCatalogListUrl(catalogType)}/${encodeURIComponent(normalizedId)}`;
}

function defaultBuildCatalogCompactPageUrl(catalogType: string, id?: string | null): string {
  const normalizedId = normalizePathSegment(id);
  if (!normalizedId) return appendQuery(defaultBuildCatalogListUrl(catalogType), { panel: 'new' });
  return appendQuery(defaultBuildCatalogListUrl(catalogType), { panel: 'edit', id: normalizedId });
}

function defaultBuildDocumentFullPageUrl(documentType: string, id?: string | null): string {
  const type = encodeURIComponent(normalizePathSegment(documentType));
  const normalizedId = normalizePathSegment(id);
  if (!normalizedId) return `/documents/${type}/new`;
  return `/documents/${type}/${encodeURIComponent(normalizedId)}`;
}

function defaultBuildDocumentCompactPageUrl(documentType: string, id?: string | null): string {
  const type = encodeURIComponent(normalizePathSegment(documentType));
  const normalizedId = normalizePathSegment(id);
  if (!normalizedId) return `/documents/${type}?panel=new`;
  return appendQuery(`/documents/${type}`, { panel: 'edit', id: normalizedId });
}

function defaultBuildDocumentEffectsPageUrl(documentType: string, id: string): string {
  const type = encodeURIComponent(normalizePathSegment(documentType));
  return `/documents/${type}/${encodeURIComponent(normalizePathSegment(id))}/effects`;
}

function defaultBuildDocumentFlowPageUrl(documentType: string, id: string): string {
  const type = encodeURIComponent(normalizePathSegment(documentType));
  return `/documents/${type}/${encodeURIComponent(normalizePathSegment(id))}/flow`;
}

function defaultBuildDocumentPrintPageUrl(
  documentType: string,
  id: string,
  options?: { autoPrint?: boolean },
): string {
  const type = encodeURIComponent(normalizePathSegment(documentType));
  const base = `/documents/${type}/${encodeURIComponent(normalizePathSegment(id))}/print`;
  return options?.autoPrint ? `${base}?autoprint=1` : base;
}

export function configureNgbRecordRouting(provider: () => NgbRecordRouting | undefined): void {
  routingProvider = provider;
}

export function resolveNgbRecordRouting(): Required<NgbRecordRouting> {
  const configuredRouting = routingProvider();
  return {
    buildCatalogListUrl:
      configuredRouting?.buildCatalogListUrl ?? defaultBuildCatalogListUrl,
    buildCatalogFullPageUrl:
      configuredRouting?.buildCatalogFullPageUrl ?? defaultBuildCatalogFullPageUrl,
    buildCatalogCompactPageUrl:
      configuredRouting?.buildCatalogCompactPageUrl ?? defaultBuildCatalogCompactPageUrl,
    buildDocumentFullPageUrl:
      configuredRouting?.buildDocumentFullPageUrl ?? defaultBuildDocumentFullPageUrl,
    buildDocumentCompactPageUrl:
      configuredRouting?.buildDocumentCompactPageUrl ?? defaultBuildDocumentCompactPageUrl,
    buildDocumentEffectsPageUrl:
      configuredRouting?.buildDocumentEffectsPageUrl ?? defaultBuildDocumentEffectsPageUrl,
    buildDocumentFlowPageUrl:
      configuredRouting?.buildDocumentFlowPageUrl ?? defaultBuildDocumentFlowPageUrl,
    buildDocumentPrintPageUrl:
      configuredRouting?.buildDocumentPrintPageUrl ?? defaultBuildDocumentPrintPageUrl,
  };
}
