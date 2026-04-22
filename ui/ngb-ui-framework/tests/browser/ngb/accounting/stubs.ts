import { defineComponent, h, type PropType } from 'vue'

type RegisterColumn = {
  key: string
  title?: string
  format?: (value: unknown, row: Record<string, unknown>) => string
}

type RegisterRow = Record<string, unknown> & {
  key: string
}

export const StubEntityListPageHeader = defineComponent({
  props: {
    title: {
      type: String,
      default: '',
    },
    canBack: {
      type: Boolean,
      default: false,
    },
    disablePrev: {
      type: Boolean,
      default: false,
    },
    disableNext: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['back', 'refresh', 'create', 'filter', 'prev', 'next'],
  setup(props, { emit, slots }) {
    return () => h('div', { 'data-testid': 'entity-list-header' }, [
      h('div', `title:${props.title}`),
      h('button', { type: 'button', onClick: () => emit('back') }, 'Header back'),
      h('button', { type: 'button', onClick: () => emit('refresh') }, 'Header refresh'),
      h('button', { type: 'button', onClick: () => emit('create') }, 'Header create'),
      h('button', { type: 'button', onClick: () => emit('prev'), disabled: props.disablePrev }, 'Header prev'),
      h('button', { type: 'button', onClick: () => emit('next'), disabled: props.disableNext }, 'Header next'),
      h('div', { 'data-testid': 'header-filters' }, slots.filters?.()),
    ])
  },
})

export const StubRegisterGrid = defineComponent({
  props: {
    columns: {
      type: Array as PropType<RegisterColumn[]>,
      default: () => [],
    },
    rows: {
      type: Array as PropType<RegisterRow[]>,
      default: () => [],
    },
    storageKey: {
      type: String,
      default: '',
    },
    groupBy: {
      type: Array as PropType<string[]>,
      default: () => [],
    },
  },
  emits: ['rowActivate'],
  setup(props, { emit, slots }) {
    const toggleAllGroups = () => undefined

    function formatRow(row: RegisterRow): string {
      return props.columns
        .map((column) => {
          const raw = row[column.key]
          const formatted = column.format ? column.format(raw, row) : raw == null ? '—' : String(raw)
          return `${column.key}=${formatted}`
        })
        .join(' | ')
    }

    return () => h('div', { 'data-testid': 'stub-register-grid' }, [
      h('div', `storage:${props.storageKey}`),
      h('div', `group-by:${props.groupBy.join('|') || 'none'}`),
      h('div', { 'data-testid': 'stub-register-grid-status-header' }, slots.statusHeader?.({
        hasGroups: props.groupBy.length > 0,
        allGroupsExpanded: true,
        toggleAllGroups,
      })),
      ...props.rows.map((row) =>
        h(
          'button',
          {
            key: row.key,
            type: 'button',
            onClick: () => emit('rowActivate', String(row.key)),
          },
          formatRow(row),
        ),
      ),
    ])
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

export const StubButton = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
    loading: {
      type: Boolean,
      default: false,
    },
    variant: {
      type: String,
      default: 'primary',
    },
  },
  emits: ['click'],
  setup(props, { emit, slots }) {
    return () => h('button', {
      type: 'button',
      disabled: props.disabled || props.loading,
      'data-variant': props.variant,
      'data-loading': String(props.loading),
      onClick: () => emit('click'),
    }, slots.default?.())
  },
})

export const StubInput = defineComponent({
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
    readonly: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('input', {
      type: 'text',
      value: props.modelValue,
      placeholder: props.placeholder,
      disabled: props.disabled,
      readOnly: props.readonly,
      onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
    })
  },
})

function shiftStubMonth(value: string): string {
  const match = /^(\d{4})-(\d{2})$/.exec(String(value).trim())
  if (!match) return '2026-01'

  const year = Number(match[1])
  const month = Number(match[2])
  const candidate = new Date(year, month, 1)
  return `${candidate.getFullYear()}-${String(candidate.getMonth() + 1).padStart(2, '0')}`
}

export const StubMonthPicker = defineComponent({
  props: {
    modelValue: {
      type: String,
      default: '',
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('button', {
      type: 'button',
      'data-testid': `month-picker-${props.modelValue || 'empty'}`,
      onClick: () => emit('update:modelValue', shiftStubMonth(props.modelValue)),
    }, `month-picker:${props.modelValue || 'none'}`)
  },
})

type LookupItem = {
  id: string
  label: string
  meta?: string | null
}

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
    readonly: {
      type: Boolean,
      default: false,
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
  emits: ['update:modelValue', 'query', 'open'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'stub-lookup' }, [
      h('div', `lookup-value:${props.modelValue?.label ?? 'none'}`),
      h('div', `lookup-items:${props.items.map((item) => item.label).join('|') || 'none'}`),
      h('input', {
        type: 'text',
        value: '',
        placeholder: props.placeholder,
        disabled: props.disabled || props.readonly,
        onInput: (event: Event) => emit('query', (event.target as HTMLInputElement).value),
      }),
      h('button', {
        type: 'button',
        'data-action': 'select-first',
        disabled: props.disabled || props.readonly || props.items.length === 0,
        onClick: () => emit('update:modelValue', props.items[0] ?? null),
      }, 'Lookup select first'),
      props.showClear
        ? h('button', {
          type: 'button',
          'data-action': 'clear',
          disabled: props.disabled || props.readonly,
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

export const StubConfirmDialog = defineComponent({
  props: {
    open: {
      type: Boolean,
      default: false,
    },
    title: {
      type: String,
      default: '',
    },
    message: {
      type: String,
      default: '',
    },
    confirmText: {
      type: String,
      default: 'Confirm',
    },
  },
  emits: ['update:open', 'confirm'],
  setup(props, { emit }) {
    return () => props.open
      ? h('div', { 'data-testid': 'confirm-dialog' }, [
        h('div', props.title),
        h('div', props.message),
        h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Dialog cancel'),
        h('button', { type: 'button', onClick: () => emit('confirm') }, `Dialog confirm:${props.confirmText}`),
      ])
      : null
  },
})

export const StubValidationSummary = defineComponent({
  props: {
    messages: {
      type: Array as PropType<string[]>,
      default: () => [],
    },
  },
  setup(props) {
    return () => h('div', { 'data-testid': 'validation-summary' }, props.messages.map((message) =>
      h('div', { key: message }, message),
    ))
  },
})

export const StubCheckbox = defineComponent({
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
    return () => h('input', {
      type: 'checkbox',
      checked: props.modelValue,
      disabled: props.disabled,
      onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).checked),
      onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).checked),
    })
  },
})

export const StubDatePicker = defineComponent({
  props: {
    modelValue: {
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
      disabled: props.disabled,
      onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
      onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
    })
  },
})

export const StubSelect = defineComponent({
  props: {
    modelValue: {
      type: [String, Number],
      default: '',
    },
    options: {
      type: Array as PropType<Array<{ value: string | number; label: string }>>,
      default: () => [],
    },
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h(
      'select',
      {
        value: String(props.modelValue ?? ''),
        disabled: props.disabled,
        onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLSelectElement).value),
        onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLSelectElement).value),
      },
      props.options.map((option) =>
        h('option', { key: String(option.value), value: String(option.value) }, option.label),
      ),
    )
  },
})

export const StubFormLayout = defineComponent({
  setup(_, { slots }) {
    return () => h('div', { 'data-testid': 'form-layout' }, slots.default?.())
  },
})

export const StubFormSection = defineComponent({
  props: {
    title: {
      type: String,
      default: '',
    },
    description: {
      type: String,
      default: '',
    },
  },
  setup(props, { slots }) {
    return () => h('section', { 'data-testid': `form-section-${props.title}` }, [
      h('h2', props.title),
      props.description ? h('div', props.description) : null,
      slots.default?.(),
    ])
  },
})

export const StubFormRow = defineComponent({
  props: {
    label: {
      type: String,
      default: '',
    },
    hint: {
      type: String,
      default: '',
    },
  },
  setup(props, { slots }) {
    return () => h('div', { 'data-testid': `form-row-${props.label}` }, [
      h('label', props.label),
      props.hint ? h('div', props.hint) : null,
      slots.default?.(),
    ])
  },
})

type TabItem = {
  key: string
  label: string
}

export const StubTabs = defineComponent({
  props: {
    modelValue: {
      type: String,
      default: null,
    },
    tabs: {
      type: Array as PropType<TabItem[]>,
      default: () => [],
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit, slots }) {
    const active = () => props.modelValue ?? props.tabs[0]?.key ?? null

    return () => h('div', { 'data-testid': 'tabs-root' }, [
      h(
        'div',
        { 'data-testid': 'tabs-list' },
        props.tabs.map((tab) =>
          h(
            'button',
            {
              key: tab.key,
              type: 'button',
              onClick: () => emit('update:modelValue', tab.key),
            },
            tab.label,
          ),
        ),
      ),
      slots.default?.({
        active: active(),
      }),
    ])
  },
})

type GjeLine = {
  clientKey: string
  side: number
  account: LookupItem | null
  amount: string
  memo: string
  dimensions: Record<string, LookupItem | null>
}

export const StubGeneralJournalEntryLinesEditor = defineComponent({
  props: {
    modelValue: {
      type: Array as PropType<GjeLine[]>,
      default: () => [],
    },
    readonly: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    const sampleLines = (): GjeLine[] => [
      {
        clientKey: 'sample-1',
        side: 1,
        account: { id: 'cash-id', label: '1100 Cash' },
        amount: '1,250.50',
        memo: 'Debit leg',
        dimensions: {},
      },
      {
        clientKey: 'sample-2',
        side: 2,
        account: { id: 'revenue-id', label: '4100 Revenue' },
        amount: '1,250.50',
        memo: 'Credit leg',
        dimensions: {},
      },
    ]

    return () => h('div', { 'data-testid': 'gje-lines-editor', 'data-readonly': String(props.readonly) }, [
      ...props.modelValue.map((line, index) =>
        h(
          'div',
          {
            key: line.clientKey,
            'data-testid': `gje-line-${index}`,
          },
          `line:${line.side}:${line.account?.label ?? 'none'}:${line.amount}:${line.memo}`,
        ),
      ),
      h(
        'button',
        {
          type: 'button',
          disabled: props.readonly,
          onClick: () => emit('update:modelValue', sampleLines()),
        },
        'Set sample lines',
      ),
    ])
  },
})

export const StubDrawer = defineComponent({
  props: {
    open: {
      type: Boolean,
      default: false,
    },
    title: {
      type: String,
      default: '',
    },
  },
  emits: ['update:open'],
  setup(props, { emit, slots }) {
    return () => h('div', { 'data-testid': 'drawer', 'data-open': String(props.open) }, [
      h('div', props.title),
      props.open ? h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Drawer close') : null,
      props.open ? slots.default?.() : null,
    ])
  },
})

export const StubEntityAuditSidebar = defineComponent({
  props: {
    open: {
      type: Boolean,
      default: false,
    },
    entityKind: {
      type: Number,
      default: 0,
    },
    entityId: {
      type: String,
      default: '',
    },
    entityTitle: {
      type: String,
      default: '',
    },
  },
  emits: ['back', 'close'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'audit-sidebar' }, [
      h('div', `Audit entity: ${props.entityTitle}`),
      h('div', `Audit kind: ${props.entityKind}`),
      h('div', `Audit id: ${props.entityId}`),
      props.open ? h('button', { type: 'button', onClick: () => emit('back') }, 'Audit back') : null,
      props.open ? h('button', { type: 'button', onClick: () => emit('close') }, 'Audit close') : null,
    ])
  },
})
