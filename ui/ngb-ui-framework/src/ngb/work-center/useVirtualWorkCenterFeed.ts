import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  watch,
  type Ref,
} from 'vue'

export type VirtualWorkCenterFeedEntry<T> = {
  item: T
  key: string
}

const VIRTUALIZATION_THRESHOLD = 80
const OVERSCAN_PX = 500

export function useVirtualWorkCenterFeed<T>(args: {
  items: Readonly<Ref<readonly T[]>>
  scrollHost: Ref<HTMLElement | null>
  listElement: Ref<HTMLElement | null>
  getKey: (item: T) => string
  estimatedItemHeight: number
}) {
  const viewportHeight = ref(0)
  const localScrollTop = ref(0)
  const measurementVersion = ref(0)
  const heights = new Map<string, number>()
  const elementsByKey = new Map<string, HTMLElement>()
  let scrollFrame: number | null = null

  const resizeObserver = typeof ResizeObserver === 'undefined'
    ? null
    : new ResizeObserver((entries) => {
      let changed = false
      for (const entry of entries) {
        const element = entry.target as HTMLElement
        const key = element.dataset.workCenterVirtualKey
        if (!key) continue
        const height = entry.borderBoxSize?.[0]?.blockSize ?? entry.contentRect.height
        if (!(height > 0) || heights.get(key) === height) continue
        heights.set(key, height)
        changed = true
      }
      if (changed) measurementVersion.value += 1
    })

  const viewportObserver = typeof ResizeObserver === 'undefined'
    ? null
    : new ResizeObserver(() => updateViewport())

  const layout = computed(() => {
    measurementVersion.value
    const offsets = new Array<number>(args.items.value.length + 1)
    offsets[0] = 0
    for (let index = 0; index < args.items.value.length; index += 1) {
      const item = args.items.value[index]!
      offsets[index + 1] = offsets[index]! + (heights.get(args.getKey(item)) ?? args.estimatedItemHeight)
    }
    return offsets
  })

  function firstItemEndingAfter(offsets: number[], position: number): number {
    let low = 0
    let high = offsets.length - 1
    while (low < high) {
      const middle = Math.floor((low + high) / 2)
      if (offsets[middle + 1]! <= position) low = middle + 1
      else high = middle
    }
    return Math.min(low, offsets.length - 2)
  }

  const virtualWindow = computed(() => {
    const items = args.items.value
    const offsets = layout.value
    const totalHeight = offsets[offsets.length - 1] ?? 0
    if (items.length <= VIRTUALIZATION_THRESHOLD) {
      return {
        entries: items.map((item) => ({ item, key: args.getKey(item) })),
        top: 0,
        bottom: 0,
      }
    }

    const startPosition = Math.max(0, localScrollTop.value - OVERSCAN_PX)
    const endPosition = localScrollTop.value + viewportHeight.value + OVERSCAN_PX
    const start = firstItemEndingAfter(offsets, startPosition)
    let end = start
    while (end < items.length && offsets[end]! < endPosition) end += 1

    return {
      entries: items.slice(start, end).map((item) => ({ item, key: args.getKey(item) })),
      top: offsets[start] ?? 0,
      bottom: Math.max(0, totalHeight - (offsets[end] ?? totalHeight)),
    }
  })

  function updateViewport(): void {
    const host = args.scrollHost.value
    const list = args.listElement.value
    if (!host || !list) return
    const hostRect = host.getBoundingClientRect()
    const listRect = list.getBoundingClientRect()
    const visibleViewportHeight = typeof globalThis.window === 'undefined'
      ? host.clientHeight
      : Math.max(0, Math.min(globalThis.window.innerHeight, hostRect.bottom) - Math.max(0, hostRect.top))
    viewportHeight.value = Math.min(host.clientHeight, visibleViewportHeight)
    localScrollTop.value = Math.max(0, hostRect.top - listRect.top)
  }

  function onScroll(): void {
    if (scrollFrame !== null) return
    scrollFrame = requestAnimationFrame(() => {
      scrollFrame = null
      updateViewport()
    })
  }

  function setItemElement(key: string, value: unknown): void {
    const previous = elementsByKey.get(key)
    if (previous) {
      resizeObserver?.unobserve(previous)
      elementsByKey.delete(key)
    }

    if (!(value instanceof HTMLElement)) return
    value.dataset.workCenterVirtualKey = key
    elementsByKey.set(key, value)
    resizeObserver?.observe(value)
  }

  onMounted(() => {
    args.scrollHost.value?.addEventListener('scroll', onScroll, { passive: true })
    if (args.scrollHost.value) viewportObserver?.observe(args.scrollHost.value)
    void nextTick(updateViewport)
  })

  watch(args.items, async () => {
    const liveKeys = new Set(args.items.value.map(args.getKey))
    for (const key of heights.keys()) {
      if (!liveKeys.has(key)) heights.delete(key)
    }
    for (const [key, element] of elementsByKey) {
      if (liveKeys.has(key)) continue
      resizeObserver?.unobserve(element)
      elementsByKey.delete(key)
    }
    measurementVersion.value += 1
    await nextTick()
    updateViewport()
  })

  onBeforeUnmount(() => {
    args.scrollHost.value?.removeEventListener('scroll', onScroll)
    if (scrollFrame !== null) cancelAnimationFrame(scrollFrame)
    resizeObserver?.disconnect()
    viewportObserver?.disconnect()
    elementsByKey.clear()
  })

  return {
    virtualEntries: computed(() => virtualWindow.value.entries),
    topSpacerHeight: computed(() => virtualWindow.value.top),
    bottomSpacerHeight: computed(() => virtualWindow.value.bottom),
    setItemElement,
  }
}
