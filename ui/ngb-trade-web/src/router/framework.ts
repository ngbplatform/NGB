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

import { getTradeLookupHint } from '../lookup/hints'
import { catalogCollectionTitle, documentCollectionTitle } from '../utils/entityCollectionTitles'

export type TradeRouteFrameworkConfig = {
  catalogRoutes: RouteRecordRaw[]
  documentRoutes: RouteRecordRaw[]
}

const tradeEntityEditorComponent = defineAsyncComponent(
  () => import('../editor/TradeEntityEditor.vue'),
)

function loadTradeCatalogPage(args: MetadataCatalogListPageLoadArgs) {
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

function resolveTradeCatalogTitle(catalogType: string, displayName: string): string {
  return catalogCollectionTitle(catalogType, displayName)
}

function resolveTradeCatalogStorageKey(catalogType: string): string {
  return `ngb:trade:catalog:${catalogType}`
}

const tradeCatalogListPageProps = {
  editorComponent: tradeEntityEditorComponent,
  loadPage: loadTradeCatalogPage,
  resolveTitle: resolveTradeCatalogTitle,
  resolveStorageKey: resolveTradeCatalogStorageKey,
} satisfies MetadataCatalogListPageProps

const tradeCatalogEditPageProps = {
  editorComponent: tradeEntityEditorComponent,
} satisfies MetadataCatalogEditPageProps

function loadTradeDocumentPage(args: Parameters<MetadataDocumentListPageProps['loadPage']>[0]) {
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

function resolveTradeDocumentTitle(documentType: string, displayName: string): string {
  return documentCollectionTitle(documentType, displayName)
}

function resolveTradeDocumentStorageKey(documentType: string): string {
  return `ngb:trade:document:${documentType}`
}

function resolveTradeDocumentLookupHint(args: Parameters<NonNullable<MetadataDocumentListPageProps['resolveLookupHint']>>[0]) {
  return getTradeLookupHint(args.entityTypeCode, args.fieldKey, args.lookup)
}

const tradeDocumentListPageProps = {
  editorComponent: tradeEntityEditorComponent,
  loadPage: loadTradeDocumentPage,
  resolveLookupHint: resolveTradeDocumentLookupHint,
  resolveTitle: resolveTradeDocumentTitle,
  resolveStorageKey: resolveTradeDocumentStorageKey,
} satisfies MetadataDocumentListPageProps

const tradeDocumentEditPageProps = {
  editorComponent: tradeEntityEditorComponent,
} satisfies MetadataDocumentEditPageProps

export function createTradeRouteFrameworkConfig(): TradeRouteFrameworkConfig {
  return {
    catalogRoutes: [
      { path: '/catalogs/:catalogType', component: loadNgbMetadataCatalogListPage, props: tradeCatalogListPageProps },
      { path: '/catalogs/:catalogType/new', name: 'CatalogCreate', component: loadNgbMetadataCatalogEditPage, props: tradeCatalogEditPageProps },
      { path: '/catalogs/:catalogType/:id', component: loadNgbMetadataCatalogEditPage, props: tradeCatalogEditPageProps },
    ],
    documentRoutes: [
      { path: '/documents/:documentType', component: loadNgbMetadataDocumentListPage, props: tradeDocumentListPageProps },
      { path: '/documents/:documentType/new', name: 'DocumentCreate', component: loadNgbMetadataDocumentEditPage, props: tradeDocumentEditPageProps },
      { path: '/documents/:documentType/:id', component: loadNgbMetadataDocumentEditPage, props: tradeDocumentEditPageProps },
    ],
  }
}
