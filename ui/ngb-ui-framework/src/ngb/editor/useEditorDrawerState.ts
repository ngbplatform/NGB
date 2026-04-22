import { ref } from 'vue';

import type { EntityEditorFlags } from './types';

function createDefaultFlags(): EntityEditorFlags {
  return {
    canSave: false,
    isDirty: false,
    loading: false,
    saving: false,
    canExpand: false,
    canDelete: false,
    canMarkForDeletion: false,
    canUnmarkForDeletion: false,
    canPost: false,
    canUnpost: false,
    canShowAudit: false,
    canShareLink: false,
    extras: undefined,
  };
}

export function useEditorDrawerState() {
  const drawerTitle = ref('');
  const drawerSubtitle = ref<string | undefined>(undefined);
  const editorFlags = ref<EntityEditorFlags>(createDefaultFlags());
  const discardOpen = ref(false);
  let discardPromise: Promise<boolean> | null = null;
  let discardResolve: ((value: boolean) => void) | null = null;

  function resetDrawerHeading() {
    drawerTitle.value = '';
    drawerSubtitle.value = undefined;
  }

  function handleEditorFlags(next: EntityEditorFlags) {
    editorFlags.value = next;
  }

  function handleEditorState(next: { title: string; subtitle?: string }) {
    drawerTitle.value = next.title;
    drawerSubtitle.value = next.subtitle;
  }

  function requestDiscard(): Promise<boolean> {
    if (discardOpen.value && discardPromise) return discardPromise;

    discardOpen.value = true;
    discardPromise = new Promise<boolean>((resolve) => {
      discardResolve = resolve;
    });
    return discardPromise;
  }

  function discardConfirm() {
    discardOpen.value = false;
    discardResolve?.(true);
    discardResolve = null;
    discardPromise = null;
  }

  function discardCancel() {
    discardOpen.value = false;
    discardResolve?.(false);
    discardResolve = null;
    discardPromise = null;
  }

  async function beforeCloseDrawer(): Promise<boolean> {
    if (discardOpen.value) return false;
    if (!editorFlags.value.isDirty) return true;
    return await requestDiscard();
  }

  return {
    drawerTitle,
    drawerSubtitle,
    editorFlags,
    discardOpen,
    handleEditorFlags,
    handleEditorState,
    resetDrawerHeading,
    requestDiscard,
    discardConfirm,
    discardCancel,
    beforeCloseDrawer,
  };
}
