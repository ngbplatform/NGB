import type { Awaitable, EntityFormModel, LookupStoreApi } from '../metadata/types';
import type {
  AuditLogPage,
  DocumentHeaderActionItem,
  DocumentEffects,
  DocumentRecord,
  EntityEditorContext,
  EditorAuditBehavior,
  EditorAuditLoadOptions,
  EditorDocumentEffectsBehavior,
  EditorPrintBehavior,
  DocumentUiEffects,
  RelationshipGraph,
} from './types';

export type EditorRoutingConfig = {
  buildCatalogListUrl?: (catalogType: string) => string;
  buildCatalogFullPageUrl?: (catalogType: string, id?: string | null) => string;
  buildCatalogCompactPageUrl?: (catalogType: string, id?: string | null) => string;
  buildDocumentFullPageUrl?: (documentType: string, id?: string | null) => string;
  buildDocumentCompactPageUrl?: (documentType: string, id?: string | null) => string;
  buildDocumentEffectsPageUrl?: (documentType: string, id: string) => string;
  buildDocumentFlowPageUrl?: (documentType: string, id: string) => string;
  buildDocumentPrintPageUrl?: (documentType: string, id: string, options?: { autoPrint?: boolean }) => string;
};

export type EditorEntityBehaviorArgs = {
  context: EntityEditorContext;
  model: EntityFormModel;
};

export type EditorComputedDisplayMode = 'always' | 'new_or_draft';

export type EditorEntityProfile = {
  tags?: string[];
  sanitizeWatchFields?: string[];
  computedDisplayWatchFields?: string[];
  computedDisplayMode?: EditorComputedDisplayMode;
  sanitizeModelForEditing?: (args: EditorEntityBehaviorArgs) => void;
  syncComputedDisplay?: (args: EditorEntityBehaviorArgs) => void;
};

export type EditorDocumentActionGroup = {
  key: string;
  label: string;
};

export type EditorConfiguredDocumentAction = {
  item: DocumentHeaderActionItem;
  group?: EditorDocumentActionGroup | null;
  run: () => Awaitable<void>;
};

export type ResolveEditorDocumentActionsArgs = {
  context: EntityEditorContext;
  documentId: string;
  model: EntityFormModel;
  uiEffects: DocumentUiEffects | null;
  loading: boolean;
  saving: boolean;
  navigate: (to: string | null | undefined) => void;
};

export type EditorFrameworkConfig = {
  routing?: EditorRoutingConfig;
  loadDocumentById: (documentType: string, id: string) => Promise<DocumentRecord>;
  loadDocumentEffects: (documentType: string, id: string, limit?: number) => Promise<DocumentEffects>;
  loadDocumentGraph: (documentType: string, id: string, depth?: number, maxNodes?: number) => Promise<RelationshipGraph>;
  loadEntityAuditLog: (
    entityKind: number,
    entityId: string,
    options?: EditorAuditLoadOptions,
  ) => Promise<AuditLogPage>;
  lookupStore?: LookupStoreApi | null;
  audit?: EditorAuditBehavior;
  effects?: EditorDocumentEffectsBehavior;
  print?: EditorPrintBehavior;
  resolveEntityProfile?: (context: EntityEditorContext) => EditorEntityProfile | null;
  resolveDocumentActions?: (args: ResolveEditorDocumentActionsArgs) => EditorConfiguredDocumentAction[] | null;
};

let editorFrameworkConfig: EditorFrameworkConfig | null = null;

function normalizePathSegment(value: string | null | undefined): string {
  return String(value ?? '').trim();
}

function appendQuery(path: string, query: Record<string, string | null | undefined>): string {
  const params = new URLSearchParams();

  for (const [key, value] of Object.entries(query)) {
    const normalized = normalizePathSegment(value);
    if (!normalized) continue;
    params.set(key, normalized);
  }

  const serialized = params.toString();
  return serialized ? `${path}?${serialized}` : path;
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

export function configureNgbEditor(config: EditorFrameworkConfig): void {
  editorFrameworkConfig = config;
}

export function getConfiguredNgbEditor(): EditorFrameworkConfig {
  if (!editorFrameworkConfig) {
    throw new Error('NGB editor framework is not configured. Call configureNgbEditor(...) during app bootstrap.');
  }

  return editorFrameworkConfig;
}

export function maybeGetConfiguredNgbEditor(): EditorFrameworkConfig | null {
  return editorFrameworkConfig;
}

export function resolveNgbEditorRouting(): Required<EditorRoutingConfig> {
  return {
    buildCatalogListUrl:
      editorFrameworkConfig?.routing?.buildCatalogListUrl ?? defaultBuildCatalogListUrl,
    buildCatalogFullPageUrl:
      editorFrameworkConfig?.routing?.buildCatalogFullPageUrl ?? defaultBuildCatalogFullPageUrl,
    buildCatalogCompactPageUrl:
      editorFrameworkConfig?.routing?.buildCatalogCompactPageUrl ?? defaultBuildCatalogCompactPageUrl,
    buildDocumentFullPageUrl:
      editorFrameworkConfig?.routing?.buildDocumentFullPageUrl ?? defaultBuildDocumentFullPageUrl,
    buildDocumentCompactPageUrl:
      editorFrameworkConfig?.routing?.buildDocumentCompactPageUrl ?? defaultBuildDocumentCompactPageUrl,
    buildDocumentEffectsPageUrl:
      editorFrameworkConfig?.routing?.buildDocumentEffectsPageUrl ?? defaultBuildDocumentEffectsPageUrl,
    buildDocumentFlowPageUrl:
      editorFrameworkConfig?.routing?.buildDocumentFlowPageUrl ?? defaultBuildDocumentFlowPageUrl,
    buildDocumentPrintPageUrl:
      editorFrameworkConfig?.routing?.buildDocumentPrintPageUrl ?? defaultBuildDocumentPrintPageUrl,
  };
}

export function resolveNgbEditorEntityProfile(context: EntityEditorContext): EditorEntityProfile {
  return editorFrameworkConfig?.resolveEntityProfile?.(context) ?? {};
}

export function resolveNgbEditorDocumentActions(
  args: ResolveEditorDocumentActionsArgs,
): EditorConfiguredDocumentAction[] {
  return editorFrameworkConfig?.resolveDocumentActions?.(args) ?? [];
}

export function sanitizeNgbEditorModelForEditing(
  context: EntityEditorContext,
  model: EntityFormModel,
): void {
  resolveNgbEditorEntityProfile(context).sanitizeModelForEditing?.({ context, model });
}

export function syncNgbEditorComputedDisplay(
  context: EntityEditorContext,
  model: EntityFormModel,
): void {
  resolveNgbEditorEntityProfile(context).syncComputedDisplay?.({ context, model });
}

export function resolveNgbEditorAuditBehavior(override?: EditorAuditBehavior): EditorAuditBehavior {
  const defaultHiddenFieldNames = [
    'created_at_utc',
    'updated_at_utc',
    'deleted_at_utc',
    'marked_for_deletion_at_utc',
  ];

  return {
    hiddenFieldNames: [
      ...defaultHiddenFieldNames,
      ...(editorFrameworkConfig?.audit?.hiddenFieldNames ?? []),
      ...(override?.hiddenFieldNames ?? []),
    ],
    explicitFieldLabels: {
      ...(editorFrameworkConfig?.audit?.explicitFieldLabels ?? {}),
      ...(override?.explicitFieldLabels ?? {}),
    },
  };
}

export function resolveNgbEditorEffectsBehavior(
  override?: EditorDocumentEffectsBehavior,
): EditorDocumentEffectsBehavior {
  return {
    ...(editorFrameworkConfig?.effects ?? {}),
    ...(override ?? {}),
  };
}

export function resolveNgbEditorPrintBehavior(override?: EditorPrintBehavior): EditorPrintBehavior {
  return {
    ...(editorFrameworkConfig?.print ?? {}),
    ...(override ?? {}),
  };
}
