import { onBeforeUnmount, type ComputedRef, type Ref } from 'vue';
import type { RegisterColumn } from './registerTypes';

type UseRegisterColumnResizeArgs = {
  columns: ComputedRef<RegisterColumn[]>;
  localWidths: Ref<Record<string, number>>;
  colWidth: (column: RegisterColumn) => number;
};

type ResizeSession = {
  key: string;
  pointerId: number;
  startX: number;
  startWidth: number;
  minWidth: number;
  handle: HTMLElement | null;
  controller: AbortController;
};

export function useRegisterColumnResize(args: UseRegisterColumnResizeArgs) {
  let activeSession: ResizeSession | null = null;

  function setPointerCaptureSafely(handle: HTMLElement | null, pointerId: number) {
    if (!handle?.setPointerCapture) return;

    try {
      handle.setPointerCapture(pointerId);
    } catch {
      // Firefox can reject synthetic pointer ids in tests; resizing still works without capture.
    }
  }

  function releasePointerCaptureSafely(handle: HTMLElement | null, pointerId: number) {
    if (!handle?.releasePointerCapture) return;

    try {
      handle.releasePointerCapture(pointerId);
    } catch {
      // Ignore invalid-pointer errors during teardown and fall back to listener cleanup.
    }
  }

  function stopResize() {
    if (!activeSession) return;
    activeSession.controller.abort();
    releasePointerCaptureSafely(activeSession.handle, activeSession.pointerId);
    activeSession = null;
  }

  function startResize(key: string, event: PointerEvent) {
    const column = args.columns.value.find((entry) => entry.key === key);
    if (!column) return;

    stopResize();

    const controller = new AbortController();
    const handle = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const session: ResizeSession = {
      key,
      pointerId: event.pointerId,
      startX: event.clientX,
      startWidth: args.colWidth(column),
      minWidth: column.minWidth ?? 80,
      handle,
      controller,
    };

    activeSession = session;
    setPointerCaptureSafely(handle, event.pointerId);

    const onMove = (nextEvent: PointerEvent) => {
      if (!activeSession || nextEvent.pointerId !== session.pointerId) return;
      const delta = nextEvent.clientX - session.startX;
      const nextWidth = Math.max(session.minWidth, session.startWidth + delta);
      args.localWidths.value = {
        ...args.localWidths.value,
        [session.key]: nextWidth,
      };
    };

    const onFinish = () => {
      stopResize();
    };

    const target: HTMLElement | Window = handle ?? window;
    target.addEventListener('pointermove', onMove, { signal: controller.signal });
    target.addEventListener('pointerup', onFinish, { signal: controller.signal });
    target.addEventListener('pointercancel', onFinish, { signal: controller.signal });
  }

  onBeforeUnmount(() => {
    stopResize();
  });

  return {
    startResize,
    stopResize,
  };
}
