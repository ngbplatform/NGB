import { inject, provide, reactive } from 'vue';

export type ToastTone = 'neutral' | 'success' | 'warn' | 'danger';

export type Toast = {
  id: string;
  title: string;
  message?: string;
  tone?: ToastTone;
  timeoutMs?: number;
};

export type ToastApi = {
  toasts: Toast[];
  push: (t: Omit<Toast, 'id'>) => void;
  remove: (id: string) => void;
};

const KEY = Symbol('ngb-toasts');

export function provideToasts(): ToastApi {
  const state = reactive({
    toasts: [] as Toast[],
  });

  function remove(id: string) {
    state.toasts = state.toasts.filter(t => t.id !== id);
  }

  function push(t: Omit<Toast, 'id'>) {
    const id = crypto?.randomUUID?.() ?? String(Date.now() + Math.random());
    const toast: Toast = {
      id,
      tone: 'neutral',
      timeoutMs: 3500,
      ...t,
    };
    state.toasts = [toast, ...state.toasts].slice(0, 5);

    const timeout = toast.timeoutMs ?? 0;
    if (timeout > 0) {
      window.setTimeout(() => remove(id), timeout);
    }
  }

  const api: ToastApi = {
    get toasts() {
      return state.toasts;
    },
    push,
    remove,
  };

  provide(KEY, api);
  return api;
}

export function useToasts(): ToastApi {
  const api = inject<ToastApi>(KEY);
  if (!api) throw new Error('useToasts(): missing provideToasts()');
  return api;
}
