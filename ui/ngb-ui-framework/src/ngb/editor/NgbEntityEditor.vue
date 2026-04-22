<script setup lang="ts">
import { computed, ref } from 'vue';

import NgbConfirmDialog from '../components/NgbConfirmDialog.vue';
import NgbDrawer from '../components/NgbDrawer.vue';
import NgbEntityForm from '../metadata/NgbEntityForm.vue';
import type { DocumentStatusValue, EntityFormModel, FormMetadata } from '../metadata/types';
import type { EditorErrorIssue, EditorErrorState } from './entityEditorErrors';
import type { EntityEditorRenderExtension } from './extensions';
import NgbEntityAuditSidebar from './NgbEntityAuditSidebar.vue';
import NgbEditorDiscardDialog from './NgbEditorDiscardDialog.vue';
import NgbEntityEditorHeader from './NgbEntityEditorHeader.vue';
import type {
  DocumentHeaderActionGroup,
  DocumentHeaderActionItem,
  EditorKind,
  EditorMode,
  EntityHeaderIconAction,
} from './types';

const props = withDefaults(defineProps<{
  kind: EditorKind;
  mode: EditorMode;
  canBack?: boolean;
  title: string;
  subtitle?: string;
  documentStatusLabel?: string;
  documentStatusTone?: 'neutral' | 'success' | 'warn';
  loading: boolean;
  saving: boolean;
  pageActions?: EntityHeaderIconAction[];
  documentPrimaryActions?: DocumentHeaderActionItem[];
  documentMoreActionGroups?: DocumentHeaderActionGroup[];
  isNew: boolean;
  isMarkedForDeletion: boolean;
  displayedError?: EditorErrorState | null;
  bannerIssues?: EditorErrorIssue[];
  form?: FormMetadata | null;
  model: EntityFormModel;
  entityTypeCode: string;
  status?: DocumentStatusValue;
  isReadOnly?: boolean;
  errors?: Record<string, string | string[] | null> | null;
  afterFormExtensions?: EntityEditorRenderExtension[];
  dialogExtensions?: EntityEditorRenderExtension[];
  auditOpen?: boolean;
  auditEntityKind?: number;
  auditEntityId?: string | null;
  auditEntityTitle?: string;
  leaveOpen?: boolean;
  markConfirmOpen?: boolean;
  markConfirmMessage?: string;
}>(), {
  canBack: true,
  subtitle: undefined,
  documentStatusLabel: 'Draft',
  documentStatusTone: 'neutral',
  pageActions: () => [],
  documentPrimaryActions: () => [],
  documentMoreActionGroups: () => [],
  displayedError: null,
  bannerIssues: () => [],
  form: null,
  status: undefined,
  isReadOnly: false,
  errors: null,
  afterFormExtensions: () => [],
  dialogExtensions: () => [],
  auditOpen: false,
  auditEntityKind: 0,
  auditEntityId: null,
  auditEntityTitle: '',
  leaveOpen: false,
  markConfirmOpen: false,
  markConfirmMessage: '',
});

const emit = defineEmits<{
  (e: 'back'): void;
  (e: 'close'): void;
  (e: 'action', action: string): void;
  (e: 'closeAuditLog'): void;
  (e: 'cancelLeave'): void;
  (e: 'confirmLeave'): void;
  (e: 'cancelMarkForDeletion'): void;
  (e: 'confirmMarkForDeletion'): void;
}>();

const formRef = ref<InstanceType<typeof NgbEntityForm> | null>(null);

const visibleBannerIssues = computed(() => {
  const summary = normalizeBannerText(props.displayedError?.summary);

  return props.bannerIssues.filter((issue) => {
    if (issue.path !== '_form') return true;
    return normalizeBannerText(issue.messages.join(' ')) !== summary;
  });
});

function focusField(path: string): boolean {
  return !!formRef.value?.focusField?.(path);
}

function focusFirstError(keys: string[]): boolean {
  return !!formRef.value?.focusFirstError?.(keys);
}

defineExpose({
  focusField,
  focusFirstError,
});

function normalizeBannerText(value: string | null | undefined): string {
  return String(value ?? '')
    .trim()
    .replace(/\s+/g, ' ')
    .toLowerCase();
}
</script>

<template>
  <div :class="mode === 'page' ? 'flex h-full min-h-0 flex-col' : 'flex flex-col'">
    <NgbEntityEditorHeader
      :kind="kind"
      :mode="mode"
      :can-back="canBack"
      :title="title"
      :subtitle="subtitle"
      :document-status-label="documentStatusLabel"
      :document-status-tone="documentStatusTone"
      :loading="loading"
      :saving="saving"
      :page-actions="pageActions"
      :document-primary-actions="documentPrimaryActions"
      :document-more-action-groups="documentMoreActionGroups"
      @back="emit('back')"
      @close="emit('close')"
      @action="(action) => emit('action', action)"
    />

    <div :class="mode === 'page' ? 'min-h-0 flex-1 overflow-auto p-6' : 'px-5 py-4'">
      <div
        v-if="!isNew && isMarkedForDeletion"
        class="mb-4 flex items-start justify-between gap-3 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 dark:border-red-900/50 dark:bg-red-950/30"
      >
        <div class="min-w-0">
          <div class="text-sm font-semibold text-ngb-danger">Deleted</div>
          <div class="mt-1 text-sm text-ngb-muted">
            This {{ kind === 'catalog' ? 'record' : 'document' }} is marked for deletion. Restore it to edit or post.
          </div>
        </div>
      </div>

      <div
        v-if="displayedError"
        class="mb-4 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900/50 dark:bg-red-950/30 dark:text-red-100"
      >
        <div class="font-medium leading-6">{{ displayedError.summary }}</div>
        <ul v-if="visibleBannerIssues.length" class="mt-2 list-disc space-y-1 pl-5">
          <li v-for="issue in visibleBannerIssues" :key="`${issue.scope}:${issue.path}:${issue.messages.join('|')}`">
            <template v-if="issue.path === '_form'">
              <span class="font-medium">{{ issue.messages.join(' ') }}</span>
            </template>
            <template v-else>
              <span class="font-medium">{{ issue.label }}:</span>
              <span> {{ issue.messages.join(' ') }}</span>
            </template>
          </li>
        </ul>
      </div>

      <div v-if="loading" class="text-sm text-ngb-muted">Loading…</div>

      <div v-else class="space-y-4">
        <template v-if="form">
          <NgbEntityForm
            ref="formRef"
            :form="form"
            :model="model"
            :entity-type-code="entityTypeCode"
            :status="kind === 'document' ? status : undefined"
            :force-readonly="isReadOnly"
            :presentation="mode === 'drawer' ? 'flat' : 'sections'"
            :errors="errors"
          />

          <component
            :is="extension.component"
            v-for="extension in afterFormExtensions"
            :key="extension.key"
            :ref="extension.componentRef ?? undefined"
            v-bind="extension.props"
          />

          <slot name="after-form" />
        </template>

        <div v-else class="text-sm text-ngb-muted">No form metadata available.</div>
      </div>
    </div>

    <component
      :is="extension.component"
      v-for="extension in dialogExtensions"
      :key="extension.key"
      :ref="extension.componentRef ?? undefined"
      v-bind="extension.props"
    />

    <slot name="dialogs" />

    <NgbDrawer
      :open="auditOpen"
      title="Audit Log"
      hide-header
      flush-body
      @update:open="(value) => (!value ? emit('closeAuditLog') : null)"
    >
      <NgbEntityAuditSidebar
        :open="auditOpen"
        :entity-kind="auditEntityKind"
        :entity-id="auditEntityId"
        :entity-title="auditEntityTitle"
        @back="emit('closeAuditLog')"
        @close="emit('closeAuditLog')"
      />
    </NgbDrawer>

    <NgbEditorDiscardDialog
      :open="leaveOpen"
      title="Discard changes?"
      message="You have unsaved changes. If you leave now, they won’t be saved."
      confirm-text="Leave"
      cancel-text="Stay"
      @cancel="emit('cancelLeave')"
      @confirm="emit('confirmLeave')"
    />

    <NgbConfirmDialog
      :open="markConfirmOpen"
      title="Mark for deletion?"
      :message="markConfirmMessage"
      confirm-text="Mark"
      cancel-text="Cancel"
      danger
      @update:open="(value) => (!value ? emit('cancelMarkForDeletion') : null)"
      @confirm="emit('confirmMarkForDeletion')"
    />
  </div>
</template>
