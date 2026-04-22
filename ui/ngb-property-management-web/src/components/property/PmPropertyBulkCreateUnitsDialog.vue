<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { NgbButton, NgbDialog, NgbIcon, NgbInput, useToasts } from 'ngb-ui-framework'

import {
  bulkCreatePmPropertyUnits,
  dryRunPmPropertyUnits,
  type PmPropertyBulkCreateUnitsRequest,
  type PmPropertyBulkCreateUnitsResponse,
} from '../../api/clients/pmCatalogs'
import { toErrorMessage } from 'ngb-ui-framework'

const props = withDefaults(
  defineProps<{
    open: boolean
    buildingId: string
    buildingDisplay?: string | null
  }>(),
  {
    buildingDisplay: null,
  },
)

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void
  (e: 'created', value: PmPropertyBulkCreateUnitsResponse): void
}>()

const toasts = useToasts()

type Step = 'pattern' | 'confirm' | 'result'
const step = ref<Step>('pattern')

const loading = ref(false)
const error = ref<string | null>(null)
const result = ref<PmPropertyBulkCreateUnitsResponse | null>(null)

const dryRunLoading = ref(false)
const dryRunError = ref<string | null>(null)
const dryRun = ref<PmPropertyBulkCreateUnitsResponse | null>(null)

const fromInclusive = ref('1')
const toInclusive = ref('10')
const stepValue = ref('1')
const unitNoFormat = ref('{0:0000}')
const floorSize = ref('')

let dryRunTimer: ReturnType<typeof window.setTimeout> | null = null
let dryRunSeq = 0

function cancelScheduledDryRun() {
  if (dryRunTimer != null) {
    clearTimeout(dryRunTimer)
    dryRunTimer = null
  }
}

onBeforeUnmount(() => {
  cancelScheduledDryRun()
  dryRunSeq++
})

function resetState() {
  step.value = 'pattern'
  loading.value = false
  error.value = null
  result.value = null

  dryRunLoading.value = false
  dryRunError.value = null
  dryRun.value = null

  fromInclusive.value = '1'
  toInclusive.value = '10'
  stepValue.value = '1'
  unitNoFormat.value = '{0:0000}'
  floorSize.value = ''
}

watch(
  () => props.open,
  (v) => {
    if (!v) return
    resetState()
    // schedule the first preview after state is reset
    scheduleDryRun()
  },
)

function parseIntStrict(raw: string): number | null {
  const s = String(raw ?? '').trim()
  if (!s) return null
  const n = Number(s)
  if (!Number.isFinite(n)) return null
  if (Math.floor(n) !== n) return null
  return n
}

const parsed = computed(() => {
  const from = parseIntStrict(fromInclusive.value)
  const to = parseIntStrict(toInclusive.value)
  const st = parseIntStrict(stepValue.value)
  const fl = floorSize.value.trim() ? parseIntStrict(floorSize.value) : null
  return { from, to, step: st, floorSize: fl }
})

const requestedCount = computed(() => {
  const { from, to, step } = parsed.value
  if (from == null || to == null || step == null) return 0
  if (step <= 0) return 0
  if (from > to) return 0
  return Math.floor((to - from) / step) + 1
})

function padZeros(n: number, width: number): string {
  const s = String(Math.abs(n))
  const padded = s.padStart(width, '0')
  return n < 0 ? `-${padded}` : padded
}

function formatPlaceholder(token: string, value: number): string {
  // Supports "{0}" and "{0:0000}" or "{1:00}".
  // If the format is unknown, falls back to String(value).
  const m = token.match(/^\{\s*(\d+)\s*(?::\s*([0]+)\s*)?\}$/)
  if (!m) return token

  const zeros = m[2]
  if (!zeros) return String(value)
  return padZeros(value, zeros.length)
}

function formatUnitNo(fmt: string, n: number, floor: number | null): string {
  // Replace occurrences of {0...} and {1...}
  let out = fmt
  out = out.replace(/\{\s*0\s*(?::\s*[0]+\s*)?\}/g, (t) => formatPlaceholder(t, n))
  if (floor != null) {
    out = out.replace(/\{\s*1\s*(?::\s*[0]+\s*)?\}/g, (t) => formatPlaceholder(t, floor))
  } else {
    out = out.replace(/\{\s*1\s*(?::\s*[0]+\s*)?\}/g, '')
  }
  return out
}

const previewUnitNosLocal = computed(() => {
  const { from, to, step, floorSize } = parsed.value
  if (from == null || to == null || step == null) return []
  if (step <= 0 || from > to) return []
  const fmt = unitNoFormat.value || '{0}'

  const preview: string[] = []
  const max = Math.min(requestedCount.value, 10)
  for (let i = 0; i < max; i++) {
    const num = from + i * step
    const floor = floorSize && floorSize > 0 ? Math.floor(i / floorSize) + 1 : null
    preview.push(formatUnitNo(fmt, num, floor))
  }
  return preview
})

const validationError = computed(() => {
  const { from, to, step, floorSize } = parsed.value
  if (from == null) return 'Enter a whole number for From.'
  if (to == null) return 'Enter a whole number for To.'
  if (step == null) return 'Enter a whole number for Step.'
  if (step <= 0) return 'Step must be greater than 0.'
  if (from > to) return 'From must be less than or equal to To.'
  if (requestedCount.value <= 0) return 'Check the unit range.'
  if (requestedCount.value > 5000) return 'You can create up to 5,000 units in one run.'
  if (floorSize != null && floorSize <= 0) return 'Floor size must be greater than 0.'

  const fmt = String(unitNoFormat.value ?? '').trim()
  if (!fmt) return 'Unit number format is required.'
  if (!fmt.includes('{0')) return 'Unit number format must include {0}.'

  return null
})

const requestedCountText = computed(() => requestedCount.value.toLocaleString())

const duplicateCount = computed(() => dryRun.value?.duplicateCount ?? 0)
const wouldCreateCount = computed(() => {
  const v = dryRun.value?.wouldCreateCount
  if (typeof v === 'number') return v
  return Math.max(0, requestedCount.value - duplicateCount.value)
})

const previewUnitNosEffective = computed(() => {
  const sample = dryRun.value?.previewUnitNosSample
  if (Array.isArray(sample) && sample.length > 0) return sample.slice(0, 10)
  return previewUnitNosLocal.value
})

function buildRequest(): PmPropertyBulkCreateUnitsRequest | null {
  if (validationError.value) return null

  const { from, to, step: st, floorSize: fl } = parsed.value
  if (from == null || to == null || st == null) return null

  return {
    buildingId: props.buildingId,
    fromInclusive: from,
    toInclusive: to,
    step: st,
    unitNoFormat: String(unitNoFormat.value ?? '').trim() || '{0}',
    floorSize: fl,
  }
}

function scheduleDryRun() {
  cancelScheduledDryRun()

  dryRunError.value = null

  if (!props.open) return
  if (step.value === 'result') return

  const req = buildRequest()
  if (!req) {
    dryRun.value = null
    dryRunLoading.value = false
    return
  }

  dryRunTimer = window.setTimeout(() => {
    void runDryRun(req)
  }, 250)
}

async function runDryRun(req: PmPropertyBulkCreateUnitsRequest) {
  const mySeq = ++dryRunSeq
  dryRunLoading.value = true
  dryRunError.value = null

  try {
    const r = await dryRunPmPropertyUnits(req)
    if (mySeq !== dryRunSeq) return
    dryRun.value = r
  } catch (cause) {
    if (mySeq !== dryRunSeq) return
    dryRun.value = null
    dryRunError.value = toErrorMessage(cause, 'Failed to preview unit creation.')
  } finally {
    if (mySeq !== dryRunSeq) return
    dryRunLoading.value = false
  }
}

watch([fromInclusive, toInclusive, stepValue, unitNoFormat, floorSize], () => scheduleDryRun())
watch(step, () => scheduleDryRun())

function close() {
  emit('update:open', false)
}

function goNext() {
  if (validationError.value) return
  step.value = 'confirm'
  scheduleDryRun()
}

function goBack() {
  if (step.value === 'confirm') step.value = 'pattern'
}

async function create() {
  if (validationError.value) return
  if (dryRunLoading.value) return
  if (!dryRun.value) return
  if (wouldCreateCount.value <= 0) {
    // Nothing to create; keep it frictionless.
    step.value = 'result'
    result.value = {
      buildingId: props.buildingId,
      requestedCount: requestedCount.value,
      createdCount: 0,
      duplicateCount: duplicateCount.value,
      createdIds: [],
      createdUnitNosSample: [],
      duplicateUnitNosSample: dryRun.value?.duplicateUnitNosSample ?? [],
    }
    return
  }

  const req = buildRequest()
  if (!req) return

  loading.value = true
  error.value = null

  try {
    const r = await bulkCreatePmPropertyUnits(req)
    result.value = r
    emit('created', r)
    step.value = 'result'

    toasts.push({
      title: 'Bulk create units',
      message: `Created ${r.createdCount.toLocaleString()} (duplicates ${r.duplicateCount.toLocaleString()}).`,
      tone: 'success',
    })
  } catch (cause) {
    error.value = toErrorMessage(cause, 'Failed to create units.')
    toasts.push({ title: 'Bulk create failed', message: error.value, tone: 'danger' })
  } finally {
    loading.value = false
  }
}

function copyLines(lines: string[]) {
  const text = lines.join('\n')
  void navigator.clipboard?.writeText(text)
  toasts.push({ title: 'Copied', message: `${lines.length} item(s) copied to clipboard.`, tone: 'neutral' })
}
</script>

<template>
  <NgbDialog
    :open="open"
    title="Bulk create units"
    :subtitle="buildingDisplay ? `Building: ${buildingDisplay}` : undefined"
    :confirm-loading="loading"
    @update:open="(v) => emit('update:open', v)"
  >
    <div class="space-y-4">
      <div v-if="step === 'pattern'" class="space-y-4">
        <div class="grid grid-cols-3 gap-3">
          <NgbInput v-model="fromInclusive" label="From" type="number" />
          <NgbInput v-model="toInclusive" label="To" type="number" />
          <NgbInput v-model="stepValue" label="Step" type="number" />
        </div>

        <NgbInput
          v-model="unitNoFormat"
          label="Unit no format"
          placeholder="{0:0000}"
          hint="Supports {0} = number, {1} = floor (optional). Examples: {0}, {0:0000}, {1}-{0:000}"
        />

        <NgbInput
          v-model="floorSize"
          label="Floor size (optional)"
          type="number"
          hint="Example: 100 ⇒ first 100 units = floor 1, next 100 = floor 2."
        />

        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-3">
          <div class="text-sm font-semibold text-ngb-text">Preview</div>

          <div class="text-sm text-ngb-muted mt-1">
            Requested:
            <span class="font-semibold text-ngb-text">{{ requestedCountText }}</span>

            <template v-if="dryRunLoading">
              · <span class="text-ngb-muted">Checking existing…</span>
            </template>
            <template v-else-if="dryRun">
              · Would create:
              <span class="font-semibold text-ngb-text">{{ wouldCreateCount.toLocaleString() }}</span>
              · Duplicates:
              <span class="font-semibold text-ngb-text">{{ duplicateCount.toLocaleString() }}</span>
            </template>
          </div>

          <div class="mt-2 text-sm text-ngb-muted">Sample unit numbers:</div>
          <div class="mt-2 flex flex-wrap gap-2">
            <span
              v-for="(x, i) in previewUnitNosEffective"
              :key="i"
              class="px-2 py-1 rounded border border-ngb-border text-xs text-ngb-text bg-ngb-card"
              >{{ x }}</span
            >
          </div>

          <div v-if="dryRunError" class="mt-2 text-sm text-ngb-danger">{{ dryRunError }}</div>
        </div>

        <div v-if="validationError" class="text-sm text-ngb-danger flex items-start gap-2">
          <NgbIcon name="help-circle" :size="16" />
          <div>{{ validationError }}</div>
        </div>
      </div>

      <div v-else-if="step === 'confirm'" class="space-y-3">
        <div class="text-sm text-ngb-text">
          We will create missing unit records under <span class="font-semibold">{{ buildingDisplay ?? buildingId }}</span>.
        </div>

        <div class="text-sm text-ngb-muted">
          Requested:
          <span class="font-semibold text-ngb-text">{{ requestedCountText }}</span>
          <template v-if="dryRun">
            · Would create:
            <span class="font-semibold text-ngb-text">{{ wouldCreateCount.toLocaleString() }}</span>
            · Duplicates:
            <span class="font-semibold text-ngb-text">{{ duplicateCount.toLocaleString() }}</span>
          </template>
        </div>

        <div v-if="dryRunLoading" class="text-sm text-ngb-muted">Checking existing units…</div>

        <div v-else-if="dryRun && wouldCreateCount <= 0" class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-3">
          <div class="text-sm font-semibold text-ngb-text">Nothing to create</div>
          <div class="text-sm text-ngb-muted mt-1">All requested unit numbers already exist (operation is idempotent).</div>
        </div>

        <div class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-3">
          <div class="flex items-center justify-between">
            <div class="text-sm font-semibold text-ngb-text">Unit numbers preview</div>
            <button
              v-if="dryRun?.duplicateUnitNosSample?.length"
              class="text-xs text-ngb-link hover:underline"
              @click="copyLines(dryRun.duplicateUnitNosSample ?? [])"
              title="Copy duplicates sample"
            >
              Copy duplicates
            </button>
          </div>

          <div class="mt-2 flex flex-wrap gap-2">
            <span
              v-for="(x, i) in previewUnitNosEffective"
              :key="i"
              class="px-2 py-1 rounded border border-ngb-border text-xs text-ngb-text bg-ngb-card"
              >{{ x }}</span
            >
          </div>
        </div>

        <div v-if="dryRunError" class="text-sm text-ngb-danger">{{ dryRunError }}</div>
        <div v-if="error" class="text-sm text-ngb-danger">{{ error }}</div>
      </div>

      <div v-else class="space-y-4">
        <div v-if="result" class="rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card p-3">
          <div class="text-sm font-semibold text-ngb-text">Result</div>
          <div class="text-sm text-ngb-muted mt-1">
            Created:
            <span class="font-semibold text-ngb-text">{{ result.createdCount.toLocaleString() }}</span>
            · Duplicates:
            <span class="font-semibold text-ngb-text">{{ result.duplicateCount.toLocaleString() }}</span>
            · Requested:
            <span class="font-semibold text-ngb-text">{{ result.requestedCount.toLocaleString() }}</span>
          </div>

          <div class="mt-3" v-if="result.createdUnitNosSample?.length">
            <div class="text-xs font-semibold text-ngb-muted">Created (sample)</div>
            <div class="mt-2 flex flex-wrap gap-2">
              <span
                v-for="(x, i) in result.createdUnitNosSample"
                :key="`c-${i}`"
                class="px-2 py-1 rounded border border-ngb-border text-xs text-ngb-text bg-ngb-card"
                >{{ x }}</span
              >
            </div>
          </div>

          <div class="mt-3" v-if="result.duplicateUnitNosSample?.length">
            <div class="flex items-center justify-between">
              <div class="text-xs font-semibold text-ngb-muted">Duplicates (sample)</div>
              <button
                class="text-xs text-ngb-link hover:underline"
                @click="copyLines(result.duplicateUnitNosSample ?? [])"
              >
                Copy
              </button>
            </div>
            <div class="mt-2 flex flex-wrap gap-2">
              <span
                v-for="(x, i) in result.duplicateUnitNosSample"
                :key="`d-${i}`"
                class="px-2 py-1 rounded border border-ngb-border text-xs text-ngb-text bg-ngb-card"
                >{{ x }}</span
              >
            </div>
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <div class="flex items-center justify-between gap-2">
        <div />
        <div class="flex items-center gap-2">
          <NgbButton variant="secondary" :disabled="loading" @click="close">{{ step === 'result' ? 'Close' : 'Cancel' }}</NgbButton>

          <NgbButton
            v-if="step === 'pattern'"
            :disabled="!!validationError || dryRunLoading || !dryRun || loading"
            @click="goNext"
          >
            Next
          </NgbButton>

          <template v-else-if="step === 'confirm'">
            <NgbButton variant="secondary" :disabled="loading" @click="goBack">Back</NgbButton>
            <NgbButton
              :loading="loading"
              :disabled="loading || dryRunLoading || !dryRun"
              @click="create"
            >
              {{ wouldCreateCount <= 0 ? 'Finish' : 'Create' }}
            </NgbButton>
          </template>

          <NgbButton v-else variant="primary" :disabled="loading" @click="close">Done</NgbButton>
        </div>
      </div>
    </template>
  </NgbDialog>
</template>
