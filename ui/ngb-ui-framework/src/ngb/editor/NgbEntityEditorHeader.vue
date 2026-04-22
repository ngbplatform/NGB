<script setup lang="ts">
import NgbHeaderActionCluster from '../components/NgbHeaderActionCluster.vue';
import NgbBadge from '../primitives/NgbBadge.vue';
import NgbIcon from '../primitives/NgbIcon.vue';
import NgbPageHeader from '../site/NgbPageHeader.vue';

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
  canBack: boolean;
  title: string;
  subtitle?: string;
  documentStatusLabel: string;
  documentStatusTone: 'neutral' | 'success' | 'warn';
  loading: boolean;
  saving: boolean;
  pageActions?: EntityHeaderIconAction[];
  documentPrimaryActions: DocumentHeaderActionItem[];
  documentMoreActionGroups: DocumentHeaderActionGroup[];
}>(), {
  subtitle: undefined,
  pageActions: () => [],
});

const emit = defineEmits<{
  (e: 'back'): void;
  (e: 'close'): void;
  (e: 'action', action: string): void;
}>();
</script>

<template>
  <template v-if="kind === 'document'">
    <template v-if="mode === 'page'">
      <NgbPageHeader :title="title" :can-back="canBack" @back="emit('back')">
        <template #secondary>
          <div class="flex min-w-0 items-center">
            <NgbBadge :tone="documentStatusTone">{{ documentStatusLabel }}</NgbBadge>
          </div>
        </template>

        <template #actions>
          <NgbHeaderActionCluster
            :primary-actions="documentPrimaryActions"
            :more-groups="documentMoreActionGroups"
            :close-disabled="loading || saving"
            @action="(action) => emit('action', action)"
            @close="emit('close')"
          />
        </template>
      </NgbPageHeader>
    </template>

    <div v-else class="border-b border-ngb-border bg-ngb-card px-5 pt-4 pb-4">
      <div class="min-w-0 w-full">
        <div class="font-semibold leading-tight truncate whitespace-nowrap text-lg">
          {{ title }}
        </div>
      </div>

      <div class="mt-3 flex items-center justify-between gap-3">
        <div class="min-w-0 flex items-center gap-2">
          <NgbBadge :tone="documentStatusTone">{{ documentStatusLabel }}</NgbBadge>
        </div>

        <div class="flex shrink-0 items-center gap-2">
          <NgbHeaderActionCluster
            :primary-actions="documentPrimaryActions"
            :more-groups="documentMoreActionGroups"
            :close-disabled="loading || saving"
            @action="(action) => emit('action', action)"
            @close="emit('close')"
          />
        </div>
      </div>
    </div>
  </template>

  <NgbPageHeader
    v-else-if="mode === 'page'"
    :title="title"
    :can-back="canBack"
    @back="emit('back')"
  >
    <template #secondary>
      <div v-if="subtitle" class="text-xs text-ngb-muted truncate">{{ subtitle }}</div>
    </template>

    <template #actions>
      <button
        v-for="item in pageActions"
        :key="item.key"
        class="ngb-iconbtn"
        :disabled="item.disabled"
        :title="item.title"
        @click="emit('action', item.key)"
      >
        <NgbIcon :name="item.icon" />
      </button>

      <button class="ngb-iconbtn" :disabled="loading || saving" title="Close" @click="emit('close')">
        <NgbIcon name="x" />
      </button>
    </template>
  </NgbPageHeader>
</template>
