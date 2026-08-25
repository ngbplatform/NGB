import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, type PropType } from 'vue'

const state = vi.hoisted(() => ({
  query: null as unknown as { value: string },
  isSearching: null as unknown as { value: boolean },
  clearPendingState: vi.fn(),
  onInput: vi.fn(),
  resetQueryState: vi.fn(),
  updatePosition: vi.fn(),
}))

vi.mock('../../../../src/ngb/primitives/useAsyncComboboxQuery', async () => {
  const { ref } = await import('vue')
  state.query = ref('')
  state.isSearching = ref(false)
  return {
    useAsyncComboboxQuery: (options: { emitQuery: (value: string) => void }) => {
      state.onInput.mockImplementation((value: string) => {
        state.query.value = value
        options.emitQuery(value)
      })
      state.resetQueryState.mockImplementation((resetOptions?: { emitEmptyQuery?: boolean }) => {
        state.query.value = ''
        if (resetOptions?.emitEmptyQuery) options.emitQuery('')
      })
      return state
    },
  }
})

vi.mock('../../../../src/ngb/primitives/useFloatingDropdownPosition', async () => {
  const { ref } = await import('vue')
  return {
    useFloatingDropdownPosition: () => ({
      floatingStyle: ref({ left: '10px', top: '20px', width: '240px' }),
      updatePosition: state.updatePosition,
    }),
  }
})

vi.mock('@headlessui/vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    Combobox: defineComponent({
      props: { modelValue: { default: null }, disabled: Boolean },
      emits: ['update:modelValue'],
      setup(_, { emit, slots }) {
        return () => h('div', { 'data-testid': 'combobox-stub' }, [
          slots.default?.(),
          h('button', { type: 'button', onClick: () => emit('update:modelValue', null) }, 'Select null stub'),
        ])
      },
    }),
    ComboboxInput: defineComponent({
      inheritAttrs: false,
      props: {
        displayValue: { type: Function as PropType<(value: unknown) => string>, required: true },
        disabled: Boolean,
        readonly: Boolean,
      },
      setup(props, { attrs }) {
        props.displayValue(null)
        props.displayValue('invalid')
        props.displayValue({ id: 'valid-id' })
        props.displayValue({ id: 'valid-id', label: 'Valid label' })
        return () => h('input', { ...attrs, disabled: props.disabled, readonly: props.readonly, 'data-testid': 'lookup-input' })
      },
    }),
    ComboboxButton: defineComponent({
      setup(_, { attrs, slots }) {
        return () => h('button', { ...attrs, type: 'button' }, slots.default?.())
      },
    }),
    ComboboxOptions: defineComponent({
      setup(_, { attrs, slots }) {
        return () => h('div', attrs, slots.default?.())
      },
    }),
    ComboboxOption: defineComponent({
      props: { value: { type: Object, required: true } },
      setup(_, { slots }) {
        return () => h('div', [
          slots.default?.({ active: false, selected: false }),
          slots.default?.({ active: true, selected: true }),
        ])
      },
    }),
  }
})

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      props: { name: String, size: Number },
      setup: (props) => () => h('span', { 'data-icon-size': props.size }, props.name),
    }),
  }
})

import NgbLookup, { type LookupItem } from '../../../../src/ngb/primitives/NgbLookup.vue'

const items: LookupItem[] = [
  { id: 'empty', label: undefined as never },
  { id: 'meta-only', label: '', meta: 'Meta only' },
  { id: 'label-only', label: 'Label only' },
  { id: 'both', label: 'Both', meta: 'Both meta' },
]

const VariantsHarness = defineComponent({
  setup() {
    const variants = [
      { key: 'grid-1', variant: 'grid', modelValue: null },
      { key: 'grid-2', variant: 'grid', modelValue: items[1], showOpen: true },
      { key: 'grid-3', variant: 'grid', modelValue: items[2], showOpen: true, showClear: true },
      { key: 'compact-1', variant: 'compact', modelValue: null },
      { key: 'compact-2', variant: 'compact', modelValue: items[1], showOpen: true },
      { key: 'compact-3', variant: 'compact', modelValue: items[2], showOpen: true, showClear: true },
      { key: 'default-2', variant: 'default', modelValue: items[1], showClear: true, disabled: true },
    ] as const

    return () => h('div', variants.map((entry, index) => h(NgbLookup, {
      ...entry,
      items,
      label: index === 0 ? 'Lookup label' : '',
      hint: index === 0 ? 'Lookup hint' : '',
      onQuery: vi.fn(),
    })))
  },
})

beforeEach(() => {
  vi.clearAllMocks()
  state.query.value = ''
  state.isSearching.value = false
})

test('covers every visual variant, padding count, tooltip shape, and slot state', async () => {
  await page.viewport(1280, 900)
  const view = await render(VariantsHarness)

  await expect.element(view.getByText('Lookup label')).toBeVisible()
  await expect.element(view.getByText('Lookup hint')).toBeVisible()

  const inputClasses = Array.from(document.querySelectorAll('[data-testid="lookup-input"]'))
    .map((element) => element.getAttribute('class') ?? '')
    .join('|')
  for (const expected of ['pr-9', 'pr-14', 'pr-20', 'pr-8', 'h-8', 'h-[26px]', 'pr-16']) {
    expect(inputClasses).toContain(expected)
  }

  expect(document.body.textContent).toContain('Meta only')
  expect(document.body.textContent).toContain('Label only')
  expect(document.body.textContent).toContain('Both meta')
  expect(document.querySelector('[title="Meta only"]')).not.toBeNull()
  expect(document.querySelector('[title="Label only"]')).not.toBeNull()
  expect(document.querySelector('[title="Both - Both meta"]')).not.toBeNull()
  expect(document.querySelector('[data-icon-size="13"]')).not.toBeNull()
  expect(document.querySelector('[data-icon-size="14"]')).not.toBeNull()

  view.getByText('Select null stub').first().element()
    .dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(state.resetQueryState).toHaveBeenCalled()
})

test('covers filtered metadata fallback and the clear guard for an empty selection', async () => {
  await page.viewport(1280, 900)
  const view = await render(NgbLookup, {
    props: {
      modelValue: null,
      items,
      showClear: true,
      'onUpdate:modelValue': vi.fn(),
    },
  })

  state.query.value = 'label'
  await expect.element(view.getByText('Label only').first()).toBeVisible()
  const clear = view.getByRole('button', { name: 'Clear' }).element()
  clear.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  expect(state.resetQueryState).not.toHaveBeenCalled()
})
