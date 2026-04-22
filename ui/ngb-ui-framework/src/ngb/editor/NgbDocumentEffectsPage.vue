<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import NgbBadge from '../primitives/NgbBadge.vue';
import NgbIcon from '../primitives/NgbIcon.vue';
import NgbTabs from '../primitives/NgbTabs.vue';
import { useMetadataStore } from '../metadata/store';
import type { DocumentTypeMetadata } from '../metadata/types';
import { useToasts } from '../primitives/toast';
import NgbRegisterGrid from '../components/register/NgbRegisterGrid.vue';
import NgbPageHeader from '../site/NgbPageHeader.vue';
import { copyAppLink } from '../router/shareLink';
import { currentRouteBackTarget, navigateBack, resolveBackTarget, withBackTarget } from '../router/backNavigation';
import { toErrorMessage } from '../utils/errorMessage';
import { isEmptyGuid, isNonEmptyGuid, shortGuid } from '../utils/guid';

import { getConfiguredNgbEditor, resolveNgbEditorEffectsBehavior } from './config';
import {
  buildDocumentEffectsPageUrl,
  resolveDocumentReopenTarget,
} from './documentNavigation';
import {
  collectAccountingEntryAccountIds,
  finalizeEffectDimensionSummary,
  resolveEffectAccountLabel as resolveDefaultEffectAccountLabel,
} from './documentEffects';
import { formatOccurredAtUtcValue } from './documentEffectsDateFormatting';
import { documentStatusLabel, documentStatusTone } from './documentStatus';
import type {
  DocumentEffects,
  DocumentRecord,
  EffectAccount,
  EffectDimensionValue,
  EffectResourceValue,
} from './types';

const route = useRoute();
const router = useRouter();
const toasts = useToasts();
const metadataStore = useMetadataStore();
const editorConfig = getConfiguredNgbEditor();
const lookupStore = editorConfig.lookupStore ?? null;

const loading = ref(false);
const error = ref<string | null>(null);
const metadata = ref<DocumentTypeMetadata | null>(null);
const document = ref<DocumentRecord | null>(null);
const effects = ref<DocumentEffects | null>(null);
const activeTab = ref<'accounting' | 'or' | 'rr'>('accounting');
let loadSequence = 0;

const documentType = computed(() => String(route.params.documentType ?? '').trim());
const documentId = computed(() => String(route.params.id ?? '').trim());

const documentRoute = computed(() => {
  if (!documentType.value || !documentId.value) return '/';
  return resolveDocumentReopenTarget(route, documentType.value, documentId.value);
});

const shareRoute = computed(() => {
  if (!documentType.value || !documentId.value) return '/';
  return buildDocumentEffectsPageUrl(documentType.value, documentId.value);
});

const tabs = computed(() => [
  { key: 'accounting', label: `Accounting Entries (${effects.value?.accountingEntries?.length ?? 0})` },
  { key: 'or', label: `Operational Registers (${effects.value?.operationalRegisterMovements?.length ?? 0})` },
  { key: 'rr', label: `Reference Registers (${effects.value?.referenceRegisterWrites?.length ?? 0})` },
]);

const title = computed(() => {
  const display = String(document.value?.display ?? '').trim();
  if (display) return display;
  return metadata.value?.displayName ?? 'Document effects';
});

const statusTone = computed<'neutral' | 'success' | 'warn'>(() => documentStatusTone(document.value?.status));
const statusLabel = computed(() => documentStatusLabel(document.value?.status));
const canShareLink = computed(() => !!documentType.value && !!documentId.value);

const accountingColumns = computed(() => [
  { key: 'occurred', title: 'Occurred', width: 190, pinned: 'left' as const },
  { key: 'account', title: 'Account', width: 320 },
  { key: 'debit', title: 'Debit', width: 140, align: 'right' as const },
  { key: 'credit', title: 'Credit', width: 140, align: 'right' as const },
  { key: 'dimensionSet', title: 'Dimension Set' },
]);

const operationalColumns = computed(() => [
  { key: 'occurred', title: 'Occurred', width: 190, pinned: 'left' as const },
  { key: 'register', title: 'Operational Register', width: 260 },
  { key: 'dimensionSet', title: 'Dimension Set', width: 320 },
  { key: 'resources', title: 'Resources' },
]);

const referenceColumns = computed(() => [
  { key: 'recorded', title: 'Recorded', width: 190, pinned: 'left' as const },
  { key: 'register', title: 'Reference Register', width: 260 },
  { key: 'status', title: 'Status', width: 140 },
  { key: 'dimensionSet', title: 'Dimension Set', width: 320 },
  { key: 'fields', title: 'Fields' },
]);

const accountingRows = computed(() => {
  const items = effects.value?.accountingEntries ?? [];
  return items.flatMap((item) => {
    const occurred = formatUtc(item.occurredAtUtc);
    const debitRow = {
      key: `${effectKey(item.entryId)}:debit`,
      occurred,
      account: resolveEffectAccountLabel(item.debitAccount, item.debitAccountId),
      debit: formatMoney(item.amount),
      credit: '—',
      dimensionSet: formatDimensionSummary(item.debitDimensions, item.debitDimensionSetId),
      __status: 'posted',
    };

    const creditRow = {
      key: `${effectKey(item.entryId)}:credit`,
      occurred,
      account: resolveEffectAccountLabel(item.creditAccount, item.creditAccountId),
      debit: '—',
      credit: formatMoney(item.amount),
      dimensionSet: formatDimensionSummary(item.creditDimensions, item.creditDimensionSetId),
      __status: 'posted',
    };

    return [debitRow, creditRow];
  });
});

const operationalRows = computed(() => {
  const items = effects.value?.operationalRegisterMovements ?? [];
  return items.map((item) => ({
    key: effectKey(item.movementId),
    occurred: formatUtc(item.occurredAtUtc),
    register: formatRegisterName(item.registerName, item.registerCode),
    dimensionSet: formatDimensionSummary(item.dimensions, item.dimensionSetId),
    resources: formatResourceSummary(item.resources),
    __status: 'posted',
  }));
});

const referenceRows = computed(() => {
  const items = effects.value?.referenceRegisterWrites ?? [];
  return items.map((item) => ({
    key: effectKey(item.recordId),
    recorded: formatUtc(item.recordedAtUtc),
    register: formatRegisterName(item.registerName, item.registerCode),
    status: item.isTombstone ? 'Tombstone' : 'Write',
    dimensionSet: formatDimensionSummary(item.dimensions, item.dimensionSetId),
    fields: formatFieldSummary(item.fields),
    __status: item.isTombstone ? 'marked' : 'posted',
  }));
});

function effectKey(value: string | number | null | undefined): string {
  return String(value ?? '');
}

function formatUtc(value: string | null | undefined): string {
  if (!value) return '—';
  return formatOccurredAtUtcValue(String(value));
}

function formatMoney(value: number | null | undefined): string {
  const amount = Number(value ?? 0);
  return amount.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function formatScalar(value: unknown): string {
  if (value == null) return '—';
  if (typeof value === 'string') return value.trim() || '—';
  if (typeof value === 'number') return Number.isFinite(value) ? formatMoney(value) : String(value);
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (Array.isArray(value)) return value.map((x) => formatScalar(x)).join(' · ');
  if (typeof value === 'object') {
    const entries = Object.entries(value as Record<string, unknown>);
    if (entries.length === 0) return '—';
    return entries.map(([key, item]) => `${humanizeCode(key)}: ${formatScalar(item)}`).join(' · ');
  }
  return String(value);
}

function formatRegisterName(name?: string | null, code?: string | null): string {
  const label = String(name ?? '').trim();
  if (label) return label;
  return humanizeCode(code);
}

function defaultResolveDimensionDisplay(item: EffectDimensionValue | null | undefined): string {
  const display = String(item?.display ?? '').trim();
  if (display) return display;
  const valueId = String(item?.valueId ?? '').trim();
  return valueId ? shortGuid(valueId) : '—';
}

function resolveDimensionDisplay(item: EffectDimensionValue | null | undefined): string {
  return resolveNgbEditorEffectsBehavior().resolveDimensionDisplay?.({
    item,
    lookupStore,
  }) ?? defaultResolveDimensionDisplay(item);
}

function resolveEffectAccountLabel(account: EffectAccount | null | undefined, accountId: string | null | undefined): string {
  return resolveNgbEditorEffectsBehavior().resolveAccountLabel?.({
    account,
    accountId,
    lookupStore,
  }) ?? resolveDefaultEffectAccountLabel(account, accountId, lookupStore ? lookupStore.labelForCoa.bind(lookupStore) : null);
}

function formatDimensionSummary(
  values: EffectDimensionValue[] | null | undefined,
  fallbackDimensionSetId?: string | null,
): string | string[] {
  return finalizeEffectDimensionSummary(
    (values ?? []).map((item) => resolveDimensionDisplay(item)),
    fallbackDimensionSetId,
  );
}

function formatResourceSummary(value: EffectResourceValue[] | Record<string, unknown> | null | undefined): string | string[] {
  if (Array.isArray(value)) {
    const parts = value
      .map((item) => {
        const code = humanizeCode(item?.code);
        const formatted = formatMoney(Number(item?.value ?? 0));
        return code ? `${code}: ${formatted}` : formatted;
      })
      .filter((item) => !!item);

    return parts.length > 0 ? parts : '—';
  }

  if (value && typeof value === 'object') {
    const parts = Object.entries(value)
      .map(([key, item]) => `${humanizeCode(key)}: ${formatScalar(item)}`)
      .filter((item) => !!item);

    return parts.length > 0 ? parts : '—';
  }

  return '—';
}

function formatFieldSummary(fields: Record<string, unknown> | null | undefined): string | string[] {
  if (!fields || typeof fields !== 'object') return '—';

  const parts = Object.entries(fields)
    .map(([key, value]) => {
      const label = key.endsWith('_document_id')
        ? humanizeCode(key.slice(0, -3))
        : humanizeCode(key)
      const resolved = resolveNgbEditorEffectsBehavior().resolveFieldValue?.({
        documentType: documentType.value,
        documentId: documentId.value,
        document: document.value,
        fieldKey: key,
        value,
        fields,
        lookupStore,
      })

      return `${label}: ${resolved ?? formatScalar(value)}`
    })
    .filter((item) => !!item);

  return parts.length > 0 ? parts : '—';
}

function humanizeCode(value: string | null | undefined): string {
  const raw = String(value ?? '').trim();
  if (!raw) return '';

  const last = raw.split('.').pop() ?? raw;
  return last
    .split(/[_\-\s]+/g)
    .filter((part) => !!part)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
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

async function copyShare(): Promise<void> {
  if (!canShareLink.value) return;
  await copyAppLink(router, toasts, shareRoute.value, {
    title: 'Effects link copied',
    message: 'Shareable effects page link copied to clipboard.',
  });
}

async function refreshPage(): Promise<void> {
  await load();
}

async function prefetchDefaultAccountLabels(snapshot: DocumentEffects | null | undefined): Promise<void> {
  if (!lookupStore) return;
  const ids = collectAccountingEntryAccountIds(snapshot);
  if (ids.length === 0) return;
  await lookupStore.ensureCoaLabels(ids);
}

async function load(): Promise<void> {
  if (!documentType.value || !documentId.value) {
    loadSequence += 1;
    error.value = 'Document type or id is missing.';
    metadata.value = null;
    document.value = null;
    effects.value = null;
    loading.value = false;
    return;
  }

  const seq = ++loadSequence;
  loading.value = true;
  error.value = null;
  try {
    const [meta, doc, effectsSnapshot] = await Promise.all([
      metadataStore.ensureDocumentType(documentType.value),
      editorConfig.loadDocumentById(documentType.value, documentId.value),
      editorConfig.loadDocumentEffects(documentType.value, documentId.value),
    ]);

    const behavior = resolveNgbEditorEffectsBehavior();
    const ancillaryTasks: Promise<unknown>[] = [
      Promise.resolve().then(() => prefetchDefaultAccountLabels(effectsSnapshot)),
    ];

    if (behavior.prefetchRelatedLabels) {
      ancillaryTasks.push(
        Promise.resolve().then(() => behavior.prefetchRelatedLabels?.({
          documentType: documentType.value,
          documentId: documentId.value,
          effects: effectsSnapshot,
          lookupStore,
        })),
      );
    }

    await Promise.allSettled(ancillaryTasks);

    if (seq !== loadSequence) return;

    metadata.value = meta;
    document.value = doc;
    effects.value = effectsSnapshot;
  } catch (cause) {
    if (seq !== loadSequence) return;
    error.value = toErrorMessage(cause, 'Could not load document effects.');
    metadata.value = null;
    document.value = null;
    effects.value = null;
  } finally {
    if (seq === loadSequence) loading.value = false;
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
        <div class="flex min-w-0 items-center">
          <NgbBadge :tone="statusTone">{{ statusLabel }}</NgbBadge>
        </div>
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

    <div class="p-6 pt-4 flex-1 min-h-0 flex flex-col">
      <div v-if="loading" class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-sm text-ngb-muted flex-1">
        Loading accounting entries / effects…
      </div>

      <div v-else-if="error" class="rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-4 text-sm text-ngb-danger dark:border-red-900/50 dark:bg-red-950/30">
        {{ error }}
      </div>

      <NgbTabs v-else v-model="activeTab" :tabs="tabs" fill class="flex-1 min-h-0">
        <template #default="{ active }">
          <section v-if="active === 'accounting'" class="h-full min-h-0 flex flex-col">
            <div
              v-if="!accountingRows.length"
              class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-sm text-ngb-muted flex-1"
            >
              No accounting entries were returned for this document.
            </div>

            <div v-else class="flex-1 min-h-0">
              <NgbRegisterGrid
                class="h-full min-h-0"
                fill-height
                :show-panel="false"
                :columns="accountingColumns"
                :rows="accountingRows"
                :show-totals="false"
                :storage-key="`ngb:document-effects:${documentType}:accounting`"
                :row-height-px="80"
                :activate-on-row-click="true"
                @rowActivate="openSourceDocument"
              />
            </div>
          </section>

          <section v-else-if="active === 'or'" class="h-full min-h-0 flex flex-col">
            <div
              v-if="!operationalRows.length"
              class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-sm text-ngb-muted flex-1"
            >
              No operational register movements were returned for this document.
            </div>

            <div v-else class="flex-1 min-h-0">
              <NgbRegisterGrid
                class="h-full min-h-0"
                fill-height
                :show-panel="false"
                :columns="operationalColumns"
                :rows="operationalRows"
                :show-totals="false"
                :storage-key="`ngb:document-effects:${documentType}:or`"
                :row-height-px="80"
                :activate-on-row-click="true"
                @rowActivate="openSourceDocument"
              />
            </div>
          </section>

          <section v-else class="h-full min-h-0 flex flex-col">
            <div
              v-if="!referenceRows.length"
              class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-6 text-sm text-ngb-muted"
            >
              No reference register writes were returned for this document.
            </div>

            <div v-else class="flex-1 min-h-0">
              <NgbRegisterGrid
                class="h-full min-h-0"
                fill-height
                :show-panel="false"
                :columns="referenceColumns"
                :rows="referenceRows"
                :show-totals="false"
                :storage-key="`ngb:document-effects:${documentType}:rr`"
                :row-height-px="80"
                :activate-on-row-click="true"
                @rowActivate="openSourceDocument"
              />
            </div>
          </section>
        </template>
      </NgbTabs>
    </div>
  </div>
</template>
