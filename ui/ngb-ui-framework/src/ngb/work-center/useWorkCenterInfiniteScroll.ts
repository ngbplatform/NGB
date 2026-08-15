import { onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue'

type InfiniteScrollOptions = {
  nextCursor: Readonly<Ref<string | null>>
  loading: Readonly<Ref<boolean>>
  loadingMore: Readonly<Ref<boolean>>
  loadMoreError: Readonly<Ref<string | null>>
  loadMore: () => Promise<void>
}

export function useWorkCenterInfiniteScroll(options: InfiniteScrollOptions) {
  const sentinel = ref<HTMLElement | null>(null)
  let observer: IntersectionObserver | null = null
  let isVisible = false

  function requestNextPage(): void {
    if (
      !isVisible
      || !options.nextCursor.value
      || options.loading.value
      || options.loadingMore.value
      || options.loadMoreError.value
    ) return

    void options.loadMore().catch(() => undefined)
  }

  watch(
    [options.nextCursor, options.loading, options.loadingMore, options.loadMoreError],
    requestNextPage,
    { flush: 'post' },
  )

  watch(
    sentinel,
    (next, previous) => {
      if (!observer) return
      if (previous) observer.unobserve(previous)
      if (next) observer.observe(next)
    },
    { flush: 'post' },
  )

  onMounted(() => {
    if (typeof IntersectionObserver === 'undefined') return

    observer = new IntersectionObserver(
      (entries) => {
        isVisible = entries.some((entry) => entry.isIntersecting)
        requestNextPage()
      },
      { rootMargin: '320px 0px' },
    )

    if (sentinel.value) observer.observe(sentinel.value)
  })

  onBeforeUnmount(() => {
    observer?.disconnect()
    observer = null
  })

  return { sentinel }
}
