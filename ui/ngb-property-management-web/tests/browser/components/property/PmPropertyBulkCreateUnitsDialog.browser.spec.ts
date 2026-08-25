import { defineComponent, h } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  bulkCreate: vi.fn(),
  dryRun: vi.fn(),
  toastPush: vi.fn(),
}))

vi.mock('../../../../src/api/clients/pmCatalogs', () => ({
  bulkCreatePmPropertyUnits: mocks.bulkCreate,
  dryRunPmPropertyUnits: mocks.dryRun,
}))

vi.mock('@ngbplatform/ui', async () => {
  const Button = defineComponent({
    props: {
      disabled: { type: Boolean, default: false },
      loading: { type: Boolean, default: false },
    },
    emits: ['click'],
    setup(props, { emit, slots }) {
      return () => h('button', {
        type: 'button',
        disabled: props.disabled,
        'data-loading': String(props.loading),
        onClick: () => emit('click'),
      }, slots.default?.())
    },
  })
  const Dialog = defineComponent({
    props: {
      open: { type: Boolean, default: false },
      title: { type: String, default: '' },
      subtitle: { type: String, default: '' },
    },
    emits: ['update:open'],
    setup(props, { emit, slots }) {
      return () => props.open
        ? h('section', { 'data-testid': 'dialog' }, [
            h('h1', props.title),
            props.subtitle ? h('p', props.subtitle) : null,
            h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Dialog dismiss'),
            slots.default?.(),
            h('footer', slots.footer?.()),
          ])
        : null
    },
  })
  const Icon = defineComponent({
    props: { name: { type: String, required: true } },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  })
  const Input = defineComponent({
    props: {
      modelValue: { type: [String, Number], default: '' },
      label: { type: String, required: true },
      type: { type: String, default: 'text' },
    },
    emits: ['update:modelValue'],
    setup(props, { emit }) {
      return () => h('label', [
        h('span', props.label),
        h('input', {
          'aria-label': props.label,
          type: 'text',
          'data-input-type': props.type,
          value: String(props.modelValue ?? ''),
          onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
          onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
        }),
      ])
    },
  })

  return {
    NgbButton: Button,
    NgbDialog: Dialog,
    NgbIcon: Icon,
    NgbInput: Input,
    toErrorMessage: (cause: unknown, fallback: string) => cause instanceof Error ? cause.message : fallback,
    useToasts: () => ({ push: mocks.toastPush }),
  }
})

import PmPropertyBulkCreateUnitsDialog from '../../../../src/components/property/PmPropertyBulkCreateUnitsDialog.vue'

type BulkResponse = {
  buildingId: string
  requestedCount: number
  createdCount: number
  duplicateCount: number
  createdIds: string[]
  createdUnitNosSample: string[]
  duplicateUnitNosSample: string[]
  isDryRun?: boolean
  wouldCreateCount?: number
  previewUnitNosSample?: string[]
}

function response(overrides: Partial<BulkResponse> = {}): BulkResponse {
  return {
    buildingId: 'building-1',
    requestedCount: 10,
    createdCount: 8,
    duplicateCount: 2,
    createdIds: ['unit-1', 'unit-2'],
    createdUnitNosSample: ['0001', '0002'],
    duplicateUnitNosSample: ['0009', '0010'],
    ...overrides,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

async function flushDebounce() {
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 300))
}

async function openDialog(buildingDisplay: string | null = 'West Tower') {
  const view = await render(PmPropertyBulkCreateUnitsDialog, {
    props: {
      open: false,
      buildingId: 'building-1',
      buildingDisplay,
    },
  })
  await view.rerender({
    open: true,
    buildingId: 'building-1',
    buildingDisplay,
  })
  return view
}

function setInput(label: string, value: string) {
  const input = document.querySelector(`input[aria-label="${label}"]`)
  if (!(input instanceof HTMLInputElement)) throw new Error(`Input "${label}" not found.`)
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

beforeEach(() => {
  mocks.bulkCreate.mockReset()
  mocks.dryRun.mockReset()
  mocks.toastPush.mockReset()
  mocks.dryRun.mockResolvedValue(response({ isDryRun: true, wouldCreateCount: 8 }))
  mocks.bulkCreate.mockResolvedValue(response())
})

test('previews, confirms, creates, copies result samples, and closes through every public exit', async () => {
  const clipboardWrite = vi.fn(async () => undefined)
  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: { writeText: clipboardWrite },
  })
  const view = await openDialog()
  await flushDebounce()

  expect(mocks.dryRun).toHaveBeenCalledWith({
    buildingId: 'building-1',
    fromInclusive: 1,
    toInclusive: 10,
    step: 1,
    unitNoFormat: '{0:0000}',
    floorSize: null,
  })
  await expect.element(view.getByText('Would create:')).toBeVisible()
  await expect.element(view.getByText('0001', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Next' }).click()
  await expect.element(view.getByText('We will create missing unit records under')).toBeVisible()
  await view.getByRole('button', { name: 'Back' }).click()
  await expect.element(view.getByText('Preview')).toBeVisible()
  await view.getByRole('button', { name: 'Next' }).click()
  await view.getByRole('button', { name: 'Copy duplicates' }).click()
  expect(clipboardWrite).toHaveBeenCalledWith('0009\n0010')

  await view.getByRole('button', { name: 'Create' }).click()
  await expect.element(view.getByText('Result')).toBeVisible()
  expect(mocks.bulkCreate).toHaveBeenCalledOnce()
  expect(mocks.toastPush).toHaveBeenCalledWith(expect.objectContaining({ tone: 'success' }))

  await view.getByRole('button', { name: 'Copy' }).click()
  expect(clipboardWrite).toHaveBeenLastCalledWith('0009\n0010')
  await view.getByRole('button', { name: 'Done' }).click()
  await view.getByRole('button', { name: 'Close' }).click()
  await view.getByRole('button', { name: 'Dialog dismiss' }).click()
  expect(view.emitted('created')).toEqual([[response()]])
  expect(view.emitted('update:open')).toEqual([[false], [false], [false]])
})

test('reports every validation boundary and only schedules valid previews', async () => {
  const view = await openDialog(null)
  const cases: Array<[string, string, string]> = [
    ['From', '', 'Enter a whole number for From.'],
    ['From', '1.5', 'Enter a whole number for From.'],
    ['From', 'Infinity', 'Enter a whole number for From.'],
    ['To', '', 'Enter a whole number for To.'],
    ['Step', '', 'Enter a whole number for Step.'],
    ['Step', '0', 'Step must be greater than 0.'],
    ['From', '20', 'From must be less than or equal to To.'],
    ['To', '6001', 'You can create up to 5,000 units in one run.'],
    ['Floor size (optional)', '0', 'Floor size must be greater than 0.'],
    ['Unit no format', '', 'Unit number format is required.'],
    ['Unit no format', 'UNIT', 'Unit number format must include {0}.'],
  ]

  for (const [label, value, message] of cases) {
    setInput('From', '1')
    setInput('To', '10')
    setInput('Step', '1')
    setInput('Floor size (optional)', '')
    setInput('Unit no format', '{0}')
    setInput(label, value)
    await expect.element(view.getByText(message)).toBeVisible()
  }

  setInput('From', '-2')
  setInput('To', '2')
  setInput('Step', '2')
  setInput('Floor size (optional)', '2')
  setInput('Unit no format', '{1:00}-{0:000}')
  await flushDebounce()
  await expect.element(view.getByText('01--002', { exact: true })).toBeVisible()
  await expect.element(view.getByText('02-002', { exact: true })).toBeVisible()
  expect(mocks.dryRun).toHaveBeenLastCalledWith(expect.objectContaining({
    fromInclusive: -2,
    toInclusive: 2,
    step: 2,
    floorSize: 2,
    unitNoFormat: '{1:00}-{0:000}',
  }))
})

test('shows dry-run failures, ignores stale completions, and accepts the latest preview', async () => {
  const stale = deferred<BulkResponse>()
  mocks.dryRun
    .mockImplementationOnce(async () => await stale.promise)
    .mockRejectedValueOnce(new Error('Latest preview failed'))
    .mockResolvedValueOnce(response({
      wouldCreateCount: 1,
      duplicateCount: 0,
      previewUnitNosSample: ['SERVER-ONLY'],
    }))

  const view = await openDialog()
  await flushDebounce()
  setInput('To', '2')
  await flushDebounce()
  await expect.element(view.getByText('Latest preview failed')).toBeVisible()

  stale.resolve(response({ previewUnitNosSample: ['STALE'] }))
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 20))
  expect(document.body.textContent ?? '').not.toContain('STALE')

  setInput('To', '3')
  await flushDebounce()
  await expect.element(view.getByText('SERVER-ONLY')).toBeVisible()
  expect(document.body.textContent ?? '').not.toContain('Latest preview failed')
})

test('finishes without a write when every requested unit is already a duplicate', async () => {
  mocks.dryRun.mockResolvedValueOnce(response({
    requestedCount: 10,
    createdCount: 0,
    duplicateCount: 10,
    wouldCreateCount: undefined,
    previewUnitNosSample: [],
    createdIds: [],
    createdUnitNosSample: [],
    duplicateUnitNosSample: [],
  }))
  const view = await openDialog(null)
  await flushDebounce()
  await view.getByRole('button', { name: 'Next' }).click()
  await expect.element(view.getByText('building-1')).toBeVisible()
  await expect.element(view.getByText('Nothing to create')).toBeVisible()
  await view.getByRole('button', { name: 'Finish' }).click()

  await expect.element(view.getByText('Result')).toBeVisible()
  expect(mocks.bulkCreate).not.toHaveBeenCalled()
  await expect.element(view.getByText('Created:')).toBeVisible()
})

test('surfaces create failures, then retries successfully without losing the confirmation', async () => {
  mocks.bulkCreate
    .mockRejectedValueOnce(new Error('Create units failed'))
    .mockResolvedValueOnce(response({ createdCount: 10, duplicateCount: 0 }))
  const view = await openDialog()
  await flushDebounce()
  await view.getByRole('button', { name: 'Next' }).click()
  await view.getByRole('button', { name: 'Create' }).click()

  await expect.element(view.getByText('Create units failed')).toBeVisible()
  expect(mocks.toastPush).toHaveBeenCalledWith(expect.objectContaining({ tone: 'danger' }))
  await view.getByRole('button', { name: 'Create' }).click()
  await expect.element(view.getByText('Result')).toBeVisible()
  expect(mocks.bulkCreate).toHaveBeenCalledTimes(2)
})

test('cancels pending debounce work and invalidates in-flight previews on close and unmount', async () => {
  const pending = deferred<BulkResponse>()
  mocks.dryRun.mockImplementationOnce(async () => await pending.promise)
  const view = await openDialog()
  setInput('To', '11')
  setInput('To', '12')
  await flushDebounce()
  expect(mocks.dryRun).toHaveBeenCalledOnce()

  await view.unmount()
  pending.resolve(response({ previewUnitNosSample: ['AFTER-UNMOUNT'] }))
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 20))
  expect(document.body.textContent ?? '').not.toContain('AFTER-UNMOUNT')
})

test('cancels a scheduled preview when the dialog closes and starts fresh when reopened', async () => {
  const view = await openDialog()
  setInput('To', '25')
  await view.rerender({
    open: false,
    buildingId: 'building-1',
    buildingDisplay: 'West Tower',
  })
  await flushDebounce()
  expect(mocks.dryRun).not.toHaveBeenCalled()

  await view.rerender({
    open: true,
    buildingId: 'building-1',
    buildingDisplay: 'West Tower',
  })
  await flushDebounce()
  expect(mocks.dryRun).toHaveBeenCalledOnce()
  expect(mocks.dryRun).toHaveBeenCalledWith(expect.objectContaining({ toInclusive: 10 }))
})

test('keeps confirmation safe while its refreshed preview is loading or failed', async () => {
  const pending = deferred<BulkResponse>()
  mocks.dryRun
    .mockResolvedValueOnce(response({ duplicateUnitNosSample: [] }))
    .mockImplementationOnce(async () => await pending.promise)

  const view = await openDialog()
  await flushDebounce()
  await view.getByRole('button', { name: 'Next' }).click()
  await flushDebounce()
  await expect.element(view.getByText('Checking existing units…')).toBeVisible()
  expect(document.querySelector('button[title="Copy duplicates sample"]')).toBeNull()

  const createButton = view.getByRole('button', { name: 'Create' }).element() as HTMLButtonElement
  createButton.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.bulkCreate).not.toHaveBeenCalled()

  pending.reject(new Error('Confirmation preview failed'))
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 20))
  await expect.element(view.getByText('Confirmation preview failed')).toBeVisible()
  createButton.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  expect(mocks.bulkCreate).not.toHaveBeenCalled()
})

test('ignores stale dry-run rejections after a newer preview succeeds', async () => {
  const stale = deferred<BulkResponse>()
  mocks.dryRun
    .mockImplementationOnce(async () => await stale.promise)
    .mockResolvedValueOnce(response({ previewUnitNosSample: ['LATEST'] }))

  const view = await openDialog()
  await flushDebounce()
  setInput('To', '4')
  await flushDebounce()
  await expect.element(view.getByText('LATEST')).toBeVisible()

  stale.reject(new Error('Stale preview rejection'))
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 20))
  expect(document.body.textContent ?? '').not.toContain('Stale preview rejection')
})
