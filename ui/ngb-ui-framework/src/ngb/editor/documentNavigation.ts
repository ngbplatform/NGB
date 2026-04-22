import type { RouteLocationNormalizedLoaded } from 'vue-router';

import type { DocumentTypeMetadata, FormMetadata } from '../metadata/types';
import {
  currentRouteBackTarget,
  resolveBackTarget,
  resolveBackTargetFromPath,
  routeTargetMatches,
  withBackTarget,
} from '../router/backNavigation';
import { resolveNgbEditorRouting } from './config';
import type { EditorKind, EditorMode } from './types';

type FormFieldDescriptor = {
  key?: string;
  isReadOnly?: boolean;
};

type FormShape = {
  sections?: Array<{
    rows?: Array<{
      fields?: FormFieldDescriptor[];
    }>;
  }>;
} | null | undefined;

export function documentHasTables(meta: DocumentTypeMetadata | null | undefined): boolean {
  return Array.isArray(meta?.parts) && meta.parts.length > 0;
}

export function shouldOpenDocumentInFullPageByDefault(meta: DocumentTypeMetadata | null | undefined): boolean {
  return documentHasTables(meta);
}

export function buildDocumentFullPageUrl(documentType: string, id?: string | null): string {
  return resolveNgbEditorRouting().buildDocumentFullPageUrl(documentType, id);
}

export function buildDocumentCompactPageUrl(documentType: string, id?: string | null): string {
  return resolveNgbEditorRouting().buildDocumentCompactPageUrl(documentType, id);
}

export function buildDocumentEffectsPageUrl(documentType: string, id: string): string {
  return resolveNgbEditorRouting().buildDocumentEffectsPageUrl(documentType, id);
}

export function buildDocumentFlowPageUrl(documentType: string, id: string): string {
  return resolveNgbEditorRouting().buildDocumentFlowPageUrl(documentType, id);
}

export function buildDocumentPrintPageUrl(
  documentType: string,
  id: string,
  options?: { autoPrint?: boolean },
): string {
  return resolveNgbEditorRouting().buildDocumentPrintPageUrl(documentType, id, options);
}

export function resolveCompactDocumentSourceTarget(
  route: Pick<RouteLocationNormalizedLoaded, 'query'>,
  compactTarget: string | null | undefined,
): string | null {
  const explicitBack = resolveBackTarget(route);
  if (routeTargetMatches(explicitBack, compactTarget)) return explicitBack;

  const nestedBack = resolveBackTargetFromPath(explicitBack);
  if (routeTargetMatches(nestedBack, compactTarget)) return nestedBack;

  return null;
}

export function resolveDocumentReopenTarget(
  route: Pick<RouteLocationNormalizedLoaded, 'query' | 'fullPath'>,
  documentType: string,
  id: string,
  compactTarget?: string | null,
): string {
  const compactSourceTarget = resolveCompactDocumentSourceTarget(
    route,
    compactTarget ?? buildDocumentCompactPageUrl(documentType, id),
  );
  if (compactSourceTarget) return compactSourceTarget;

  const explicitBack = resolveBackTarget(route);
  if (explicitBack) return explicitBack;

  return withBackTarget(
    buildDocumentFullPageUrl(documentType, id),
    currentRouteBackTarget(route),
  );
}

export function buildEntityFallbackCloseTarget(kind: EditorKind, typeCode: string): string {
  return `${kind === 'catalog' ? '/catalogs/' : '/documents/'}${typeCode}`;
}

export function resolveNavigateOnCreate(value: boolean | undefined, mode: EditorMode): boolean {
  return value ?? mode === 'page';
}

export function listFormFields(form: FormShape): FormFieldDescriptor[] {
  const out: FormFieldDescriptor[] = [];
  for (const section of form?.sections ?? []) {
    for (const row of section.rows ?? []) {
      for (const field of row.fields ?? []) out.push(field);
    }
  }
  return out;
}

export function formMetadataFieldKeys(form: FormMetadata | null | undefined): string[] {
  return listFormFields(form)
    .map((field) => String(field?.key ?? '').trim())
    .filter((key) => key.length > 0);
}
