import type { Component } from 'vue'
import type { RouteLocationNormalizedLoaded, Router } from 'vue-router'

import type { EntityEditorFlags, EntityHeaderIconAction } from '../editor/types'
import type { QueryTrashMode } from '../router/queryParams'
import type { Awaitable, LookupHint, LookupSource } from './types'
import type { MetadataRegisterPageMetadata, MetadataRegisterPageResponse } from './useMetadataRegisterPageData'

export type MetadataCatalogListPageLoadArgs = {
  catalogType: string
  offset: number
  limit: number
  search?: string
  trashMode: QueryTrashMode
}

export type MetadataCatalogDrawerActionArgs = {
  action: string
  editor: unknown | null
}

export interface MetadataCatalogListPageProps {
  editorComponent: Component
  loadPage: (args: MetadataCatalogListPageLoadArgs) => Promise<MetadataRegisterPageResponse>
  resolveTitle?: ((catalogType: string, displayName: string) => string) | null
  resolveStorageKey?: ((catalogType: string) => string | null | undefined) | null
  resolveDrawerExtraActions?: ((args: { editorFlags: EntityEditorFlags }) => EntityHeaderIconAction[]) | null
  handleDrawerExtraAction?: ((args: MetadataCatalogDrawerActionArgs) => Awaitable<boolean | void>) | null
  backTarget?: string | null
}

export interface MetadataCatalogEditPageProps {
  editorComponent: Component
  canBack?: boolean
  resolveCompactTo?: ((catalogType: string, id?: string | null) => string | null | undefined) | null
  resolveCloseTo?: ((catalogType: string) => string | null | undefined) | null
}

export type MetadataDocumentListPageLoadArgs<TMeta extends MetadataRegisterPageMetadata = MetadataRegisterPageMetadata> = {
  documentType: string
  metadata: TMeta
  offset: number
  limit: number
  search?: string
  trashMode: QueryTrashMode
  periodFrom?: string | null
  periodTo?: string | null
  listFilters: Record<string, string>
}

export type MetadataRouteLocationLike = Pick<RouteLocationNormalizedLoaded, 'fullPath' | 'params' | 'query'>

export type MetadataRouterLike = Pick<Router, 'push'>

export type MetadataDocumentCreateOverrideArgs<TMeta extends MetadataRegisterPageMetadata = MetadataRegisterPageMetadata> = {
  documentType: string
  metadata: TMeta | null
  preferFullPage: boolean
  route: MetadataRouteLocationLike
  router: MetadataRouterLike
  openCreateDrawer: (copyDraftToken?: string | null) => void
  openFullPage: () => Promise<void>
}

export interface MetadataDocumentListPageProps<TMeta extends MetadataRegisterPageMetadata = MetadataRegisterPageMetadata> {
  editorComponent: Component
  loadPage: (args: MetadataDocumentListPageLoadArgs<TMeta>) => Promise<MetadataRegisterPageResponse>
  resolveLookupHint?: ((args: {
    entityTypeCode: string
    fieldKey: string
    lookup?: LookupSource | null
  }) => LookupHint | null) | null
  resolveTitle?: ((documentType: string, displayName: string) => string) | null
  resolveStorageKey?: ((documentType: string) => string | null | undefined) | null
  resolveWarning?: ((documentType: string, metadata: TMeta | null) => string | null | undefined) | null
  isCreateDisabled?: ((documentType: string, metadata: TMeta | null) => boolean) | null
  handleCreateOverride?: ((args: MetadataDocumentCreateOverrideArgs<TMeta>) => Awaitable<boolean | void>) | null
  backTarget?: string | null
}

export interface MetadataDocumentEditPageProps {
  editorComponent: Component
  canBack?: boolean
  resolveCompactTo?: ((documentType: string, id?: string | null) => string | null | undefined) | null
  resolveCloseTo?: ((documentType: string) => string | null | undefined) | null
}
