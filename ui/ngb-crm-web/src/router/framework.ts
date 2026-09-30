import type { RouteRecordRaw } from 'vue-router'
import { defineAsyncComponent } from 'vue'
import {
  getCatalogPage,
  getDocumentPage,
  type MetadataCatalogListPageLoadArgs,
  type MetadataCatalogEditPageProps,
  type MetadataCatalogListPageProps,
  type MetadataDocumentEditPageProps,
  type MetadataDocumentListPageProps,
} from '@ngbplatform/ui'
import { loadNgbMetadataCatalogEditPage, loadNgbMetadataCatalogListPage, loadNgbMetadataDocumentEditPage, loadNgbMetadataDocumentListPage } from '@ngbplatform/ui/lazy'

import { getCRMLookupHint } from '../lookup/hints'
import { catalogCollectionTitle, documentCollectionTitle } from '../utils/entityCollectionTitles'

export type CRMRouteFrameworkConfig = {
  catalogRoutes: RouteRecordRaw[]
  documentRoutes: RouteRecordRaw[]
}

const crmEntityEditorComponent = defineAsyncComponent(
  () => import('../editor/CRMEntityEditor.vue'),
)

function loadCRMCatalogPage(args: MetadataCatalogListPageLoadArgs) {
  const request = {
    offset: args.offset,
    limit: args.limit,
    search: args.search,
    filters: { deleted: args.trashMode },
  }
  return args.signal
    ? getCatalogPage(args.catalogType, request, { signal: args.signal })
    : getCatalogPage(args.catalogType, request)
}

function resolveCRMCatalogTitle(catalogType: string, displayName: string): string {
  return catalogCollectionTitle(catalogType, displayName)
}

function resolveCRMCatalogStorageKey(catalogType: string): string {
  return `ngb:crm:catalog:${catalogType}`
}

const crmCatalogListPageProps = {
  editorComponent: crmEntityEditorComponent,
  loadPage: loadCRMCatalogPage,
  resolveTitle: resolveCRMCatalogTitle,
  resolveStorageKey: resolveCRMCatalogStorageKey,
} satisfies MetadataCatalogListPageProps

const crmCatalogEditPageProps = {
  editorComponent: crmEntityEditorComponent,
} satisfies MetadataCatalogEditPageProps

function loadCRMDocumentPage(args: Parameters<MetadataDocumentListPageProps['loadPage']>[0]) {
  const request = {
    offset: args.offset,
    limit: args.limit,
    search: args.search,
    filters: {
      deleted: args.trashMode,
      ...(args.periodFrom ? { periodFrom: args.periodFrom } : {}),
      ...(args.periodTo ? { periodTo: args.periodTo } : {}),
      ...args.listFilters,
    },
  }
  return args.signal
    ? getDocumentPage(args.documentType, request, { signal: args.signal })
    : getDocumentPage(args.documentType, request)
}

function resolveCRMDocumentTitle(documentType: string, displayName: string): string {
  return documentCollectionTitle(documentType, displayName)
}

function resolveCRMDocumentStorageKey(documentType: string): string {
  return `ngb:crm:document:${documentType}`
}

function resolveCRMDocumentLookupHint(args: Parameters<NonNullable<MetadataDocumentListPageProps['resolveLookupHint']>>[0]) {
  return getCRMLookupHint(args.entityTypeCode, args.fieldKey, args.lookup)
}

const crmDocumentListPageProps = {
  editorComponent: crmEntityEditorComponent,
  loadPage: loadCRMDocumentPage,
  resolveLookupHint: resolveCRMDocumentLookupHint,
  resolveTitle: resolveCRMDocumentTitle,
  resolveStorageKey: resolveCRMDocumentStorageKey,
} satisfies MetadataDocumentListPageProps

const crmDocumentEditPageProps = {
  editorComponent: crmEntityEditorComponent,
} satisfies MetadataDocumentEditPageProps

export function createCRMRouteFrameworkConfig(): CRMRouteFrameworkConfig {
  return {
    catalogRoutes: [
      { path: '/catalogs/:catalogType', component: loadNgbMetadataCatalogListPage, props: crmCatalogListPageProps },
      { path: '/catalogs/:catalogType/new', name: 'CatalogCreate', component: loadNgbMetadataCatalogEditPage, props: crmCatalogEditPageProps },
      { path: '/catalogs/:catalogType/:id', component: loadNgbMetadataCatalogEditPage, props: crmCatalogEditPageProps },
    ],
    documentRoutes: [
      { path: '/documents/:documentType', component: loadNgbMetadataDocumentListPage, props: crmDocumentListPageProps },
      { path: '/documents/:documentType/new', name: 'DocumentCreate', component: loadNgbMetadataDocumentEditPage, props: crmDocumentEditPageProps },
      { path: '/documents/:documentType/:id', component: loadNgbMetadataDocumentEditPage, props: crmDocumentEditPageProps },
    ],
  }
}
