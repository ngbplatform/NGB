import type { RouteRecordRaw } from 'vue-router'
import { defineAsyncComponent } from 'vue'
import {
  getCatalogPage,
  getDocumentPage,
  type MetadataCatalogEditPageProps,
  type MetadataCatalogListPageLoadArgs,
  type MetadataCatalogListPageProps,
  type MetadataDocumentEditPageProps,
  type MetadataDocumentListPageProps,
} from '@ngbplatform/ui'
import { loadNgbMetadataCatalogEditPage, loadNgbMetadataCatalogListPage, loadNgbMetadataDocumentEditPage, loadNgbMetadataDocumentListPage } from '@ngbplatform/ui/lazy'

import { getAgencyBillingLookupHint } from '../lookup/hints'
import { catalogCollectionTitle, documentCollectionTitle } from '../utils/entityCollectionTitles'

export type AgencyBillingRouteFrameworkConfig = {
  catalogRoutes: RouteRecordRaw[]
  documentRoutes: RouteRecordRaw[]
}

function loadAgencyBillingEntityEditor() {
  return import('../editor/AgencyBillingEntityEditor.vue')
}

const agencyBillingEntityEditorComponent = defineAsyncComponent(loadAgencyBillingEntityEditor)

function loadAgencyBillingCatalogPage(args: MetadataCatalogListPageLoadArgs) {
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
      { path: '/catalogs/:catalogType', component: loadNgbMetadataCatalogListPage, props: agencyBillingCatalogListPageProps },
      { path: '/catalogs/:catalogType/new', name: 'CatalogCreate', component: loadNgbMetadataCatalogEditPage, props: agencyBillingCatalogEditPageProps },
      { path: '/catalogs/:catalogType/:id', component: loadNgbMetadataCatalogEditPage, props: agencyBillingCatalogEditPageProps },
    ],
    documentRoutes: [
      { path: '/documents/:documentType', component: loadNgbMetadataDocumentListPage, props: agencyBillingDocumentListPageProps },
      { path: '/documents/:documentType/new', name: 'DocumentCreate', component: loadNgbMetadataDocumentEditPage, props: agencyBillingDocumentEditPageProps },
      { path: '/documents/:documentType/:id', component: loadNgbMetadataDocumentEditPage, props: agencyBillingDocumentEditPageProps },
    ],
  }
}
