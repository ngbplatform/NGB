<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'

const props = defineProps<{ chart: string }>()

const inlineViewportRef = ref<HTMLDivElement | null>(null)
const inlineCanvasRef = ref<HTMLDivElement | null>(null)
const dialogViewportRef = ref<HTMLDivElement | null>(null)
const dialogCanvasRef = ref<HTMLDivElement | null>(null)

const rendered = ref('')
const error = ref('')
const isFullscreen = ref(false)
const viewportHeightPx = ref<number | null>(null)

let mermaidModule: any = null
let resizeObserver: ResizeObserver | null = null
let bindFunctionsRef: ((element: Element) => void) | undefined

const state = reactive({
  scale: 1,
  baseScale: 1,
  minScale: 0.05,
  maxScale: 24,
  x: 0,
  y: 0,
  isDragging: false,
  pointerId: null as number | null,
  startX: 0,
  startY: 0,
  originX: 0,
  originY: 0
})

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max)
}

function getActiveViewport() {
  return isFullscreen.value ? dialogViewportRef.value : inlineViewportRef.value
}

function getActiveCanvas() {
  return isFullscreen.value ? dialogCanvasRef.value : inlineCanvasRef.value
}

function getTransformTarget() {
  return getActiveCanvas()
}

async function ensureMermaid() {
  if (mermaidModule || import.meta.env.SSR) {
    return
  }

  mermaidModule = await import('mermaid')
  mermaidModule.default.initialize({
    startOnLoad: false,
    securityLevel: 'loose',
    theme: 'default',
    flowchart: {
      useMaxWidth: false,
      htmlLabels: false,
      curve: 'linear'
    }
  })
}

function getSvgElement() {
  return getActiveCanvas()?.querySelector('svg') ?? null
}

function getSvgSize(svg: SVGSVGElement) {
  const viewBox = svg.viewBox?.baseVal
  if (viewBox?.width && viewBox?.height) {
    return { width: viewBox.width, height: viewBox.height }
  }

  const width = Number.parseFloat(svg.getAttribute('width') ?? '')
  const height = Number.parseFloat(svg.getAttribute('height') ?? '')
  if (Number.isFinite(width) && Number.isFinite(height) && width > 0 && height > 0) {
    return { width, height }
  }

  const box = svg.getBBox()
  return {
    width: Math.max(box.width, 1),
    height: Math.max(box.height, 1)
  }
}

function prepareSvg(svg: SVGSVGElement) {
  const canvas = getActiveCanvas()
  const { width, height } = getSvgSize(svg)

  svg.setAttribute('width', String(width))
  svg.setAttribute('height', String(height))
  svg.setAttribute('preserveAspectRatio', 'xMinYMin meet')
  svg.style.display = 'block'
  svg.style.width = `${width}px`
  svg.style.height = `${height}px`
  svg.style.maxWidth = 'none'
  svg.style.maxHeight = 'none'
  svg.style.overflow = 'visible'
  svg.style.textRendering = 'geometricPrecision'
  svg.style.shapeRendering = 'geometricPrecision'
  svg.style.userSelect = 'none'
  svg.style.touchAction = 'none'

  if (canvas) {
    canvas.style.width = `${width}px`
    canvas.style.height = `${height}px`
    canvas.style.transformOrigin = '0 0'
  }
}

function applyTransform() {
  const target = getTransformTarget()
  if (!target) {
    return
  }

  target.style.transform = `translate(${state.x}px, ${state.y}px) scale(${state.scale})`
}

function fitToViewport(options?: { preserveHeight?: boolean }) {
  const viewport = getActiveViewport()
  const svg = getSvgElement()
  if (!viewport || !svg) {
    return
  }

  const { width: svgWidth, height: svgHeight } = getSvgSize(svg)
  if (!svgWidth || !svgHeight) {
    return
  }

  const rect = viewport.getBoundingClientRect()
  if (!rect.width) {
    return
  }

  const padding = isFullscreen.value ? 20 : 12
  const controlReserveX = 44
  const controlReserveY = 44
  const availableWidth = Math.max(rect.width - padding * 2 - controlReserveX, 1)

  if (isFullscreen.value) {
    const availableHeight = Math.max(rect.height - padding * 2 - controlReserveY, 1)
    const nextScale = Math.min(availableWidth / svgWidth, availableHeight / svgHeight)

    state.baseScale = nextScale
    state.minScale = Math.min(nextScale, 0.05)
    state.maxScale = Math.max(nextScale * 24, 24)
    state.scale = nextScale
    state.x = Math.max((rect.width - svgWidth * nextScale) / 2, padding)
    state.y = Math.max((rect.height - svgHeight * nextScale) / 2, padding)
    applyTransform()
    return
  }

  const nextScale = availableWidth / svgWidth
  const contentHeight = svgHeight * nextScale + padding * 2
  if (!options?.preserveHeight) {
    viewportHeightPx.value = Math.max(Math.round(contentHeight), 240)
  }

  state.baseScale = nextScale
  state.minScale = Math.min(nextScale, 0.05)
  state.maxScale = Math.max(nextScale * 24, 24)
  state.scale = nextScale
  state.x = padding
  state.y = padding
  applyTransform()
}

function zoomAt(factor: number, clientX?: number, clientY?: number) {
  const viewport = getActiveViewport()
  const svg = getSvgElement()
  if (!viewport || !svg) {
    return
  }

  const rect = viewport.getBoundingClientRect()
  const previousScale = state.scale
  const nextScale = clamp(previousScale * factor, state.minScale, state.maxScale)
  if (nextScale === previousScale) {
    return
  }

  const anchorX = clientX == null ? rect.width / 2 : clientX - rect.left
  const anchorY = clientY == null ? rect.height / 2 : clientY - rect.top
  const worldX = (anchorX - state.x) / previousScale
  const worldY = (anchorY - state.y) / previousScale

  state.scale = nextScale
  state.x = anchorX - worldX * nextScale
  state.y = anchorY - worldY * nextScale
  applyTransform()
}

function zoomIn() {
  zoomAt(1.25)
}

function zoomOut() {
  zoomAt(1 / 1.25)
}

function panBy(dx: number, dy: number) {
  state.x += dx
  state.y += dy
  applyTransform()
}

function refitDiagram() {
  fitToViewport({ preserveHeight: false })
}

function refitFullscreen() {
  fitToViewport({ preserveHeight: true })
}

function onWheel(event: WheelEvent) {
  event.preventDefault()
  const factor = event.deltaY < 0 ? 1.12 : 1 / 1.12
  zoomAt(factor, event.clientX, event.clientY)
}

function onPointerDown(event: PointerEvent) {
  const target = event.target as HTMLElement | null
  if (target?.closest('.mermaid-controls')) {
    return
  }

  if (event.button !== 0) {
    return
  }

  const viewport = getActiveViewport()
  if (!viewport) {
    return
  }

  state.isDragging = true
  state.pointerId = event.pointerId
  state.startX = event.clientX
  state.startY = event.clientY
  state.originX = state.x
  state.originY = state.y

  viewport.setPointerCapture(event.pointerId)
}

function onPointerMove(event: PointerEvent) {
  if (!state.isDragging || event.pointerId !== state.pointerId) {
    return
  }

  state.x = state.originX + (event.clientX - state.startX)
  state.y = state.originY + (event.clientY - state.startY)
  applyTransform()
}

function stopDragging(event?: PointerEvent) {
  if (!state.isDragging) {
    return
  }

  const viewport = getActiveViewport()
  if (viewport && event && state.pointerId != null) {
    try {
      viewport.releasePointerCapture(state.pointerId)
    } catch {
      // no-op
    }
  }

  state.isDragging = false
  state.pointerId = null
}

function bindCurrentCanvas() {
  const canvas = getActiveCanvas()
  const svg = getSvgElement()
  if (!canvas || !svg) {
    return
  }

  prepareSvg(svg)
  bindFunctionsRef?.(canvas)
  requestAnimationFrame(() => fitToViewport({ preserveHeight: false }))
}

async function renderChart() {
  if (import.meta.env.SSR) {
    return
  }

  if (!props.chart?.trim()) {
    rendered.value = ''
    error.value = ''
    bindFunctionsRef = undefined
    return
  }

  try {
    error.value = ''
    await ensureMermaid()

    const id = `mermaid-${Math.random().toString(36).slice(2)}`
    const { svg, bindFunctions } = await mermaidModule.default.render(id, props.chart.trim())

    bindFunctionsRef = bindFunctions
    rendered.value = svg
    await nextTick()
    bindCurrentCanvas()
  } catch (err) {
    rendered.value = ''
    bindFunctionsRef = undefined
    error.value = err instanceof Error ? err.message : 'Failed to render Mermaid diagram.'
  }
}

function reconnectResizeObserver() {
  resizeObserver?.disconnect()
  const activeViewport = getActiveViewport()
  if (activeViewport) {
    resizeObserver?.observe(activeViewport)
  }
}

function onWindowKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && isFullscreen.value) {
    closeFullscreen()
  }
}

async function toggleFullscreen() {
  if (isFullscreen.value) {
    await closeFullscreen()
    return
  }

  isFullscreen.value = true
  document.body.style.overflow = 'hidden'
  await nextTick()
  bindCurrentCanvas()
  refitFullscreen()
}

async function closeFullscreen() {
  isFullscreen.value = false
  document.body.style.overflow = ''
  await nextTick()
  bindCurrentCanvas()
  refitDiagram()
}

onMounted(() => {
  renderChart()
  window.addEventListener('keydown', onWindowKeydown)

  if (!import.meta.env.SSR && typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => {
      if (rendered.value) {
        isFullscreen.value ? refitFullscreen() : refitDiagram()
      }
    })

    reconnectResizeObserver()
  }
})

watch(() => props.chart, renderChart)
watch(isFullscreen, async () => {
  await nextTick()
  reconnectResizeObserver()
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  document.body.style.overflow = ''
  window.removeEventListener('keydown', onWindowKeydown)
})
</script>

<template>
  <div class="mermaid-diagram">
    <div class="mermaid-stage">
      <div
        v-if="!isFullscreen"
        ref="inlineViewportRef"
        class="mermaid-viewport"
        :class="{ 'is-dragging': state.isDragging }"
        :style="viewportHeightPx ? { height: `${viewportHeightPx}px` } : undefined"
        @pointerdown="onPointerDown"
        @pointermove="onPointerMove"
        @pointerup="stopDragging"
        @pointercancel="stopDragging"
        @wheel="onWheel"
      >
        <div ref="inlineCanvasRef" class="mermaid-canvas" v-html="rendered" />
        <pre v-if="error" class="mermaid-error">{{ error }}</pre>

        <div
          v-if="rendered"
          class="mermaid-controls"
          aria-label="Diagram controls"
          @pointerdown.stop
          @pointerup.stop
          @click.stop
          @wheel.stop
        >
          <button class="control-button" type="button" aria-label="Pan up" @click="panBy(0, -120)">↑</button>
          <button class="control-button control-button--icon" type="button" aria-label="Open fullscreen" @click="toggleFullscreen">⤢</button>
          <button class="control-button" type="button" aria-label="Pan left" @click="panBy(-120, 0)">←</button>
          <button class="control-button control-button--accent" type="button" aria-label="Refresh fit" @click="refitDiagram">⟳</button>
          <button class="control-button" type="button" aria-label="Pan right" @click="panBy(120, 0)">→</button>
          <button class="control-button" type="button" aria-label="Zoom in" @click="zoomIn">+</button>
          <button class="control-button" type="button" aria-label="Pan down" @click="panBy(0, 120)">↓</button>
          <button class="control-button" type="button" aria-label="Zoom out" @click="zoomOut">−</button>
        </div>
      </div>
    </div>

    <Teleport to="body">
      <div v-if="isFullscreen" class="mermaid-dialog" @click.self="closeFullscreen">
        <div class="mermaid-dialog__surface">
          <div
            ref="dialogViewportRef"
            class="mermaid-viewport mermaid-viewport--fullscreen"
            :class="{ 'is-dragging': state.isDragging }"
            @pointerdown="onPointerDown"
            @pointermove="onPointerMove"
            @pointerup="stopDragging"
            @pointercancel="stopDragging"
            @wheel="onWheel"
          >
            <div ref="dialogCanvasRef" class="mermaid-canvas" v-html="rendered" />
            <div
              class="mermaid-controls"
              aria-label="Diagram controls"
              @pointerdown.stop
              @pointerup.stop
              @click.stop
              @wheel.stop
            >
              <button class="control-button" type="button" aria-label="Pan up" @click="panBy(0, -120)">↑</button>
              <button class="control-button control-button--icon" type="button" aria-label="Exit fullscreen" @click="toggleFullscreen">⇲</button>
              <button class="control-button" type="button" aria-label="Pan left" @click="panBy(-120, 0)">←</button>
              <button class="control-button control-button--accent" type="button" aria-label="Refresh fit" @click="refitFullscreen">⟳</button>
              <button class="control-button" type="button" aria-label="Pan right" @click="panBy(120, 0)">→</button>
              <button class="control-button" type="button" aria-label="Zoom in" @click="zoomIn">+</button>
              <button class="control-button" type="button" aria-label="Pan down" @click="panBy(0, 120)">↓</button>
              <button class="control-button" type="button" aria-label="Zoom out" @click="zoomOut">−</button>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.mermaid-diagram {
  position: relative;
  margin: 1rem 0;
}

.mermaid-stage {
  position: relative;
}

.mermaid-viewport {
  position: relative;
  min-height: 16rem;
  overflow: hidden;
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  background: var(--vp-c-bg-soft);
  cursor: grab;
}

.mermaid-viewport.is-dragging {
  cursor: grabbing;
}

.mermaid-viewport--fullscreen {
  min-height: 0;
  width: min(96vw, 1600px);
  height: min(92vh, 1100px);
  border-radius: 18px;
}

.mermaid-canvas {
  position: absolute;
  top: 0;
  left: 0;
  transform-origin: 0 0;
  will-change: transform;
}

.mermaid-canvas :deep(svg) {
  position: absolute;
  top: 0;
  left: 0;
}

.mermaid-controls {
  position: absolute;
  right: 6px;
  bottom: 6px;
  display: grid;
  grid-template-columns: repeat(3, 1.45rem);
  grid-template-areas:
    ". up full"
    "left fit right"
    "plus down minus";
  gap: 0.12rem;
  z-index: 3;
}

.mermaid-controls > :nth-child(1) { grid-area: up; }
.mermaid-controls > :nth-child(2) { grid-area: full; }
.mermaid-controls > :nth-child(3) { grid-area: left; }
.mermaid-controls > :nth-child(4) { grid-area: fit; }
.mermaid-controls > :nth-child(5) { grid-area: right; }
.mermaid-controls > :nth-child(6) { grid-area: plus; }
.mermaid-controls > :nth-child(7) { grid-area: down; }
.mermaid-controls > :nth-child(8) { grid-area: minus; }

.control-button {
  width: 1.45rem;
  height: 1.45rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background: color-mix(in srgb, var(--vp-c-bg) 96%, transparent);
  box-shadow: var(--vp-shadow-1);
  color: var(--vp-c-text-1);
  font-size: 0.74rem;
  line-height: 1;
  cursor: pointer;
}

.control-button:hover {
  background: var(--vp-c-bg-mute);
}

.control-button--accent {
  color: var(--vp-c-brand-1);
}

.control-button--icon {
  font-size: 0.66rem;
}

.mermaid-dialog {
  position: fixed;
  inset: 0;
  z-index: 999;
  display: grid;
  place-items: center;
  background: rgba(15, 23, 42, 0.58);
  backdrop-filter: blur(4px);
}

.mermaid-dialog__surface {
  position: relative;
}

.mermaid-error {
  position: absolute;
  inset: 0;
  margin: 0;
  padding: 1rem;
  overflow: auto;
  color: #b42318;
  background: #fff5f5;
  white-space: pre-wrap;
}
</style>
