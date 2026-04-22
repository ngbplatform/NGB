import { ref, type ComputedRef, type Ref } from 'vue';
import type { Router } from 'vue-router';

const DRAWER_CLOSE_SENTINEL = '__ngb_drawer_close__';

export type UseEntityEditorLeaveGuardArgs = {
  isDirty: ComputedRef<boolean>;
  loading: Ref<boolean>;
  saving: Ref<boolean>;
  router: Router;
  onClose: () => void;
};

export function useEntityEditorLeaveGuard(args: UseEntityEditorLeaveGuardArgs) {
  const leaveOpen = ref(false);
  const leaveTo = ref<string | null>(null);

  function requestNavigate(to: string | null | undefined) {
    if (!to) return;

    if (args.isDirty.value && !args.loading.value && !args.saving.value) {
      leaveTo.value = to;
      leaveOpen.value = true;
      return;
    }

    void args.router.push(to);
  }

  function requestClose() {
    if (args.isDirty.value && !args.loading.value && !args.saving.value) {
      leaveTo.value = DRAWER_CLOSE_SENTINEL;
      leaveOpen.value = true;
      return;
    }

    args.onClose();
  }

  function confirmLeave() {
    const target = leaveTo.value;
    leaveTo.value = null;
    leaveOpen.value = false;

    if (!target) return;
    if (target === DRAWER_CLOSE_SENTINEL) {
      args.onClose();
      return;
    }

    void args.router.push(target);
  }

  function cancelLeave() {
    leaveTo.value = null;
    leaveOpen.value = false;
  }

  return {
    leaveOpen,
    requestNavigate,
    requestClose,
    confirmLeave,
    cancelLeave,
  };
}
