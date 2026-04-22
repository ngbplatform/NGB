<script setup lang="ts">
import { computed } from 'vue';

import NgbIcon from '../primitives/NgbIcon.vue';

import type { EntityEditorFlags, EntityHeaderIconAction } from './types';

const props = withDefaults(defineProps<{
  flags: EntityEditorFlags;
  extraActions?: EntityHeaderIconAction[];
  restoreTitle?: string;
  markForDeletionTitle?: string;
}>(), {
  extraActions: () => [],
  restoreTitle: 'Unmark for deletion',
  markForDeletionTitle: 'Mark for deletion',
});

const emit = defineEmits<{
  (e: 'action', action: string): void;
}>();

const actionDisabled = computed(() => props.flags.loading || props.flags.saving);
const markTitle = computed(() => props.flags.canUnmarkForDeletion ? props.restoreTitle : props.markForDeletionTitle);
const markIcon = computed(() => props.flags.canUnmarkForDeletion ? 'trash-restore' : 'trash');
</script>

<template>
  <button
    v-if="flags.canExpand"
    class="ngb-iconbtn"
    :disabled="actionDisabled"
    title="Open full page"
    @click="emit('action', 'expand')"
  >
    <NgbIcon name="open-in-new" />
  </button>

  <button
    v-if="flags.canShareLink"
    class="ngb-iconbtn"
    :disabled="actionDisabled"
    title="Share link"
    @click="emit('action', 'share')"
  >
    <NgbIcon name="share" />
  </button>

  <button
    v-if="flags.canShowAudit"
    class="ngb-iconbtn"
    :disabled="actionDisabled"
    title="Audit log"
    @click="emit('action', 'audit')"
  >
    <NgbIcon name="history" />
  </button>

  <button
    v-if="flags.canMarkForDeletion || flags.canUnmarkForDeletion"
    class="ngb-iconbtn"
    :disabled="actionDisabled"
    :title="markTitle"
    @click="emit('action', 'mark')"
  >
    <NgbIcon :name="markIcon" />
  </button>

  <button
    v-for="item in extraActions"
    :key="item.key"
    class="ngb-iconbtn"
    :disabled="item.disabled"
    :title="item.title"
    @click="emit('action', item.key)"
  >
    <NgbIcon :name="item.icon" />
  </button>

  <button
    class="ngb-iconbtn"
    :disabled="actionDisabled || !flags.canSave"
    title="Save"
    @click="emit('action', 'save')"
  >
    <NgbIcon name="save" />
  </button>
</template>
