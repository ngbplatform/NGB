import { computed, onBeforeUnmount, onMounted, ref, type ComponentPublicInstance, type Ref } from 'vue'

type FloatingHost = Element | ComponentPublicInstance | null
type FloatingHostRef = Ref<FloatingHost>

const DROPDOWN_GAP_PX = 8
const VIEWPORT_PADDING_PX = 8
const DEFAULT_DROPDOWN_HEIGHT_PX = 288

function resolveHostElement(host: FloatingHost | undefined): HTMLElement | null {
  const raw = host && '$el' in host ? host.$el : host
  return raw instanceof HTMLElement ? raw : null
}

export function useFloatingDropdownPosition(anchorRef: FloatingHostRef, overlayRefs: FloatingHostRef[]) {
  const floatingLeft = ref(0)
  const floatingTop = ref(0)
  const floatingWidth = ref(0)
  const floatingMaxHeight = ref(DEFAULT_DROPDOWN_HEIGHT_PX)
  let rafId: number | null = null

  const floatingStyle = computed(() => ({
    left: `${floatingLeft.value}px`,
    top: `${floatingTop.value}px`,
    width: `${floatingWidth.value}px`,
    maxHeight: `${floatingMaxHeight.value}px`,
  }))

  function resolveViewportBounds() {
    if (typeof window === 'undefined') {
      return {
        width: 0,
        height: 0,
      }
    }

    const viewport = window.visualViewport
    return {
      width: Math.round(viewport?.width ?? window.innerWidth ?? Number.MAX_SAFE_INTEGER),
      height: Math.round(viewport?.height ?? window.innerHeight ?? Number.MAX_SAFE_INTEGER),
    }
  }

  function resolveOverlayHeight() {
    const overlayEl = overlayRefs
      .map((overlayRef) => resolveHostElement(overlayRef.value))
      .find((element) => !!element)

    const renderedHeight = overlayEl?.offsetHeight ?? 0
    return renderedHeight > 0 ? renderedHeight : DEFAULT_DROPDOWN_HEIGHT_PX
  }

  function computePosition() {
    const anchorEl = resolveHostElement(anchorRef.value)
    if (!anchorEl) return

    const rect = anchorEl.getBoundingClientRect()
    const viewport = resolveViewportBounds()
    const overlayHeight = resolveOverlayHeight()
    const maxWidth = Math.max(0, viewport.width - VIEWPORT_PADDING_PX * 2)
    const width = Math.max(0, Math.min(Math.round(rect.width), maxWidth))
    const left = Math.min(
      Math.max(Math.round(rect.left), VIEWPORT_PADDING_PX),
      Math.max(VIEWPORT_PADDING_PX, viewport.width - VIEWPORT_PADDING_PX - width),
    )

    const availableBelow = Math.max(0, viewport.height - rect.bottom - DROPDOWN_GAP_PX - VIEWPORT_PADDING_PX)
    const availableAbove = Math.max(0, rect.top - DROPDOWN_GAP_PX - VIEWPORT_PADDING_PX)
    const shouldOpenAbove = availableAbove > availableBelow && availableBelow < overlayHeight
    const availableHeight = shouldOpenAbove ? availableAbove : availableBelow
    const maxHeight = Math.max(0, Math.min(DEFAULT_DROPDOWN_HEIGHT_PX, Math.round(availableHeight)))
    const renderedHeight = Math.min(overlayHeight, maxHeight || overlayHeight)
    const top = shouldOpenAbove
      ? Math.max(VIEWPORT_PADDING_PX, Math.round(rect.top - DROPDOWN_GAP_PX - renderedHeight))
      : Math.round(rect.bottom + DROPDOWN_GAP_PX)

    floatingLeft.value = left
    floatingTop.value = top
    floatingWidth.value = width
    floatingMaxHeight.value = maxHeight
  }

  function schedulePositionRecalc() {
    if (typeof window === 'undefined') return
    if (typeof window.requestAnimationFrame !== 'function') {
      computePosition()
      return
    }
    if (rafId != null && typeof window.cancelAnimationFrame === 'function') {
      window.cancelAnimationFrame(rafId)
    }
    rafId = window.requestAnimationFrame(() => {
      rafId = null
      computePosition()
    })
  }

  function updatePosition() {
    computePosition()
    schedulePositionRecalc()
  }

  function onWindowChange() {
    const hasVisibleOverlay = overlayRefs.some((overlayRef) => !!resolveHostElement(overlayRef.value))
    if (!hasVisibleOverlay) return
    computePosition()
  }

  onMounted(() => {
    if (typeof window === 'undefined') return
    window.addEventListener('resize', onWindowChange)
    window.addEventListener('scroll', onWindowChange, true)
  })

  onBeforeUnmount(() => {
    if (typeof window === 'undefined') return
    window.removeEventListener('resize', onWindowChange)
    window.removeEventListener('scroll', onWindowChange, true)
    if (rafId != null && typeof window.cancelAnimationFrame === 'function') {
      window.cancelAnimationFrame(rafId)
    }
    rafId = null
  })

  return {
    floatingStyle,
    updatePosition,
  }
}
