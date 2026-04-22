import { markRaw } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import {
  NgbMetadataCatalogEditPage,
  NgbMetadataCatalogListPage,
  NgbMetadataDocumentEditPage,
  NgbMetadataDocumentListPage,
  getCatalogPage,
  getDocumentPage,
  type MetadataCatalogEditPageProps,
  type MetadataCatalogListPageLoadArgs,
  type MetadataCatalogListPageProps,
  type MetadataDocumentEditPageProps,
  type MetadataDocumentListPageProps,
} from 'ngb-ui-framework'

import AgencyBillingEntityEditor from '../editor/AgencyBillingEntityEditor.vue'
import { getAgencyBillingLookupHint } from '../lookup/hints'
import { catalogCollectionTitle, documentCollectionTitle } from '../utils/entityCollectionTitles'

export type AgencyBillingRouteFrameworkConfig = {
  catalogRoutes: RouteRecordRaw[]
  documentRoutes: RouteRecordRaw[]
}

const agencyBillingEntityEditorComponent = markRaw(AgencyBillingEntityEditor)

function loadAgencyBillingCatalogPage(args: MetadataCatalogListPageLoadArgs) {
  return getCatalogPage(args.catalogType, {
    offset: args.offset,
    limit: args.limit,
    search: args.search,
    filters: { deleted: args.trashMode },
  })
}

function resolveAgencyBillingCatalogTitle(catalogType: string, displayName: string): string {
  return catalogCollectionTitle(catalogType, displayName)
}

function resolveAgencyBillingCatalogStorageKey(catalogType: string): string {
  return `ngb:agency-billing:catalog:${catalogType}`
}

const agencyBillingCatalogListPageProps = {
  editorComponent: agencyBillingEntityEditorComponent,
  loadPage: loadAgencyBillingCatalogPage,
  resolveTitle: resolveAgencyBillingCatalogTitle,
  resolveStorageKey: resolveAgencyBillingCatalogStorageKey,
} satisfies MetadataCatalogListPageProps

const agencyBillingCatalogEditPageProps = {
  editorComponent: agencyBillingEntityEditorComponent,
} satisfies MetadataCatalogEditPageProps

function loadAgencyBillingDocumentPage(args: Parameters<MetadataDocumentListPageProps['loadPage']>[0]) {
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

function resolveAgencyBillingDocumentTitle(documentType: string, displayName: string): string {
  return documentCollectionTitle(documentType, displayName)
}

function resolveAgencyBillingDocumentStorageKey(documentType: string): string {
  return `ngb:agency-billing:document:${documentType}`
}

function resolveAgencyBillingDocumentLookupHint(args: Parameters<NonNullable<MetadataDocumentListPageProps['resolveLookupHint']>>[0]) {
  return getAgencyBillingLookupHint(args.entityTypeCode, args.fieldKey, args.lookup)
}

const agencyBillingDocumentListPageProps = {
  editorComponent: agencyBillingEntityEditorComponent,
  loadPage: loadAgencyBillingDocumentPage,
  resolveLookupHint: resolveAgencyBillingDocumentLookupHint,
  resolveTitle: resolveAgencyBillingDocumentTitle,
  resolveStorageKey: resolveAgencyBillingDocumentStorageKey,
} satisfies MetadataDocumentListPageProps

const agencyBillingDocumentEditPageProps = {
  editorComponent: agencyBillingEntityEditorComponent,
} satisfies MetadataDocumentEditPageProps

export function createAgencyBillingRouteFrameworkConfig(): AgencyBillingRouteFrameworkConfig {
  return {
    catalogRoutes: [
      { path: '/catalogs/:catalogType', component: NgbMetadataCatalogListPage, props: agencyBillingCatalogListPageProps },
      { path: '/catalogs/:catalogType/new', name: 'CatalogCreate', component: NgbMetadataCatalogEditPage, props: agencyBillingCatalogEditPageProps },
      { path: '/catalogs/:catalogType/:id', component: NgbMetadataCatalogEditPage, props: agencyBillingCatalogEditPageProps },
    ],
    documentRoutes: [
      { path: '/documents/:documentType', component: NgbMetadataDocumentListPage, props: agencyBillingDocumentListPageProps },
      { path: '/documents/:documentType/new', name: 'DocumentCreate', component: NgbMetadataDocumentEditPage, props: agencyBillingDocumentEditPageProps },
      { path: '/documents/:documentType/:id', component: NgbMetadataDocumentEditPage, props: agencyBillingDocumentEditPageProps },
    ],
  }
}
