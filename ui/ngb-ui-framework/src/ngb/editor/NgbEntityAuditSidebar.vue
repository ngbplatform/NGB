<script setup lang="ts">
import { computed, ref, watch } from 'vue';

import { toErrorMessage } from '../utils/errorMessage';
import { stableStringify } from '../utils/stableValue';
import NgbIcon from '../primitives/NgbIcon.vue';

import { getConfiguredNgbEditor, resolveNgbEditorAuditBehavior } from './config';
import type { EditorAuditBehavior } from './types';
import type { AuditEvent } from './types';

const props = defineProps<{
  open: boolean;
  entityKind: number;
  entityId?: string | null;
  entityTitle?: string | null;
  behavior?: EditorAuditBehavior;
}>();

const emit = defineEmits<{
  (e: 'back'): void;
  (e: 'close'): void;
}>();

const loading = ref(false);
const error = ref<string | null>(null);
const items = ref<AuditEvent[]>([]);
let loadSequence = 0;

const canLoad = computed(() => !!props.entityId);
const title = computed(() => props.entityTitle?.trim() || 'Audit Log');
const behavior = computed(() => resolveNgbEditorAuditBehavior(props.behavior));
const hiddenFieldNames = computed(() => new Set((behavior.value.hiddenFieldNames ?? []).map((item) => item.toLowerCase())));
const explicitFieldLabels = computed(() => {
  const entries = Object.entries(behavior.value.explicitFieldLabels ?? {});
  return Object.fromEntries(entries.map(([key, value]) => [key.toLowerCase(), value]));
});

function formatDateTime(v: string): string {
  const d = new Date(v);
  if (Number.isNaN(d.getTime())) return v;
  return d.toLocaleString([], {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function actorLabel(e: AuditEvent): string {
  const a = e.actor;
  if (!a) return 'System';
  return a.displayName?.trim() || a.email?.trim() || 'System';
}

function humanizeField(fieldPath: string): string {
  const raw = String(fieldPath ?? '');
  const last = raw.split('.').pop() || raw;
  const explicit = explicitFieldLabels.value[last.toLowerCase()];
  if (explicit) return explicit;
  return last
    .replace(/_/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/\bid\b/gi, 'ID')
    .replace(/\butc\b/gi, 'UTC')
    .replace(/\b\w/g, (m) => m.toUpperCase());
}

function humanizeLooseText(v: string): string {
  return String(v ?? '')
    .replace(/[._]/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/\b\w/g, (m) => m.toUpperCase())
    .trim();
}

function parseJsonLoose(v?: string | null): unknown {
  if (v == null || v === '') return null;
  try {
    return JSON.parse(v);
  } catch {
    return v;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

function looksLikeIsoDateTime(v: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/.test(v);
}

function looksLikeDateOnly(v: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(v);
}

function formatPrimitiveString(v: string): string {
  const trimmed = v.trim();
  if (!trimmed) return '—';

  if (looksLikeIsoDateTime(trimmed)) return formatDateTime(trimmed);

  if (looksLikeDateOnly(trimmed)) {
    const d = new Date(`${trimmed}T00:00:00`);
    if (!Number.isNaN(d.getTime())) return d.toLocaleDateString();
  }

  if (/^(true|false)$/i.test(trimmed)) {
    return trimmed.toLowerCase() === 'true' ? 'Yes' : 'No';
  }

  if (/^[a-z]+([A-Z][a-z0-9]+)+$/.test(trimmed) || trimmed.includes('_')) {
    return humanizeLooseText(trimmed);
  }

  return trimmed;
}

function formatAuditValue(parsed: unknown): string {
  if (parsed == null || parsed === '') return '—';
  if (typeof parsed === 'boolean') return parsed ? 'Yes' : 'No';
  if (typeof parsed === 'number') return String(parsed);
  if (typeof parsed === 'string') return formatPrimitiveString(parsed);
  if (Array.isArray(parsed)) {
    if (parsed.length === 0) return '—';
    return parsed.map((entry) => formatAuditValue(entry)).join(', ');
  }
  if (isRecord(parsed)) {
    for (const key of ['display', 'label', 'name', 'email', 'id']) {
      const value = parsed[key];
      if (typeof value === 'string' && value.trim()) return value;
    }
    return stableStringify(parsed);
  }
  return String(parsed);
}

function valueText(v?: string | null): string {
  return formatAuditValue(parseJsonLoose(v));
}

function actionTitle(actionCode: string): string {
  const code = String(actionCode ?? '').toLowerCase();
  if (code.endsWith('.create') || code.includes('.create_')) return 'Created';
  if (code.endsWith('.update') || code.includes('.update_') || code.includes('.replace_')) return 'Updated';
  if (code.endsWith('.submit')) return 'Submitted';
  if (code.endsWith('.approve')) return 'Approved';
  if (code.endsWith('.reject')) return 'Rejected';
  if (code.endsWith('.post')) return 'Posted';
  if (code.endsWith('.unpost')) return 'Unposted';
  if (code.endsWith('.repost')) return 'Reposted';
  if (code.includes('unmark_for_deletion') || code.includes('restore')) return 'Restored';
  if (code.includes('mark_for_deletion') || code.includes('soft_delete') || code.includes('.tombstone')) {
    return 'Marked for deletion';
  }
  if (code.includes('set_active')) return 'Status changed';
  return humanizeLooseText(actionCode);
}

function eventSummary(item: AuditEvent): string {
  return `${actionTitle(item.actionCode)} by ${actorLabel(item)}`;
}

function sameText(a: string, b: string): boolean {
  return a.trim() === b.trim();
}

function eventRows(item: AuditEvent): Array<{ field: string; before: string; after: string }> {
  const changes = Array.isArray(item.changes) ? item.changes : [];

  const rows = changes
    .filter((change) => {
      const key = String(change.fieldPath ?? '').split('.').pop()?.toLowerCase() ?? '';
      return !hiddenFieldNames.value.has(key);
    })
    .map((change) => ({
      field: humanizeField(change.fieldPath),
      before: valueText(change.oldValueJson),
      after: valueText(change.newValueJson),
    }))
    .filter((row) => !(sameText(row.before, row.after) && row.before !== '—'));

  if (rows.length > 0) return rows;
  return [{ field: 'Details', before: '—', after: 'No business-field changes recorded.' }];
}

async function load() {
  if (!props.open || !props.entityId) return;
  const seq = ++loadSequence;
  loading.value = true;
  error.value = null;
  try {
    const page = await getConfiguredNgbEditor().loadEntityAuditLog(props.entityKind, props.entityId, { limit: 100 });
    if (seq !== loadSequence) return;
    items.value = page.items ?? [];
  } catch (cause) {
    if (seq !== loadSequence) return;
    error.value = toErrorMessage(cause, 'Failed to load the audit log.');
  } finally {
    if (seq === loadSequence) loading.value = false;
  }
}

watch(
  () => [props.open, props.entityKind, props.entityId],
  () => {
    loadSequence += 1;
    items.value = [];
    error.value = null;
    loading.value = false;
    if (props.open && props.entityId) void load();
  },
  { immediate: true },
);
</script>

<template>
  <div class="h-full min-h-0 flex flex-col bg-ngb-card">
    <div class="px-5 py-4 border-b border-ngb-border flex items-start gap-3">
      <div class="min-w-0 flex-1">
        <div class="text-base font-semibold text-ngb-text truncate">Audit Log</div>
        <div class="text-sm text-ngb-muted truncate mt-1">{{ title }}</div>
      </div>

      <button class="ngb-iconbtn shrink-0" title="Close" @click="emit('close')">
        <NgbIcon name="x" />
      </button>
    </div>

    <div class="flex-1 min-h-0 overflow-auto">
      <div v-if="error" class="px-5 py-4 text-sm text-red-700">{{ error }}</div>
      <div v-else-if="!canLoad" class="px-5 py-4 text-sm text-ngb-muted">Save the record first to see its history.</div>
      <div v-else-if="loading && items.length === 0" class="px-5 py-4 text-sm text-ngb-muted">Loading…</div>
      <div v-else-if="items.length === 0" class="px-5 py-4 text-sm text-ngb-muted">No history yet.</div>

      <div v-else class="py-3">
        <section v-for="item in items" :key="item.auditEventId" class="bg-ngb-card px-4 mb-4 last:mb-0">
          <div class="py-3 border-b border-ngb-border flex items-center justify-between gap-4">
            <div class="min-w-0 text-sm font-semibold text-ngb-text truncate">{{ eventSummary(item) }}</div>
            <div class="shrink-0 text-sm text-ngb-text whitespace-nowrap">{{ formatDateTime(item.occurredAtUtc) }}</div>
          </div>

          <div class="border border-ngb-border border-t-0">
            <table class="w-full text-sm table-fixed">
              <thead class="bg-ngb-bg text-ngb-muted text-xs">
                <tr>
                  <th class="w-[34%] px-4 py-2 text-left font-semibold border-r border-ngb-border">Field</th>
                  <th class="w-[33%] px-4 py-2 text-left font-semibold border-r border-ngb-border">Original Value</th>
                  <th class="w-[33%] px-4 py-2 text-left font-semibold">New Value</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in eventRows(item)" :key="`${item.auditEventId}:${row.field}`" class="border-t border-ngb-border">
                  <td class="px-4 py-3 align-top border-r border-ngb-border break-words text-ngb-text">{{ row.field }}</td>
                  <td class="px-4 py-3 align-top border-r border-ngb-border break-words text-ngb-text">{{ row.before }}</td>
                  <td class="px-4 py-3 align-top break-words text-ngb-text">{{ row.after }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>
