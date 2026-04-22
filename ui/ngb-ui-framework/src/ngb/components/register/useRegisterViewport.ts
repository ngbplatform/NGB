import { computed, onBeforeUnmount, onMounted, ref, type ComputedRef, type Ref } from 'vue';
import type { DisplayRow } from './registerTypes';

type UseRegisterViewportArgs = {
  viewport: Ref<HTMLElement | null>;
  heightPx: ComputedRef<number>;
  rowHeight: ComputedRef<number>;
  displayRows: ComputedRef<DisplayRow[]>;
  overscan?: number;
};

export function useRegisterViewport(args: UseRegisterViewportArgs) {
  const overscan = args.overscan ?? 8;
  const scrollTop = ref(0);
  const viewportHeight = ref(args.heightPx.value);

  let resizeObserver: ResizeObserver | null = null;

  onMounted(() => {
    viewportHeight.value = args.viewport.value?.clientHeight ?? args.heightPx.value;

    if (typeof ResizeObserver !== 'undefined' && args.viewport.value) {
      resizeObserver = new ResizeObserver(() => {
        viewportHeight.value = args.viewport.value?.clientHeight ?? args.heightPx.value;
      });
      resizeObserver.observe(args.viewport.value);
    }
  });

  onBeforeUnmount(() => {
    if (resizeObserver) {
      resizeObserver.disconnect();
      resizeObserver = null;
    }
  });

  function onScroll() {
    scrollTop.value = args.viewport.value?.scrollTop ?? 0;
  }

  const totalHeight = computed(() => args.displayRows.value.length * args.rowHeight.value);
  const startIndex = computed(() => Math.max(0, Math.floor(scrollTop.value / args.rowHeight.value) - overscan));
  const endIndex = computed(() => {
    const visibleCount = Math.ceil(viewportHeight.value / args.rowHeight.value) + overscan * 2;
    return Math.min(args.displayRows.value.length, startIndex.value + visibleCount);
  });
  const offsetTop = computed(() => startIndex.value * args.rowHeight.value);
  const visibleRows = computed(() => args.displayRows.value.slice(startIndex.value, endIndex.value));

  return {
    onScroll,
    totalHeight,
    offsetTop,
    visibleRows,
  };
}
