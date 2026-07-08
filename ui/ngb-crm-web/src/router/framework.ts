import { markRaw } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import {
  NgbMetadataCatalogEditPage,
  NgbMetadataCatalogListPage,
  NgbMetadataDocumentEditPage,
  NgbMetadataDocumentListPage,
  getCatalogPage,
  getDocumentPage,
  type MetadataCatalogListPageLoadArgs,
  type MetadataCatalogEditPageProps,
  type MetadataCatalogListPageProps,
  type MetadataDocumentEditPageProps,
  type MetadataDocumentListPageProps,
} from '@ngbplatform/ui'

import { getCRMLookupHint } from '../lookup/hints'
import CRMEntityEditor from '../editor/CRMEntityEditor.vue'
import { catalogCollectionTitle, documentCollectionTitle } from '../utils/entityCollectionTitles'

export type CRMRouteFrameworkConfig = {
  catalogRoutes: RouteRecordRaw[]
  documentRoutes: RouteRecordRaw[]
}

const crmEntityEditorComponent = markRaw(CRMEntityEditor)

function loadCRMCatalogPage(args: MetadataCatalogListPageLoadArgs) {
  return getCatalogPage(args.catalogType, {
    offset: args.offset,
    limit: args.limit,
    search: args.search,
    filters: { deleted: args.trashMode },
  })
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
  return getDocumentPage(args.documentType, {
    offset: args.offset,
    limit: args.limit,
    search: args.search,
    filters: {
      deleted: args.trashMode,
      ...(args.periodFrom ? { periodFrom: args.periodFrom } : {}),
      ...(args.periodTo ? { periodTo: args.periodTo } : {}),
      ...args.listFilters,
    },
  })
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
      { path: '/catalogs/:catalogType', component: NgbMetadataCatalogListPage, props: crmCatalogListPageProps },
      { path: '/catalogs/:catalogType/new', name: 'CatalogCreate', component: NgbMetadataCatalogEditPage, props: crmCatalogEditPageProps },
      { path: '/catalogs/:catalogType/:id', component: NgbMetadataCatalogEditPage, props: crmCatalogEditPageProps },
    ],
    documentRoutes: [
      { path: '/documents/:documentType', component: NgbMetadataDocumentListPage, props: crmDocumentListPageProps },
      { path: '/documents/:documentType/new', name: 'DocumentCreate', component: NgbMetadataDocumentEditPage, props: crmDocumentEditPageProps },
      { path: '/documents/:documentType/:id', component: NgbMetadataDocumentEditPage, props: crmDocumentEditPageProps },
    ],
  }
}
