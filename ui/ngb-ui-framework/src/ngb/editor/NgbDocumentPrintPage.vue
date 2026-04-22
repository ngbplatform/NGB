<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import NgbIcon from '../primitives/NgbIcon.vue';
import { useMetadataStore } from '../metadata/store';
import { dataTypeKind } from '../metadata/dataTypes';
import { formatLooseEntityValue, formatTypedEntityValue } from '../metadata/entityValueFormatting';
import { isReferenceValue } from '../metadata/entityModel';
import { lookupHintFromSource } from '../metadata/lookup';
import type {
  ColumnMetadata,
  DocumentTypeMetadata,
  EntityFormModel,
  FieldMetadata,
  LookupHint,
} from '../metadata/types';
import { isNonEmptyGuid } from '../utils/guid';
import { toErrorMessage } from '../utils/errorMessage';
import { navigateBack, resolveBackTarget } from '../router/backNavigation';

import { getConfiguredNgbEditor, resolveNgbEditorPrintBehavior } from './config';
import { resolveDocumentReopenTarget } from './documentNavigation';
import { documentStatusLabel, normalizeDocumentStatusValue } from './documentStatus';
import type { DocumentRecord } from './types';

type PrintableFieldCell = {
  key: string;
  label: string;
  value: string;
};

type PrintableFieldRow = {
  key: string;
  cells: PrintableFieldCell[];
};

type PrintableSection = {
  key: string;
  title: string;
  rows: PrintableFieldRow[];
};

type PrintablePartSection = {
  key: string;
  title: string;
  columns: Array<{ key: string; label: string; align: 'left' | 'right' }>;
  rows: Array<{ key: string; cells: Record<string, string> }>;
};

const route = useRoute();
const router = useRouter();
const metadataStore = useMetadataStore();
const editorConfig = getConfiguredNgbEditor();
const lookupStore = editorConfig.lookupStore ?? null;

const loading = ref(false);
const error = ref<string | null>(null);
const metadata = ref<DocumentTypeMetadata | null>(null);
const documentRecord = ref<DocumentRecord | null>(null);
const autoPrintTriggered = ref(false);
const printedAt = ref(new Date());

const documentType = computed(() => String(route.params.documentType ?? '').trim());
const documentId = computed(() => String(route.params.id ?? '').trim());
const autoPrintRequested = computed(() => {
  const raw = Array.isArray(route.query.autoprint) ? route.query.autoprint[0] : route.query.autoprint;
  return String(raw ?? '').trim() === '1';
});

const documentRoute = computed(() => {
  if (!documentType.value || !documentId.value) return '/';
  return resolveDocumentReopenTarget(route, documentType.value, documentId.value);
});

async function goBack(): Promise<void> {
  const explicitBack = resolveBackTarget(route);
  if (explicitBack && explicitBack !== documentRoute.value) {
    await router.replace(documentRoute.value);
    return;
  }

  await navigateBack(router, route, documentRoute.value);
}

const sheetTitle = computed(() => {
  const display = String(documentRecord.value?.display ?? '').trim();
  if (display) return display;

  const number = String(documentRecord.value?.number ?? '').trim();
  const typeLabel = String(metadata.value?.displayName ?? 'Document').trim();
  if (number) return `${typeLabel} ${number}`;
  return typeLabel || 'Document';
});

const normalizedStatus = computed(() => normalizeDocumentStatusValue(documentRecord.value?.status));
const statusLabel = computed(() => documentStatusLabel(normalizedStatus.value));

const statusClass = computed(() => {
  const status = normalizedStatus.value;
  if (status === 2) return 'print-status posted';
  if (status === 3) return 'print-status deleted';
  return 'print-status draft';
});

const printableSections = computed<PrintableSection[]>(() => {
  const form = metadata.value?.form;
  const fields = documentRecord.value?.payload?.fields ?? {};
  if (!form?.sections) return [];

  return form.sections
    .map((section, sectionIndex) => ({
      key: `section:${section.title || sectionIndex}`,
      title: String(section.title ?? '').trim() || 'Main',
      rows: (section.rows ?? [])
        .map((row, rowIndex) => ({
          key: `row:${sectionIndex}:${rowIndex}`,
          cells: (row.fields ?? [])
            .filter((field) => !isHiddenDocumentField(field))
            .map((field) => ({
              key: field.key,
              label: field.label,
              value: formatFieldValue(field, (fields as EntityFormModel)[field.key]),
            })),
        }))
        .filter((row) => row.cells.length > 0),
    }))
    .filter((section) => section.rows.length > 0);
});

const printableParts = computed<PrintablePartSection[]>(() => {
  const partsMeta = metadata.value?.parts ?? [];
  const partsPayload = documentRecord.value?.payload?.parts ?? {};

  return partsMeta
    .map((part) => {
      const rows = Array.isArray(partsPayload?.[part.partCode]?.rows) ? partsPayload[part.partCode]!.rows : [];
      const columns = (part.list?.columns ?? []).map((column) => ({
        key: column.key,
        label: column.label,
        align:
          dataTypeKind(column.dataType) === 'Money'
          || dataTypeKind(column.dataType) === 'Decimal'
          || dataTypeKind(column.dataType) === 'Int32'
            ? 'right'
            : 'left',
      }));

      return {
        key: part.partCode,
        title: String(part.title ?? part.partCode).trim() || part.partCode,
        columns,
        rows: rows.map((row, index) => ({
          key: `${part.partCode}:${index}`,
          cells: Object.fromEntries(
            columns.map((column) => {
              const metaColumn = (part.list?.columns ?? []).find((entry) => entry.key === column.key);
              return [column.key, formatPartCell(metaColumn ?? null, row?.[column.key])];
            }),
          ),
        })),
      };
    })
    .filter((part) => part.columns.length > 0 && part.rows.length > 0);
});

watch(
  () => sheetTitle.value,
  (next) => {
    if (typeof document === 'undefined') return;
    document.title = String(next ?? '').trim() || 'Document print';
  },
  { immediate: true },
);

function handleBeforePrint() {
  if (typeof document === 'undefined') return;
  document.title = ' ';
}

function handleAfterPrint() {
  if (typeof document === 'undefined') return;
  document.title = String(sheetTitle.value ?? '').trim() || 'Document print';
}

onMounted(() => {
  if (typeof window === 'undefined') return;
  window.addEventListener('beforeprint', handleBeforePrint);
  window.addEventListener('afterprint', handleAfterPrint);
});

onBeforeUnmount(() => {
  if (typeof window === 'undefined') return;
  window.removeEventListener('beforeprint', handleBeforePrint);
  window.removeEventListener('afterprint', handleAfterPrint);
  handleAfterPrint();
});

watch(
  () => [documentType.value, documentId.value] as const,
  () => {
    autoPrintTriggered.value = false;
    printedAt.value = new Date();
    void load();
  },
  { immediate: true },
);

watch(
  () => [loading.value, error.value, autoPrintRequested.value, metadata.value?.documentType, documentRecord.value?.id] as const,
  async () => {
    if (!autoPrintRequested.value || autoPrintTriggered.value || loading.value || error.value || !metadata.value || !documentRecord.value) return;
    autoPrintTriggered.value = true;
    await nextTick();
    window.setTimeout(() => {
      if (typeof window !== 'undefined') window.print();
    }, 120);
  },
);

async function load() {
  if (!documentType.value || !documentId.value) return;

  loading.value = true;
  error.value = null;

  try {
    const [nextMetadata, nextDocument] = await Promise.all([
      metadataStore.ensureDocumentType(documentType.value),
      editorConfig.loadDocumentById(documentType.value, documentId.value),
    ]);

    metadata.value = nextMetadata;
    documentRecord.value = nextDocument;
    await prefetchLookupLabels(nextMetadata, nextDocument);
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to load the print preview.');
  } finally {
    loading.value = false;
  }
}

function triggerPrint() {
  if (typeof window === 'undefined') return;
  window.print();
}

function isHiddenDocumentField(field: FieldMetadata): boolean {
  return field.key === 'display' || field.key === 'number';
}

function resolveLookupHint(fieldKey: string, lookup?: FieldMetadata['lookup'] | ColumnMetadata['lookup'] | null): LookupHint | null {
  return resolveNgbEditorPrintBehavior().resolveLookupHint?.({
    documentType: documentType.value,
    fieldKey,
    lookup: lookup ?? null,
  }) ?? lookupHintFromSource(lookup ?? null);
}

function labelForHint(hint: LookupHint, id: string): string {
  if (!lookupStore) return id;
  if (hint.kind === 'catalog') return lookupStore.labelForCatalog(hint.catalogType, id);
  if (hint.kind === 'coa') return lookupStore.labelForCoa(id);
  return lookupStore.labelForAnyDocument(hint.documentTypes, id);
}

function formatValueWithHint(value: unknown, hint: LookupHint | null): string | null {
  if (isReferenceValue(value)) return formatLooseEntityValue(value);
  if (!hint || !isNonEmptyGuid(value)) return null;
  return labelForHint(hint, value);
}

function formatFieldValue(field: FieldMetadata, value: unknown): string {
  const hint = resolveLookupHint(field.key, field.lookup);
  const fromHint = formatValueWithHint(value, hint);
  if (fromHint) return fromHint;
  return formatTypedEntityValue(field.dataType, value);
}

function formatPartCell(column: ColumnMetadata | null, value: unknown): string {
  if (!column) return formatLooseEntityValue(value);
  const hint = resolveLookupHint(column.key, column.lookup);
  const fromHint = formatValueWithHint(value, hint);
  if (fromHint) return fromHint;
  return formatTypedEntityValue(column.dataType, value);
}

type LookupBuckets = {
  catalogs: Map<string, Set<string>>;
  documents: Map<string, { types: string[]; ids: Set<string> }>;
  coaIds: Set<string>;
};

function collectLookupValue(hint: LookupHint | null, value: unknown, buckets: LookupBuckets) {
  if (!hint) return;
  if (isReferenceValue(value) && value.display.trim().length > 0) return;

  const id = isReferenceValue(value) ? value.id : (isNonEmptyGuid(value) ? value : null);
  if (!id) return;

  if (hint.kind === 'catalog') {
    const ids = buckets.catalogs.get(hint.catalogType) ?? new Set<string>();
    ids.add(id);
    buckets.catalogs.set(hint.catalogType, ids);
    return;
  }

  if (hint.kind === 'coa') {
    buckets.coaIds.add(id);
    return;
  }

  const key = hint.documentTypes.join('|');
  const entry = buckets.documents.get(key) ?? { types: [...hint.documentTypes], ids: new Set<string>() };
  entry.ids.add(id);
  buckets.documents.set(key, entry);
}

async function prefetchLookupLabels(meta: DocumentTypeMetadata, doc: DocumentRecord) {
  if (!lookupStore) return;

  const buckets: LookupBuckets = {
    catalogs: new Map(),
    documents: new Map(),
    coaIds: new Set(),
  };

  const fields = doc.payload?.fields ?? {};
  for (const section of meta.form?.sections ?? []) {
    for (const row of section.rows ?? []) {
      for (const field of row.fields ?? []) {
        collectLookupValue(resolveLookupHint(field.key, field.lookup), (fields as EntityFormModel)[field.key], buckets);
      }
    }
  }

  const partsPayload = doc.payload?.parts ?? {};
  for (const part of meta.parts ?? []) {
    const rows = Array.isArray(partsPayload?.[part.partCode]?.rows) ? partsPayload[part.partCode]!.rows : [];
    for (const row of rows) {
      for (const column of part.list?.columns ?? []) {
        collectLookupValue(resolveLookupHint(column.key, column.lookup), row?.[column.key], buckets);
      }
    }
  }

  const tasks: Promise<unknown>[] = [];

  for (const [catalogType, ids] of buckets.catalogs.entries()) {
    tasks.push(Promise.resolve().then(() => lookupStore.ensureCatalogLabels(catalogType, Array.from(ids))));
  }

  if (buckets.coaIds.size > 0) {
    tasks.push(Promise.resolve().then(() => lookupStore.ensureCoaLabels(Array.from(buckets.coaIds))));
  }

  for (const entry of buckets.documents.values()) {
    tasks.push(Promise.resolve().then(() => lookupStore.ensureAnyDocumentLabels(entry.types, Array.from(entry.ids))));
  }

  if (tasks.length > 0) {
    await Promise.allSettled(tasks);
  }
}
</script>

<template>
  <div class="document-print-root">
    <div class="document-print-toolbar">
      <button class="document-print-toolbar-button" @click="goBack">
        <NgbIcon name="arrow-left" :size="16" />
        <span>Back</span>
      </button>

      <div class="document-print-toolbar-title">Print preview</div>

      <button class="document-print-toolbar-button" :disabled="loading || !!error" @click="triggerPrint">
        <NgbIcon name="printer" :size="16" />
        <span>Print</span>
      </button>
    </div>

    <main class="document-print-shell">
      <div v-if="error" class="document-print-error">
        {{ error }}
      </div>

      <div v-else-if="loading" class="document-print-loading">
        Loading print preview...
      </div>

      <article v-else class="document-print-sheet">
        <header class="document-print-header">
          <div class="document-print-header-main">
            <h1 class="document-print-title">{{ sheetTitle }}</h1>
          </div>

          <div class="document-print-meta">
            <div>
              <div class="document-print-meta-label">Status</div>
              <div :class="statusClass">{{ statusLabel }}</div>
            </div>

            <div>
              <div class="document-print-meta-label">Printed</div>
              <div class="document-print-meta-value">{{ printedAt.toLocaleString() }}</div>
            </div>
          </div>
        </header>

        <section v-for="section in printableSections" :key="section.key" class="document-print-section">
          <h2 class="document-print-section-title">{{ section.title }}</h2>

          <div class="document-print-field-rows">
            <div v-for="row in section.rows" :key="row.key" class="document-print-field-row">
              <div v-for="cell in row.cells" :key="cell.key" class="document-print-field-cell">
                <div class="document-print-field-label">{{ cell.label }}</div>
                <div class="document-print-field-value">{{ cell.value }}</div>
              </div>
            </div>
          </div>
        </section>

        <section v-for="part in printableParts" :key="part.key" class="document-print-section">
          <h2 class="document-print-section-title">{{ part.title }}</h2>

          <div class="document-print-table-wrap">
            <table class="document-print-table">
              <thead>
                <tr>
                  <th
                    v-for="column in part.columns"
                    :key="column.key"
                    :class="column.align === 'right' ? 'align-right' : ''"
                  >
                    {{ column.label }}
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in part.rows" :key="row.key">
                  <td
                    v-for="column in part.columns"
                    :key="column.key"
                    :class="column.align === 'right' ? 'align-right' : ''"
                  >
                    {{ row.cells[column.key] }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>
      </article>
    </main>
  </div>
</template>

<style scoped>
.document-print-root {
  min-height: 100vh;
  background: var(--ngb-bg);
  color: var(--ngb-text);
}

.document-print-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 16px 24px;
  border-bottom: 1px solid var(--ngb-border);
  background: var(--ngb-card);
}

.document-print-toolbar-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--ngb-muted);
}

.document-print-toolbar-button {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  height: 36px;
  padding: 0 12px;
  border: 1px solid var(--ngb-border);
  border-radius: var(--ngb-radius);
  background: var(--ngb-card);
  color: var(--ngb-text);
  cursor: pointer;
  transition: background-color 120ms ease, border-color 120ms ease, color 120ms ease;
}

.document-print-toolbar-button:hover:not(:disabled) {
  background: var(--ngb-bg);
}

.document-print-toolbar-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.document-print-shell {
  padding: 24px;
}

.document-print-loading,
.document-print-error {
  max-width: 980px;
  margin: 0 auto;
  padding: 18px 20px;
  border-radius: var(--ngb-radius);
  border: 1px solid var(--ngb-border);
  background: var(--ngb-card);
}

.document-print-error {
  color: var(--ngb-danger);
  border-color: color-mix(in srgb, var(--ngb-danger) 30%, var(--ngb-border));
}

.document-print-sheet {
  width: min(980px, 100%);
  margin: 0 auto;
  background: #fff;
  color: #111827;
  border: 1px solid #dbe3ef;
  border-radius: var(--ngb-radius);
  box-shadow: 0 18px 46px rgba(15, 23, 42, 0.08);
  padding: 28px 32px 36px;
}

.document-print-header {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 24px;
  padding-bottom: 20px;
  border-bottom: 1px solid #dbe3ef;
}

.document-print-header-main {
  min-width: 0;
  flex: 1 1 auto;
}

.document-print-title {
  margin: 0;
  font-size: 28px;
  line-height: 1.2;
}

.document-print-meta {
  flex: 0 0 auto;
  align-self: flex-end;
  display: grid;
  grid-template-columns: repeat(2, minmax(140px, max-content));
  gap: 12px 24px;
}

.document-print-meta-label {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #6b7280;
}

.document-print-meta-value {
  margin-top: 4px;
  font-size: 14px;
  line-height: 1.4;
}

.print-status {
  display: inline-flex;
  align-items: center;
  min-height: 28px;
  padding: 0 10px;
  border-radius: 999px;
  font-size: 13px;
  font-weight: 600;
  margin-top: 4px;
}

.print-status.draft {
  color: #475569;
  background: #f1f5f9;
}

.print-status.posted {
  color: #166534;
  background: #dcfce7;
}

.print-status.deleted {
  color: #991b1b;
  background: #fee2e2;
}

.document-print-section {
  margin-top: 24px;
  break-inside: avoid;
}

.document-print-section-title {
  margin: 0 0 12px;
  font-size: 16px;
  line-height: 1.3;
}

.document-print-field-rows {
  display: grid;
  gap: 12px;
}

.document-print-field-row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 20px;
}

.document-print-field-cell {
  min-width: 0;
  padding: 12px 14px;
  border: 1px solid #e5e7eb;
  border-radius: var(--ngb-radius);
}

.document-print-field-cell:only-child {
  grid-column: 1 / -1;
}

.document-print-field-label {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: #6b7280;
}

.document-print-field-value {
  margin-top: 6px;
  font-size: 14px;
  line-height: 1.45;
  white-space: pre-wrap;
  word-break: break-word;
}

.document-print-table-wrap {
  overflow: hidden;
  border: 1px solid #dbe3ef;
  border-radius: var(--ngb-radius);
}

.document-print-table {
  width: 100%;
  border-collapse: collapse;
}

.document-print-table th,
.document-print-table td {
  padding: 10px 12px;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
  font-size: 13px;
  line-height: 1.4;
  vertical-align: top;
}

.document-print-table th {
  background: #f8fafc;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: #6b7280;
}

.document-print-table tr:last-child td {
  border-bottom: 0;
}

.document-print-table .align-right {
  text-align: right;
}

@media print {
  .document-print-root {
    background: #fff;
  }

  .document-print-toolbar {
    display: none;
  }

  .document-print-shell {
    padding: 0;
  }

  .document-print-sheet {
    width: 100%;
    margin: 0;
    border: 0;
    box-shadow: none;
    border-radius: 0;
    padding: 0;
  }
}
</style>
