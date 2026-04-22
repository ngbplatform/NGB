<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import NgbBadge from '../primitives/NgbBadge.vue';
import NgbIcon from '../primitives/NgbIcon.vue';
import NgbStatusIcon from '../primitives/NgbStatusIcon.vue';
import { useToasts } from '../primitives/toast';
import NgbPageHeader from '../site/NgbPageHeader.vue';
import { copyAppLink } from '../router/shareLink';
import { currentRouteBackTarget, navigateBack, resolveBackTarget, withBackTarget } from '../router/backNavigation';
import { toErrorMessage } from '../utils/errorMessage';

import { getConfiguredNgbEditor } from './config';
import { buildDocumentFlowPageUrl, buildDocumentFullPageUrl, resolveDocumentReopenTarget } from './documentNavigation';
import { documentStatusLabel, documentStatusTone, documentStatusVisual } from './documentStatus';
import type { RelationshipGraph, RelationshipGraphNode } from './types';

type FlowRow = {
  key: string;
  node: RelationshipGraphNode;
  level: number;
  ancestorHasNext: boolean[];
  isCurrent: boolean;
};

const TREE_INDENT_PX = 42;
const TREE_LINE_LEFT_PX = 18;
const TREE_ROW_GAP_PX = 18;
const DEFAULT_DEPTH = 5;
const DEFAULT_MAX_NODES = 200;
const FLOW_CODES = new Set(['based_on', 'created_from', 'reversal_of', 'related_to', 'supersedes']);

const route = useRoute();
const router = useRouter();
const toasts = useToasts();

const loading = ref(false);
const error = ref<string | null>(null);
const graph = ref<RelationshipGraph | null>(null);

const documentType = computed(() => String(route.params.documentType ?? '').trim());
const documentId = computed(() => String(route.params.id ?? '').trim());

const documentRoute = computed(() => {
  if (!documentType.value || !documentId.value) return '/';
  return resolveDocumentReopenTarget(route, documentType.value, documentId.value);
});

const shareRoute = computed(() => {
  if (!documentType.value || !documentId.value) return '/';
  return buildDocumentFlowPageUrl(documentType.value, documentId.value);
});

const rootNode = computed<RelationshipGraphNode | null>(() => {
  const entityId = documentId.value;
  if (!entityId) return graph.value?.nodes?.[0] ?? null;
  return graph.value?.nodes.find((node) => sameId(node.entityId, entityId)) ?? graph.value?.nodes?.[0] ?? null;
});

const title = computed(() => String(rootNode.value?.title ?? '').trim() || 'Document flow');
const statusTone = computed<'neutral' | 'success' | 'warn'>(() => documentStatusTone(rootNode.value?.documentStatus));
const statusLabel = computed(() => documentStatusLabel(rootNode.value?.documentStatus));
const canShareLink = computed(() => !!documentType.value && !!documentId.value);

const rootNodeId = computed(() => rootNode.value?.nodeId ?? null);

const flowRows = computed<FlowRow[]>(() => {
  const nodes = graph.value?.nodes ?? [];
  const edges = graph.value?.edges ?? [];
  if (nodes.length === 0) return [];

  const nodeById = new Map(nodes.map((node) => [node.nodeId, node] as const));
  const root = rootNodeId.value ?? nodes[0]?.nodeId ?? null;
  if (!root || !nodeById.has(root)) return [];

  const adjacency = new Map<string, Set<string>>();
  for (const edge of edges) {
    const relationship = String(edge.relationshipType ?? '').trim().toLowerCase();
    if (!FLOW_CODES.has(relationship)) continue;
    if (!nodeById.has(edge.fromNodeId) || !nodeById.has(edge.toNodeId)) continue;
    if (edge.fromNodeId === edge.toNodeId) continue;

    const from = adjacency.get(edge.fromNodeId) ?? new Set<string>();
    from.add(edge.toNodeId);
    adjacency.set(edge.fromNodeId, from);

    const to = adjacency.get(edge.toNodeId) ?? new Set<string>();
    to.add(edge.fromNodeId);
    adjacency.set(edge.toNodeId, to);
  }

  const component = new Set<string>();
  const queue: string[] = [root];
  while (queue.length > 0) {
    const current = queue.shift()!;
    if (component.has(current)) continue;
    component.add(current);
    for (const next of adjacency.get(current) ?? []) {
      if (!component.has(next)) queue.push(next);
    }
  }

  if (component.size === 0) component.add(root);

  const childrenByParent = new Map<string, string[]>();
  const parentByNode = new Map<string, string | null>([[root, null]]);
  const bfsQueue: string[] = [root];

  while (bfsQueue.length > 0) {
    const current = bfsQueue.shift()!;
    const neighbors = Array.from(adjacency.get(current) ?? [])
      .filter((nodeId) => component.has(nodeId) && !parentByNode.has(nodeId))
      .sort((a, b) => compareNodes(nodeById.get(a), nodeById.get(b)));

    if (neighbors.length > 0) {
      childrenByParent.set(current, neighbors);
    }

    for (const neighborId of neighbors) {
      parentByNode.set(neighborId, current);
      bfsQueue.push(neighborId);
    }
  }

  const rows: FlowRow[] = [];
  const walk = (nodeId: string, ancestorHasNext: boolean[]) => {
    const node = nodeById.get(nodeId);
    if (!node) return;

    rows.push({
      key: `${nodeId}:${rows.length}`,
      node,
      level: ancestorHasNext.length,
      ancestorHasNext,
      isCurrent: nodeId === root,
    });

    const children = childrenByParent.get(nodeId) ?? [];
    children.forEach((childId, index) => {
      walk(childId, [...ancestorHasNext, index < children.length - 1]);
    });
  };

  walk(root, []);
  return rows;
});

function sameId(a: string | null | undefined, b: string | null | undefined): boolean {
  return String(a ?? '').trim().toLowerCase() === String(b ?? '').trim().toLowerCase();
}

function compareNodes(a?: RelationshipGraphNode, b?: RelationshipGraphNode): number {
  const aDate = sortableDate(a?.subtitle);
  const bDate = sortableDate(b?.subtitle);
  if (aDate !== bDate) return aDate.localeCompare(bDate);

  const aDisplay = displayForNode(a).toLowerCase();
  const bDisplay = displayForNode(b).toLowerCase();
  return aDisplay.localeCompare(bDisplay);
}

function sortableDate(value: string | null | undefined): string {
  const raw = String(value ?? '').trim();
  if (!raw) return '9999-99-99';
  const date = new Date(raw);
  if (Number.isNaN(date.getTime())) return raw;
  return date.toISOString();
}

function statusIconStatus(node: RelationshipGraphNode): 'saved' | 'posted' | 'marked' {
  return documentStatusVisual(node.documentStatus);
}

function displayForNode(node: RelationshipGraphNode | null | undefined): string {
  const titleValue = String(node?.title ?? '').trim();
  if (titleValue) return titleValue;
  return String(node?.entityId ?? '').trim();
}

function formatMoney(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '';
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function amountLabel(node: RelationshipGraphNode): string {
  return formatMoney(node.amount);
}

function rowButtonClass(row: FlowRow): string {
  if (row.isCurrent) {
    return 'border-amber-300 bg-amber-50/70 shadow-card dark:border-amber-700/70 dark:bg-amber-950/20';
  }
  return 'border-ngb-border bg-ngb-card hover:border-ngb-accent/40 hover:bg-ngb-surface';
}

function rowPaddingLeft(level: number): string {
  return `${level * TREE_INDENT_PX}px`;
}

function lineLeft(level: number): string {
  return `${level * TREE_INDENT_PX + TREE_LINE_LEFT_PX}px`;
}

function ancestorLineStyle(level: number): Record<string, string> {
  return {
    left: lineLeft(level),
    top: `-${TREE_ROW_GAP_PX}px`,
    height: `calc(100% + ${TREE_ROW_GAP_PX * 2}px)`,
  };
}

function parentStemStyle(level: number): Record<string, string> {
  return {
    left: lineLeft(level - 1),
    top: `-${TREE_ROW_GAP_PX}px`,
    height: `calc(50% + ${TREE_ROW_GAP_PX}px)`,
  };
}

function parentElbowStyle(level: number): Record<string, string> {
  return {
    left: lineLeft(level - 1),
    top: '50%',
    width: `${TREE_INDENT_PX - TREE_LINE_LEFT_PX + 1}px`,
  };
}

function openSourceDocument(): void {
  if (!documentType.value || !documentId.value) return;
  void router.push(documentRoute.value);
}

async function goBack(): Promise<void> {
  const explicitBack = resolveBackTarget(route);
  if (explicitBack && explicitBack !== documentRoute.value) {
    await router.replace(documentRoute.value);
    return;
  }

  await navigateBack(router, route, documentRoute.value);
}

function openNode(node: RelationshipGraphNode): void {
  if (!node?.typeCode || !node?.entityId) return;
  void router.push(withBackTarget(buildDocumentFullPageUrl(node.typeCode, node.entityId), currentRouteBackTarget(route)));
}

async function copyShare(): Promise<void> {
  if (!canShareLink.value) return;
  await copyAppLink(router, toasts, shareRoute.value, {
    title: 'Document flow link copied',
    message: 'Shareable document flow page link copied to clipboard.',
  });
}

async function refreshPage(): Promise<void> {
  await load();
}

async function load(): Promise<void> {
  if (!documentType.value || !documentId.value) {
    error.value = 'Document type or id is missing.';
    return;
  }

  loading.value = true;
  error.value = null;
  try {
    graph.value = await getConfiguredNgbEditor().loadDocumentGraph(
      documentType.value,
      documentId.value,
      DEFAULT_DEPTH,
      DEFAULT_MAX_NODES,
    );
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Could not load document flow.');
    graph.value = null;
  } finally {
    loading.value = false;
  }
}

watch(
  () => [documentType.value, documentId.value],
  () => {
    void load();
  },
  { immediate: true },
);
</script>

<template>
  <div class="h-full min-h-0 flex flex-col bg-ngb-bg">
    <NgbPageHeader :title="title" can-back @back="goBack">
      <template #secondary>
        <NgbBadge :tone="statusTone">{{ statusLabel }}</NgbBadge>
      </template>

      <template #actions>
        <button
          class="ngb-iconbtn"
          :disabled="loading || !documentId"
          title="Open document"
          @click="openSourceDocument"
        >
          <NgbIcon name="edit" />
        </button>

        <button
          v-if="canShareLink"
          class="ngb-iconbtn"
          :disabled="loading"
          title="Share link"
          @click="copyShare"
        >
          <NgbIcon name="share" />
        </button>

        <button
          class="ngb-iconbtn"
          :disabled="loading"
          title="Refresh"
          @click="refreshPage"
        >
          <NgbIcon name="refresh" />
        </button>
      </template>
    </NgbPageHeader>

    <div class="p-6 pt-4 flex-1 min-h-0 overflow-auto">
      <div
        v-if="loading"
        class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-sm text-ngb-muted"
      >
        Loading document flow…
      </div>

      <div
        v-else-if="error"
        class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-4 text-sm text-ngb-danger dark:border-red-900/50 dark:bg-red-950/30"
      >
        {{ error }}
      </div>

      <div
        v-else-if="!flowRows.length"
        class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-sm text-ngb-muted"
      >
        This document has no related documents yet.
      </div>

      <div v-else class="space-y-[18px] py-2">
        <div
          v-for="row in flowRows"
          :key="row.key"
          class="relative"
          :style="{ paddingLeft: rowPaddingLeft(row.level) }"
        >
          <div class="pointer-events-none absolute inset-y-0 left-0" :style="{ width: rowPaddingLeft(row.level) }">
            <span
              v-for="(hasNext, index) in row.ancestorHasNext"
              v-show="hasNext"
              :key="`${row.key}:ancestor:${index}`"
              class="absolute border-l-[3px] border-slate-300/95 dark:border-slate-700/95"
              :style="ancestorLineStyle(index)"
            />

            <template v-if="row.level > 0">
              <span
                class="absolute border-l-[3px] border-slate-300/95 dark:border-slate-700/95"
                :style="parentStemStyle(row.level)"
              />
              <span
                class="absolute border-t-[3px] border-slate-300/95 dark:border-slate-700/95"
                :style="parentElbowStyle(row.level)"
              />
            </template>
          </div>

          <button
            type="button"
            class="group w-full min-w-0 rounded-[var(--ngb-radius)] border text-left transition-colors"
            :class="rowButtonClass(row)"
            :title="displayForNode(row.node)"
            @click="openNode(row.node)"
          >
            <div class="grid grid-cols-[30px,minmax(0,1fr),160px] items-center gap-3 px-4 py-3.5">
              <div class="flex items-center justify-center">
                <NgbStatusIcon :status="statusIconStatus(row.node)" />
              </div>

              <div class="min-w-0 truncate text-sm font-medium text-ngb-text">
                {{ displayForNode(row.node) }}
              </div>

              <div class="text-right text-sm font-semibold tabular-nums text-ngb-text">
                {{ amountLabel(row.node) }}
              </div>
            </div>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
