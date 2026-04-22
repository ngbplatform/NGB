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
} from 'ngb-ui-framework'

import { getTradeLookupHint } from '../lookup/hints'
import TradeEntityEditor from '../editor/TradeEntityEditor.vue'
import { catalogCollectionTitle, documentCollectionTitle } from '../utils/entityCollectionTitles'

export type TradeRouteFrameworkConfig = {
  catalogRoutes: RouteRecordRaw[]
  documentRoutes: RouteRecordRaw[]
}

const tradeEntityEditorComponent = markRaw(TradeEntityEditor)

function loadTradeCatalogPage(args: MetadataCatalogListPageLoadArgs) {
  return getCatalogPage(args.catalogType, {
    offset: args.offset,
    limit: args.limit,
    search: args.search,
    filters: { deleted: args.trashMode },
  })
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
      { path: '/catalogs/:catalogType', component: NgbMetadataCatalogListPage, props: tradeCatalogListPageProps },
      { path: '/catalogs/:catalogType/new', name: 'CatalogCreate', component: NgbMetadataCatalogEditPage, props: tradeCatalogEditPageProps },
      { path: '/catalogs/:catalogType/:id', component: NgbMetadataCatalogEditPage, props: tradeCatalogEditPageProps },
    ],
    documentRoutes: [
      { path: '/documents/:documentType', component: NgbMetadataDocumentListPage, props: tradeDocumentListPageProps },
      { path: '/documents/:documentType/new', name: 'DocumentCreate', component: NgbMetadataDocumentEditPage, props: tradeDocumentEditPageProps },
      { path: '/documents/:documentType/:id', component: NgbMetadataDocumentEditPage, props: tradeDocumentEditPageProps },
    ],
  }
}
