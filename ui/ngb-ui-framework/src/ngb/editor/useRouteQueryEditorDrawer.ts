import { computed, getCurrentInstance, inject } from 'vue';
import {
  matchedRouteKey,
  onBeforeRouteUpdate,
  type LocationQuery,
  type RouteLocationNormalizedLoaded,
  type Router,
} from 'vue-router';

import type { Awaitable } from '../metadata/types';
import {
  normalizeSingleQueryValue,
  pushCleanRouteQuery,
  replaceCleanRouteQuery,
} from '../router/queryParams';

export type RouteQueryEditorDrawerMode = 'new' | 'edit' | null;

export type RouteQueryEditorDrawerState = {
  mode: RouteQueryEditorDrawerMode;
  id: string | null;
};

export type RouteQueryEditorDrawerOpenState = {
  mode: 'new' | 'edit';
  id: string | null;
};

export type RouteQueryEditorDrawerMutationOptions = {
  patch?: Record<string, unknown>;
  onCommit?: () => void;
};

export type UseRouteQueryEditorDrawerArgs = {
  route: RouteLocationNormalizedLoaded;
  router: Router;
  panelKey?: string;
  idKey?: string;
  clearKeys?: string[];
  idImpliesEdit?: boolean;
  onBeforeOpen?: (
    next: RouteQueryEditorDrawerOpenState,
    current: RouteQueryEditorDrawerState,
  ) => Awaitable<boolean>;
  onBeforeClose?: (
    current: RouteQueryEditorDrawerState,
    next: RouteQueryEditorDrawerState,
  ) => Awaitable<boolean>;
};

export function useRouteQueryEditorDrawer(args: UseRouteQueryEditorDrawerArgs) {
  const panelKey = args.panelKey ?? 'panel';
  const idKey = args.idKey ?? 'id';
  const clearKeys = args.clearKeys ?? [];
  let suppressedRouteGuards = 0;

  function resolveState(query: LocationQuery): RouteQueryEditorDrawerState {
    const id = normalizeSingleQueryValue(query[idKey]) || null;
    const panel = normalizeSingleQueryValue(query[panelKey]).toLowerCase();
    if (panel === 'new') return { mode: 'new', id: null };
    if (panel === 'edit' && id) return { mode: 'edit', id };
    if (args.idImpliesEdit && id) return { mode: 'edit', id };
    return { mode: null, id: null };
  }

  function sameState(left: RouteQueryEditorDrawerState, right: RouteQueryEditorDrawerState) {
    return left.mode === right.mode && left.id === right.id;
  }

  const panelState = computed(() => resolveState(args.route.query));
  const panelId = computed(() => panelState.value.id);
  const panelMode = computed<RouteQueryEditorDrawerMode>(() => panelState.value.mode);
  const currentId = computed(() => (panelMode.value === 'edit' ? panelId.value : null));
  const isPanelOpen = computed(() => panelMode.value !== null);

  function currentState(): RouteQueryEditorDrawerState {
    return panelState.value;
  }

  async function commitRouteMutation(run: () => Promise<unknown>) {
    suppressedRouteGuards += 1;
    try {
      await run();
    } finally {
      suppressedRouteGuards = Math.max(0, suppressedRouteGuards - 1);
    }
  }

  function buildPatch(
    mode: RouteQueryEditorDrawerMode,
    id: string | null,
    patch?: Record<string, unknown>,
  ) {
    const nextPatch: Record<string, unknown> = {};
    for (const key of clearKeys) nextPatch[key] = undefined;
    nextPatch[panelKey] = mode ?? undefined;
    nextPatch[idKey] = mode === 'edit' ? id ?? undefined : undefined;
    return {
      ...nextPatch,
      ...(patch ?? {}),
    };
  }

  async function openCreateDrawer(options: RouteQueryEditorDrawerMutationOptions = {}): Promise<boolean> {
    const current = currentState();
    if (current.mode === 'new') return false;

    const next: RouteQueryEditorDrawerOpenState = { mode: 'new', id: null };
    if (await args.onBeforeOpen?.(next, current) === false) return false;

    options.onCommit?.();
    await commitRouteMutation(async () => {
      await pushCleanRouteQuery(args.route, args.router, buildPatch('new', null, options.patch));
    });
    return true;
  }

  async function openEditDrawer(
    id: string,
    options: RouteQueryEditorDrawerMutationOptions = {},
  ): Promise<boolean> {
    const normalizedId = String(id ?? '').trim();
    if (!normalizedId) return false;

    const current = currentState();
    if (current.mode === 'edit' && current.id === normalizedId) return false;

    const next: RouteQueryEditorDrawerOpenState = { mode: 'edit', id: normalizedId };
    if (await args.onBeforeOpen?.(next, current) === false) return false;

    options.onCommit?.();
    await commitRouteMutation(async () => {
      await pushCleanRouteQuery(args.route, args.router, buildPatch('edit', normalizedId, options.patch));
    });
    return true;
  }

  async function closeDrawer(options: RouteQueryEditorDrawerMutationOptions = {}): Promise<void> {
    options.onCommit?.();
    await commitRouteMutation(async () => {
      await replaceCleanRouteQuery(args.route, args.router, buildPatch(null, null, options.patch));
    });
  }

  const activeMatchedRoute = getCurrentInstance() ? inject(matchedRouteKey, null) : null;

  if (activeMatchedRoute) {
    onBeforeRouteUpdate(async (to) => {
      if (suppressedRouteGuards > 0) return true;

      const current = currentState();
      const next = resolveState(to.query);
      if (sameState(current, next)) return true;

      if (next.mode === null) {
        if (current.mode === null) return true;
        return await args.onBeforeClose?.(current, next) !== false;
      }

      return await args.onBeforeOpen?.(next, current) !== false;
    });
  }

  return {
    panelId,
    panelMode,
    currentId,
    isPanelOpen,
    openCreateDrawer,
    openEditDrawer,
    closeDrawer,
  };
}
