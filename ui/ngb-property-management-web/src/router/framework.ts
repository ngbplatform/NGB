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

import PmEntityEditor from '../editor/pm/PmEntityEditor.vue'
import { getLookupHint } from '../lookup/hints'
import { buildPmOpenItemsPath } from './pmRoutePaths'
import { catalogCollectionTitle, documentCollectionTitle } from '../utils/entityCollectionTitles'
import type { PmEntityEditorHandle } from '../editor/types'

export type PmRouteFrameworkConfig = {
  catalogRoutes: RouteRecordRaw[]
  documentRoutes: RouteRecordRaw[]
}

const pmEntityEditorComponent = markRaw(PmEntityEditor)

function loadPmCatalogPage(args: MetadataCatalogListPageLoadArgs) {
  return getCatalogPage(args.catalogType, {
    offset: args.offset,
    limit: args.limit,
    search: args.search,
    filters: { deleted: args.trashMode },
  })
}

function resolvePmCatalogTitle(catalogType: string, displayName: string): string {
  return catalogCollectionTitle(catalogType, displayName)
}

function resolvePmCatalogStorageKey(catalogType: string): string {
  return `pm:catalog:${catalogType}`
}

function resolvePmCatalogDrawerExtraActions(args: Parameters<NonNullable<MetadataCatalogListPageProps['resolveDrawerExtraActions']>>[0]) {
  return args.editorFlags.extras?.bulkCreateUnits
    ? [{ key: 'bulkCreateUnits', title: 'Bulk create units', icon: 'grid', disabled: args.editorFlags.loading || args.editorFlags.saving }]
    : []
}

function handlePmCatalogDrawerExtraAction(args: Parameters<NonNullable<MetadataCatalogListPageProps['handleDrawerExtraAction']>>[0]) {
  if (args.action !== 'bulkCreateUnits') return false
  (args.editor as PmEntityEditorHandle | null)?.openBulkCreateUnitsWizard()
  return true
}

const pmCatalogListPageProps = {
  editorComponent: pmEntityEditorComponent,
  loadPage: loadPmCatalogPage,
  resolveTitle: resolvePmCatalogTitle,
  resolveStorageKey: resolvePmCatalogStorageKey,
  resolveDrawerExtraActions: resolvePmCatalogDrawerExtraActions,
  handleDrawerExtraAction: handlePmCatalogDrawerExtraAction,
} satisfies MetadataCatalogListPageProps

const pmCatalogEditPageProps = {
  editorComponent: pmEntityEditorComponent,
} satisfies MetadataCatalogEditPageProps

function loadPmDocumentPage(args: Parameters<MetadataDocumentListPageProps['loadPage']>[0]) {
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

function resolvePmDocumentTitle(documentType: string, displayName: string): string {
  return documentCollectionTitle(documentType, displayName)
}

function resolvePmDocumentStorageKey(documentType: string): string {
  return `pm:document:${documentType}`
}

function resolvePmDocumentWarning(documentType: string): string | null {
  if (documentType === 'pm.payable_apply') {
    return 'Payable Apply documents are created by the payables Apply flow. Open Items is the canonical place to create new applications.'
  }

  if (documentType === 'pm.receivable_apply') {
    return 'Receivable Apply documents are created by the receivables Apply flow. Open Items is the canonical place to create new applications.'
  }

  return null
}

function isPmDocumentCreateDisabled(documentType: string): boolean {
  return documentType === 'pm.receivable_apply' || documentType === 'pm.payable_apply'
}

async function handlePmDocumentCreateOverride(
  args: Parameters<NonNullable<MetadataDocumentListPageProps['handleCreateOverride']>>[0],
) {
  if (args.documentType === 'pm.receivable_apply') {
    await args.router.push(buildPmOpenItemsPath('receivables'))
    return true
  }

  if (args.documentType === 'pm.payable_apply') {
    await args.router.push(buildPmOpenItemsPath('payables'))
    return true
  }

  return false
}

function resolvePmDocumentLookupHint(args: Parameters<NonNullable<MetadataDocumentListPageProps['resolveLookupHint']>>[0]) {
  return getLookupHint(args.entityTypeCode, args.fieldKey, args.lookup)
}

const pmDocumentListPageProps = {
  editorComponent: pmEntityEditorComponent,
  loadPage: loadPmDocumentPage,
  resolveLookupHint: resolvePmDocumentLookupHint,
  resolveTitle: resolvePmDocumentTitle,
  resolveStorageKey: resolvePmDocumentStorageKey,
  resolveWarning: resolvePmDocumentWarning,
  isCreateDisabled: isPmDocumentCreateDisabled,
  handleCreateOverride: handlePmDocumentCreateOverride,
} satisfies MetadataDocumentListPageProps

const pmDocumentEditPageProps = {
  editorComponent: pmEntityEditorComponent,
} satisfies MetadataDocumentEditPageProps

export function createPmRouteFrameworkConfig(): PmRouteFrameworkConfig {
  return {
    catalogRoutes: [
      { path: '/catalogs/:catalogType', component: NgbMetadataCatalogListPage, props: pmCatalogListPageProps },
      { path: '/catalogs/:catalogType/new', name: 'CatalogCreate', component: NgbMetadataCatalogEditPage, props: pmCatalogEditPageProps },
      { path: '/catalogs/:catalogType/:id', component: NgbMetadataCatalogEditPage, props: pmCatalogEditPageProps },
    ],
    documentRoutes: [
      { path: '/documents/:documentType', component: NgbMetadataDocumentListPage, props: pmDocumentListPageProps },
      // pm.receivable_apply and pm.payable_apply are created from open-items flows, not from a blank editor.
      { path: '/documents/pm.receivable_apply/new', redirect: '/receivables/open-items' },
      { path: '/documents/pm.payable_apply/new', redirect: '/payables/open-items' },
      { path: '/documents/:documentType/new', name: 'DocumentCreate', component: NgbMetadataDocumentEditPage, props: pmDocumentEditPageProps },
      { path: '/documents/:documentType/:id', component: NgbMetadataDocumentEditPage, props: pmDocumentEditPageProps },
    ],
  }
}
