import { defineComponent, h, type PropType } from 'vue'

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

export const StubSelect = defineComponent({
  props: {
    modelValue: {
      type: [String, Number, Boolean, Object, Array, null] as PropType<unknown>,
      default: null,
    },
    options: {
      type: Array as PropType<Array<{ value: string; label: string }>>,
      default: () => [],
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('button', {
      type: 'button',
      'data-testid': 'stub-select',
      onClick: () => emit('update:modelValue', 'selected'),
    }, `select:${props.options.map((option) => option.label).join('|')}`)
  },
})

export const StubLookupControl = defineComponent({
  props: {
    hint: {
      type: Object as PropType<Record<string, unknown> | null>,
      default: null,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('button', {
      type: 'button',
      'data-testid': 'stub-lookup',
      onClick: () => emit('update:modelValue', { id: 'lookup-id', display: 'Lookup Label' }),
    }, `lookup:${props.hint ? 'yes' : 'no'}`)
  },
})

export const StubCheckbox = defineComponent({
  props: {
    modelValue: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('button', {
      type: 'button',
      'data-testid': 'stub-checkbox',
      onClick: () => emit('update:modelValue', !props.modelValue),
    }, `checkbox:${String(props.modelValue)}`)
  },
})

export const StubDatePicker = defineComponent({
  emits: ['update:modelValue'],
  setup(_, { emit }) {
    return () => h('button', {
      type: 'button',
      'data-testid': 'stub-date-picker',
      onClick: () => emit('update:modelValue', '2026-04-08'),
    }, 'date-picker')
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
      role: 'switch',
      disabled: props.disabled,
      'aria-checked': String(props.modelValue),
      'data-testid': 'stub-switch',
      onClick: () => emit('update:modelValue', !props.modelValue),
    }, `switch:${String(props.modelValue)}`)
  },
})

export const StubInput = defineComponent({
  props: {
    type: {
      type: String,
      default: 'text',
    },
    modelValue: {
      type: [String, Number, Boolean, Object, Array, null] as PropType<unknown>,
      default: null,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('button', {
      type: 'button',
      'data-testid': 'stub-input',
      onClick: () => emit('update:modelValue', props.type === 'number' ? 42 : 'updated'),
    }, `input:${props.type}:${String(props.modelValue ?? '')}`)
  },
})

export const StubMultiSelect = defineComponent({
  props: {
    modelValue: {
      type: Array as PropType<Array<{ id: string; label: string }>>,
      default: () => [],
    },
    items: {
      type: Array as PropType<Array<{ id: string; label: string }>>,
      default: () => [],
    },
    disabled: {
      type: Boolean,
      default: false,
    },
    placeholder: {
      type: String,
      default: '',
    },
  },
  emits: ['query', 'update:modelValue'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'stub-multi-select' }, [
      h('div', `multi-selected:${props.modelValue.map((item) => item.label).join('|') || 'none'}`),
      h('div', `multi-items:${props.items.map((item) => item.label).join('|') || 'none'}`),
      h('input', {
        type: 'text',
        disabled: props.disabled,
        placeholder: props.placeholder,
        onInput: (event: Event) => emit('query', (event.target as HTMLInputElement).value),
      }),
      h('button', {
        type: 'button',
        disabled: props.disabled || props.items.length === 0,
        onClick: () => emit('update:modelValue', props.items.slice(0, 2)),
      }, 'Select many'),
    ])
  },
})

export const StubFormLayout = defineComponent({
  setup(_, { slots }) {
    return () => h('div', { 'data-testid': 'form-layout' }, slots.default?.())
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
    dense: {
      type: Boolean,
      default: false,
    },
    error: {
      type: String,
      default: '',
    },
  },
  setup(props, { slots }) {
    return () => h('div', { 'data-testid': 'form-row' }, [
      h('div', `row-label:${props.label}`),
      h('div', `row-hint:${props.hint || 'none'}`),
      h('div', `row-dense:${String(props.dense)}`),
      h('div', `row-error:${props.error || 'none'}`),
      slots.default?.(),
    ])
  },
})

export const StubFormSection = defineComponent({
  props: {
    title: {
      type: String,
      default: '',
    },
  },
  setup(props, { slots }) {
    return () => h('section', { 'data-testid': 'form-section' }, [
      h('h2', props.title),
      slots.default?.(),
    ])
  },
})

export const StubEntityFormFieldsBlock = defineComponent({
  props: {
    rows: {
      type: Array as PropType<Array<{ fields?: Array<{ key: string; label: string }> }>>,
      default: () => [],
    },
    displayField: {
      type: Object as PropType<{ key: string; label: string } | null>,
      default: null,
    },
  },
  setup(props) {
    const fieldKeys = [
      ...(props.displayField ? [props.displayField.key] : []),
      ...props.rows.flatMap((row) => (row.fields ?? []).map((field) => field.key)),
    ]

    return () => h('div', { 'data-testid': 'entity-form-fields-block' }, [
      h('div', `display-field:${props.displayField?.key ?? 'none'}`),
      h('div', `rows:${props.rows.length}`),
      ...fieldKeys.map((key) =>
        h('div', { key, 'data-validation-key': key }, [
          h('input', {
            'aria-label': `field-${key}`,
            value: key,
          }),
        ]),
      ),
    ])
  },
})

export const StubMetadataFieldRenderer = defineComponent({
  props: {
    field: {
      type: Object as PropType<{ key: string }>,
      required: true,
    },
    modelValue: {
      type: [String, Number, Boolean, Object, Array, null] as PropType<unknown>,
      default: null,
    },
    readonly: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': `metadata-field-renderer:${props.field.key}` }, [
      h('div', `renderer-key:${props.field.key}`),
      h('div', `renderer-readonly:${String(props.readonly)}`),
      h('div', `renderer-value:${JSON.stringify(props.modelValue ?? null)}`),
      h('button', {
        type: 'button',
        onClick: () => emit('update:modelValue', `updated:${props.field.key}`),
      }, `Update field:${props.field.key}`),
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
    subtitle: {
      type: String,
      default: '',
    },
  },
  emits: ['update:open'],
  setup(props, { emit, slots }) {
    return () => h('div', { 'data-testid': 'stub-drawer' }, [
      h('div', `drawer-open:${String(props.open)}`),
      h('div', `drawer-title:${props.title}`),
      h('div', `drawer-subtitle:${props.subtitle}`),
      h('div', { 'data-testid': 'stub-drawer-actions' }, slots.actions?.()),
      h('div', { 'data-testid': 'stub-drawer-body' }, slots.default?.()),
      props.open
        ? h('button', {
          type: 'button',
          onClick: () => emit('update:open', false),
        }, 'Drawer close')
        : null,
    ])
  },
})

export const StubMonthPicker = defineComponent({
  props: {
    modelValue: {
      type: String,
      default: null,
    },
    placeholder: {
      type: String,
      default: '',
    },
    disabled: {
      type: Boolean,
      default: false,
    },
    grouped: {
      type: Boolean,
      default: false,
    },
    displayFormat: {
      type: String,
      default: '',
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': `stub-month-picker:${props.placeholder || 'month'}` }, [
      h('div', `month-value:${props.modelValue ?? 'none'}`),
      h('div', `month-disabled:${String(props.disabled)}`),
      h('div', `month-grouped:${String(props.grouped)}`),
      h('div', `month-format:${props.displayFormat}`),
      h('button', {
        type: 'button',
        disabled: props.disabled,
        onClick: () => emit('update:modelValue', '2026-04'),
      }, 'Month set'),
      h('button', {
        type: 'button',
        disabled: props.disabled,
        onClick: () => emit('update:modelValue', null),
      }, 'Month clear'),
    ])
  },
})

export const StubFilterFieldControl = defineComponent({
  props: {
    field: {
      type: Object as PropType<{ key: string; label?: string; description?: string | null }>,
      required: true,
    },
    state: {
      type: Object as PropType<{ raw?: string; items?: Array<{ id: string; label: string }> }>,
      default: () => ({ raw: '', items: [] }),
    },
    lookupItems: {
      type: Array as PropType<Array<{ id: string; label: string }>>,
      default: () => [],
    },
    disabled: {
      type: Boolean,
      default: false,
    },
    selectEmptyLabel: {
      type: String,
      default: '',
    },
  },
  emits: ['lookup-query', 'update:items', 'update:raw'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': `stub-filter-field:${props.field.key}` }, [
      h('div', `state-raw:${props.state?.raw ?? ''}`),
      h('div', `lookup-items:${props.lookupItems.map((item) => item.label).join('|') || 'none'}`),
      h('div', `empty-label:${props.selectEmptyLabel}`),
      h('button', {
        type: 'button',
        disabled: props.disabled,
        onClick: () => emit('lookup-query', 'river'),
      }, 'Field lookup query'),
      h('button', {
        type: 'button',
        disabled: props.disabled,
        onClick: () => emit('update:items', [{ id: 'property-1', label: 'Riverfront Tower' }]),
      }, 'Field set items'),
      h('button', {
        type: 'button',
        disabled: props.disabled,
        onClick: () => emit('update:raw', 'posted'),
      }, 'Field set raw'),
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
    return () => h('div', { 'data-testid': 'stub-page-header' }, [
      h('div', `header-title:${props.title}`),
      h('div', `header-can-back:${String(props.canBack)}`),
      props.canBack
        ? h('button', { type: 'button', onClick: () => emit('back') }, 'Header back')
        : null,
      h('div', { 'data-testid': 'stub-page-header-secondary' }, slots.secondary?.()),
      h('div', { 'data-testid': 'stub-page-header-actions' }, slots.actions?.()),
    ])
  },
})

type RegisterRow = Record<string, unknown> & {
  key: string
}

export const StubRegisterPageLayout = defineComponent({
  props: {
    title: {
      type: String,
      default: '',
    },
    warning: {
      type: String,
      default: null,
    },
    filterActive: {
      type: Boolean,
      default: false,
    },
    storageKey: {
      type: String,
      default: '',
    },
    rows: {
      type: Array as PropType<RegisterRow[]>,
      default: () => [],
    },
    drawerOpen: {
      type: Boolean,
      default: false,
    },
    beforeClose: {
      type: Function as PropType<(() => boolean | Promise<boolean>) | null>,
      default: null,
    },
  },
  emits: ['back', 'refresh', 'create', 'filter', 'prev', 'next', 'rowActivate', 'update:drawerOpen'],
  setup(props, { emit, slots }) {
    async function requestDrawerClose() {
      const allowed = await props.beforeClose?.()
      if (allowed === false) return
      emit('update:drawerOpen', false)
    }

    return () => h('div', { 'data-testid': 'register-page-layout' }, [
      h('div', `title:${props.title}`),
      h('div', `storage:${props.storageKey}`),
      h('div', `filter-active:${String(props.filterActive)}`),
      h('div', `drawer-open:${String(props.drawerOpen)}`),
      props.warning ? h('div', `warning:${props.warning}`) : null,
      h('button', { type: 'button', onClick: () => emit('back') }, 'Layout back'),
      h('button', { type: 'button', onClick: () => emit('refresh') }, 'Layout refresh'),
      h('button', { type: 'button', onClick: () => emit('create') }, 'Layout create'),
      h('button', { type: 'button', onClick: () => emit('filter') }, 'Layout filter'),
      h('button', { type: 'button', onClick: () => emit('prev') }, 'Layout prev'),
      h('button', { type: 'button', onClick: () => emit('next') }, 'Layout next'),
      props.drawerOpen
        ? h('button', { type: 'button', onClick: () => void requestDrawerClose() }, 'Layout close drawer')
        : null,
      h('div', { 'data-testid': 'layout-filters' }, slots.filters?.()),
      h('div', { 'data-testid': 'layout-before-grid' }, slots.beforeGrid?.()),
      h(
        'div',
        { 'data-testid': 'layout-grid' },
        slots.grid?.() ?? props.rows.map((row) =>
          h(
            'button',
            {
              key: row.key,
              type: 'button',
              onClick: () => emit('rowActivate', String(row.key)),
            },
            `Row:${String(row.key)}`,
          ),
        ),
      ),
      h('div', { 'data-testid': 'layout-filter-drawer' }, slots.filterDrawer?.()),
      h('div', { 'data-testid': 'layout-drawer-actions' }, slots.drawerActions?.()),
      h('div', { 'data-testid': 'layout-drawer-content' }, slots.drawerContent?.()),
    ])
  },
})

export const StubRecycleBinFilter = defineComponent({
  props: {
    modelValue: {
      type: String,
      default: 'active',
    },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () => h('button', {
      type: 'button',
      'data-testid': 'stub-recycle-bin-filter',
      onClick: () => emit('update:modelValue', props.modelValue === 'active' ? 'deleted' : 'active'),
    }, `trash:${props.modelValue}`)
  },
})

export const StubDocumentPeriodFilter = defineComponent({
  props: {
    fromMonth: {
      type: String,
      default: '',
    },
    toMonth: {
      type: String,
      default: '',
    },
  },
  emits: ['update:fromMonth', 'update:toMonth'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'stub-period-filter' }, [
      h('div', `from:${props.fromMonth || 'none'}`),
      h('div', `to:${props.toMonth || 'none'}`),
      h('button', { type: 'button', onClick: () => emit('update:fromMonth', '2026-03') }, 'Set from month'),
      h('button', { type: 'button', onClick: () => emit('update:toMonth', '2026-04') }, 'Set to month'),
    ])
  },
})

export const StubDocumentListFiltersDrawer = defineComponent({
  props: {
    open: {
      type: Boolean,
      default: false,
    },
    canUndo: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['update:open', 'lookup-query', 'update:items', 'update:value', 'undo'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'stub-document-filters-drawer' }, [
      h('div', `filter-drawer-open:${String(props.open)}`),
      props.open
        ? h('button', {
          type: 'button',
          onClick: () => emit('lookup-query', {
            key: 'property_id',
            query: 'river',
          }),
        }, 'Filter lookup query')
        : null,
      props.open
        ? h('button', {
          type: 'button',
          onClick: () => emit('update:items', {
            key: 'property_id',
            items: [{ id: 'property-1', label: 'Riverfront Tower' }],
          }),
        }, 'Filter set items')
        : null,
      props.open
        ? h('button', {
          type: 'button',
          onClick: () => emit('update:value', {
            key: 'status',
            value: 'posted',
          }),
        }, 'Filter set value')
        : null,
      props.open && props.canUndo
        ? h('button', { type: 'button', onClick: () => emit('undo') }, 'Filter undo')
        : null,
      props.open
        ? h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Filter close')
        : null,
    ])
  },
})

export const StubBadge = defineComponent({
  setup(_, { slots }) {
    return () => h('span', { 'data-testid': 'stub-badge' }, slots.default?.())
  },
})

export const StubEntityEditorDrawerActions = defineComponent({
  props: {
    extraActions: {
      type: Array as PropType<Array<{ key: string; title?: string }>>,
      default: () => [],
    },
  },
  emits: ['action'],
  setup(props, { emit }) {
    const standardActions = ['expand', 'share', 'audit', 'mark', 'save']

    return () => h('div', { 'data-testid': 'stub-drawer-actions' }, [
      ...standardActions.map((action) =>
        h(
          'button',
          {
            key: action,
            type: 'button',
            onClick: () => emit('action', action),
          },
          `Drawer action:${action}`,
        ),
      ),
      ...props.extraActions.map((action) =>
        h(
          'button',
          {
            key: action.key,
            type: 'button',
            onClick: () => emit('action', action.key),
          },
          `Drawer action:${action.key}`,
        ),
      ),
    ])
  },
})

export const StubEditorDiscardDialog = defineComponent({
  props: {
    open: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['cancel', 'confirm'],
  setup(props, { emit }) {
    return () => props.open
      ? h('div', { 'data-testid': 'stub-discard-dialog' }, [
        h('button', { type: 'button', onClick: () => emit('cancel') }, 'Discard cancel'),
        h('button', { type: 'button', onClick: () => emit('confirm') }, 'Discard confirm'),
      ])
      : null
  },
})
