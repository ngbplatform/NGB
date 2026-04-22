import { defineComponent, h, ref, type PropType } from 'vue'

type LookupItem = {
  id: string
  label: string
  meta?: string | null
}

type VariantOption = {
  value: string
  label: string
}

type ReportSheetDto = {
  rows?: unknown[]
}

type ReportContext = {
  request?: {
    variantCode?: string | null
  }
}

export const StubPageHeader = defineComponent({
  props: {
    title: {
      type: String,
      default: '',
    },
    canBack: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['back'],
  setup(props, { emit, slots }) {
    return () => h('header', { 'data-testid': 'page-header' }, [
      props.canBack
        ? h('button', { type: 'button', onClick: () => emit('back') }, 'Header back')
        : null,
      h('h1', props.title),
      h('div', { 'data-testid': 'page-header-secondary' }, slots.secondary?.()),
      h('div', { 'data-testid': 'page-header-actions' }, slots.actions?.()),
    ])
  },
})

export const StubBadge = defineComponent({
  props: {
    tone: {
      type: String,
      default: 'neutral',
    },
  },
  setup(props, { slots }) {
    return () => h('span', { 'data-testid': `badge-${props.tone}` }, slots.default?.())
  },
})

export const StubIcon = defineComponent({
  props: {
    name: {
      type: String,
      default: '',
    },
  },
  setup(props) {
    return () => h('span', { 'data-testid': `icon-${props.name}` }, `icon:${props.name}`)
  },
})

export const StubInput = defineComponent({
  props: {
    modelValue: {
      type: [String, Number],
      default: '',
    },
    placeholder: {
      type: String,
      default: '',
    },
    type: {
      type: String,
      default: 'text',
    },
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('input', {
      type: props.type,
      value: String(props.modelValue ?? ''),
      placeholder: props.placeholder,
      disabled: props.disabled,
      'data-testid': props.placeholder ? `stub-input:${props.placeholder}` : 'stub-input',
      onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
      onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
    })
  },
})

export const StubLookup = defineComponent({
  props: {
    modelValue: {
      type: Object as PropType<LookupItem | null>,
      default: null,
    },
    items: {
      type: Array as PropType<LookupItem[]>,
      default: () => [],
    },
    placeholder: {
      type: String,
      default: '',
    },
    disabled: {
      type: Boolean,
      default: false,
    },
    showOpen: {
      type: Boolean,
      default: false,
    },
    showClear: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['query', 'update:modelValue', 'open'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'stub-lookup' }, [
      h('div', `lookup-value:${props.modelValue?.label ?? 'none'}`),
      h('div', `lookup-items:${props.items.map((item) => item.label).join('|') || 'none'}`),
      h('input', {
        type: 'text',
        value: '',
        placeholder: props.placeholder,
        disabled: props.disabled,
        onInput: (event: Event) => emit('query', (event.target as HTMLInputElement).value),
      }),
      h('button', {
        type: 'button',
        'data-action': 'select-first',
        disabled: props.disabled || props.items.length === 0,
        onClick: () => emit('update:modelValue', props.items[0] ?? null),
      }, 'Lookup select first'),
      props.showClear
        ? h('button', {
          type: 'button',
          'data-action': 'clear',
          disabled: props.disabled,
          onClick: () => emit('update:modelValue', null),
        }, 'Lookup clear')
        : null,
      props.showOpen
        ? h('button', {
          type: 'button',
          'data-action': 'open',
          onClick: () => emit('open'),
        }, 'Lookup open')
        : null,
    ])
  },
})

export const StubSwitch = defineComponent({
  props: {
    modelValue: {
      type: Boolean,
      default: false,
    },
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('button', {
      type: 'button',
      'data-testid': 'stub-switch',
      disabled: props.disabled,
      onClick: () => emit('update:modelValue', !props.modelValue),
    }, `switch:${String(props.modelValue)}`)
  },
})

export const StubDatePicker = defineComponent({
  props: {
    modelValue: {
      type: String,
      default: '',
    },
    placeholder: {
      type: String,
      default: '',
    },
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('input', {
      type: 'date',
      value: props.modelValue ?? '',
      placeholder: props.placeholder,
      disabled: props.disabled,
      'data-testid': props.placeholder ? `stub-date-picker:${props.placeholder}` : 'stub-date-picker',
      onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
      onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
    })
  },
})

export const StubDrawer = defineComponent({
  props: {
    open: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:open'],
  setup(props, { emit, slots }) {
    return () => props.open
      ? h('div', { 'data-testid': 'stub-drawer' }, [
        h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Drawer close'),
        slots.default?.(),
      ])
      : null
  },
})

export const StubDialog = defineComponent({
  props: {
    open: {
      type: Boolean,
      default: false,
    },
    title: {
      type: String,
      default: '',
    },
    subtitle: {
      type: String,
      default: '',
    },
    confirmText: {
      type: String,
      default: 'Confirm',
    },
    cancelText: {
      type: String,
      default: 'Cancel',
    },
    confirmLoading: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:open', 'confirm'],
  setup(props, { emit, slots }) {
    return () => props.open
      ? h('div', { 'data-testid': 'stub-dialog' }, [
        h('div', props.title),
        props.subtitle ? h('div', props.subtitle) : null,
        slots.default?.(),
        h('button', { type: 'button', onClick: () => emit('update:open', false) }, props.cancelText),
        h('button', {
          type: 'button',
          disabled: props.confirmLoading,
          onClick: () => emit('confirm'),
        }, `Dialog confirm:${props.confirmText}`),
      ])
      : null
  },
})

export const StubDateRangeFilter = defineComponent({
  props: {
    fromDate: {
      type: String,
      default: '',
    },
    toDate: {
      type: String,
      default: '',
    },
    fromPlaceholder: {
      type: String,
      default: '',
    },
    toPlaceholder: {
      type: String,
      default: '',
    },
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:fromDate', 'update:toDate'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'stub-date-range' }, [
      h('input', {
        type: 'date',
        value: props.fromDate,
        placeholder: props.fromPlaceholder,
        disabled: props.disabled,
        onInput: (event: Event) => emit('update:fromDate', (event.target as HTMLInputElement).value),
      }),
      h('input', {
        type: 'date',
        value: props.toDate,
        placeholder: props.toPlaceholder,
        disabled: props.disabled,
        onInput: (event: Event) => emit('update:toDate', (event.target as HTMLInputElement).value),
      }),
    ])
  },
})

export const StubReportComposerPanel = defineComponent({
  props: {
    selectedVariantCode: {
      type: String,
      default: '',
    },
    variantSummary: {
      type: String,
      default: '',
    },
    variantOptions: {
      type: Array as PropType<VariantOption[]>,
      default: () => [],
    },
  },
  emits: [
    'update:selectedVariantCode',
    'create-variant',
    'edit-variant',
    'save-variant',
    'delete-variant',
    'reset-variant',
    'load-variant',
    'run',
    'close',
  ],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'report-composer-panel' }, [
      h('div', { 'data-testid': 'composer-variant-summary' }, props.variantSummary),
      h('select', {
        'data-testid': 'composer-variant-select',
        value: props.selectedVariantCode,
        onInput: (event: Event) => emit('update:selectedVariantCode', (event.target as HTMLSelectElement).value),
        onChange: (event: Event) => emit('update:selectedVariantCode', (event.target as HTMLSelectElement).value),
      }, props.variantOptions.map((option) =>
        h('option', { key: option.value, value: option.value }, option.label),
      )),
      h('button', { type: 'button', onClick: () => emit('create-variant') }, 'Composer create variant'),
      h('button', { type: 'button', onClick: () => emit('edit-variant') }, 'Composer edit variant'),
      h('button', { type: 'button', onClick: () => emit('save-variant') }, 'Composer save variant'),
      h('button', { type: 'button', onClick: () => emit('delete-variant') }, 'Composer delete variant'),
      h('button', { type: 'button', onClick: () => emit('reset-variant') }, 'Composer reset variant'),
      h('button', { type: 'button', onClick: () => emit('load-variant') }, 'Composer load variant'),
      h('button', { type: 'button', onClick: () => emit('run') }, 'Composer run'),
      h('button', { type: 'button', onClick: () => emit('close') }, 'Composer close'),
    ])
  },
})

export const StubReportSheet = defineComponent({
  props: {
    sheet: {
      type: Object as PropType<ReportSheetDto | null>,
      default: null,
    },
    loading: {
      type: Boolean,
      default: false,
    },
    loadingMore: {
      type: Boolean,
      default: false,
    },
    canLoadMore: {
      type: Boolean,
      default: false,
    },
    showEndOfList: {
      type: Boolean,
      default: false,
    },
    loadedCount: {
      type: Number,
      default: 0,
    },
    totalCount: {
      type: Number,
      default: null,
    },
    rowNoun: {
      type: String,
      default: 'row',
    },
    currentReportContext: {
      type: Object as PropType<ReportContext | null>,
      default: null,
    },
    sourceTrail: {
      type: Object as PropType<{ items?: unknown[] } | null>,
      default: null,
    },
    backTarget: {
      type: String,
      default: '',
    },
  },
  emits: ['load-more', 'scroll-top-change'],
  setup(props, { emit, expose }) {
    const restoredScrollTop = ref(0)

    expose({
      restoreScrollTop(value: number) {
        restoredScrollTop.value = value
      },
    })

    return () => h('div', { 'data-testid': 'stub-report-sheet' }, [
      h('div', `rows:${props.sheet?.rows?.length ?? 0}`),
      h('div', `loaded:${props.loadedCount}`),
      h('div', `total:${props.totalCount ?? 'none'}`),
      h('div', `loading:${String(props.loading)}`),
      h('div', `loading-more:${String(props.loadingMore)}`),
      h('div', `show-end:${String(props.showEndOfList)}`),
      h('div', `row-noun:${props.rowNoun}`),
      h('div', `variant:${props.currentReportContext?.request?.variantCode ?? 'none'}`),
      h('div', `source-trail-count:${props.sourceTrail?.items?.length ?? 0}`),
      h('div', `back-target:${props.backTarget || 'none'}`),
      h('div', `restored-scroll-top:${restoredScrollTop.value}`),
      h('button', { type: 'button', onClick: () => emit('scroll-top-change', 120) }, 'Report sheet scroll'),
      props.canLoadMore
        ? h('button', { type: 'button', onClick: () => emit('load-more') }, 'Load more')
        : null,
    ])
  },
})
