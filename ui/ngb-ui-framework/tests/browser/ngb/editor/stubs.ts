import { computed, defineComponent, h, ref, type PropType } from 'vue'

type TabItem = {
  key: string
  label: string
}

type RegisterColumn = {
  key: string
  title?: string
}

type RegisterRow = Record<string, unknown> & {
  key: string
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
        ? h('button', { type: 'button', onClick: () => emit('back') }, 'Back')
        : null,
      h('h1', props.title),
      h('div', { 'data-testid': 'page-header-secondary' }, slots.secondary?.()),
      h('div', { 'data-testid': 'page-header-actions' }, slots.actions?.()),
    ])
  },
})

export const StubBadge = defineComponent({
  setup(_, { slots }) {
    return () => h('span', { 'data-testid': 'badge' }, slots.default?.())
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
    return () => h('span', { 'data-testid': `icon-${props.name}` })
  },
})

export const StubStatusIcon = defineComponent({
  props: {
    status: {
      type: String,
      default: '',
    },
  },
  setup(props) {
    return () => h('span', { 'data-testid': `status-${props.status}` })
  },
})

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
    const active = computed(() => props.modelValue ?? props.tabs[0]?.key ?? null)

    return () => h('div', { 'data-testid': 'tabs-root' }, [
      h(
        'div',
        { 'data-testid': 'tabs-list' },
        props.tabs.map((tab) =>
          h(
            'button',
            {
              type: 'button',
              onClick: () => emit('update:modelValue', tab.key),
            },
            tab.label,
          ),
        ),
      ),
      slots.default?.({
        active: active.value,
      }),
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
  },
  emits: ['rowActivate'],
  setup(props, { emit }) {
    function rowText(row: RegisterRow): string {
      return props.columns
        .map((column) => String(row[column.key] ?? ''))
        .filter((value) => value.trim().length > 0)
        .join(' | ')
    }

    return () => h('div', { 'data-testid': 'register-grid', 'data-storage-key': props.storageKey }, [
      ...props.rows.map((row) =>
        h(
          'button',
          {
            key: row.key,
            type: 'button',
            'data-testid': `register-row-${row.key}`,
            onClick: () => emit('rowActivate', row),
          },
          rowText(row),
        ),
      ),
    ])
  },
})

export const StubEntityEditorHeader = defineComponent({
  props: {
    kind: {
      type: String,
      default: 'document',
    },
    mode: {
      type: String,
      default: 'page',
    },
    canBack: {
      type: Boolean,
      default: true,
    },
    title: {
      type: String,
      default: '',
    },
    subtitle: {
      type: String,
      default: '',
    },
    documentStatusLabel: {
      type: String,
      default: '',
    },
    documentStatusTone: {
      type: String,
      default: 'neutral',
    },
    pageActions: {
      type: Array as PropType<Array<{ key: string }>>,
      default: () => [],
    },
    documentPrimaryActions: {
      type: Array as PropType<Array<{ key: string }>>,
      default: () => [],
    },
    documentMoreActionGroups: {
      type: Array as PropType<Array<{ key: string }>>,
      default: () => [],
    },
  },
  emits: ['back', 'close', 'action'],
  setup(props, { emit }) {
    return () => h('div', {
      'data-testid': 'editor-header',
      'data-kind': props.kind,
      'data-mode': props.mode,
      'data-status-tone': props.documentStatusTone,
    }, [
      h('h1', props.title),
      props.subtitle ? h('p', props.subtitle) : null,
      h('div', `Status: ${props.documentStatusLabel}`),
      h('div', `Page actions: ${props.pageActions.length}`),
      h('div', `Primary actions: ${props.documentPrimaryActions.length}`),
      h('div', `More groups: ${props.documentMoreActionGroups.length}`),
      props.canBack ? h('button', { type: 'button', onClick: () => emit('back') }, 'Header back') : null,
      h('button', { type: 'button', onClick: () => emit('close') }, 'Header close'),
      h(
        'button',
        {
          type: 'button',
          onClick: () => emit('action', props.documentPrimaryActions[0]?.key ?? 'save'),
        },
        'Header action',
      ),
    ])
  },
})

export const StubEntityForm = defineComponent({
  props: {
    entityTypeCode: {
      type: String,
      default: '',
    },
    status: {
      type: Number,
      default: undefined,
    },
    forceReadonly: {
      type: Boolean,
      default: false,
    },
    presentation: {
      type: String,
      default: 'sections',
    },
    errors: {
      type: Object as PropType<Record<string, string | string[] | null> | null>,
      default: null,
    },
  },
  setup(props, { expose }) {
    const lastFocusPath = ref('none')
    const lastErrorKeys = ref('none')

    function focusField(path: string): boolean {
      lastFocusPath.value = path
      return path === 'customer_id'
    }

    function focusFirstError(keys: string[]): boolean {
      lastErrorKeys.value = keys.join('|') || 'none'
      return keys.includes('amount')
    }

    expose({
      focusField,
      focusFirstError,
    })

    return () => h('div', {
      'data-testid': 'entity-form',
      'data-entity-type': props.entityTypeCode,
      'data-presentation': props.presentation,
      'data-status': props.status == null ? 'none' : String(props.status),
      'data-readonly': String(props.forceReadonly),
    }, [
      h('div', `Last focus: ${lastFocusPath.value}`),
      h('div', `Last error keys: ${lastErrorKeys.value}`),
      h('div', `Error keys: ${Object.keys(props.errors ?? {}).join('|') || 'none'}`),
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
    return () => h('div', {
      'data-testid': 'drawer',
      'data-open': String(props.open),
    }, [
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

export const StubDiscardDialog = defineComponent({
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
  },
  emits: ['cancel', 'confirm'],
  setup(props, { emit }) {
    return () => props.open
      ? h('div', { 'data-testid': 'discard-dialog' }, [
        h('div', props.title),
        h('div', props.message),
        h('button', { type: 'button', onClick: () => emit('cancel') }, 'Leave cancel'),
        h('button', { type: 'button', onClick: () => emit('confirm') }, 'Leave confirm'),
      ])
      : null
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
  },
  emits: ['update:open', 'confirm'],
  setup(props, { emit }) {
    return () => props.open
      ? h('div', { 'data-testid': 'confirm-dialog' }, [
        h('div', props.title),
        h('div', props.message),
        h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Mark cancel'),
        h('button', { type: 'button', onClick: () => emit('confirm') }, 'Mark confirm'),
      ])
      : null
  },
})

export const StubHeaderActionCluster = defineComponent({
  props: {
    primaryActions: {
      type: Array as PropType<Array<{ key: string; title: string }>>,
      default: () => [],
    },
    moreGroups: {
      type: Array as PropType<Array<{ key: string; label: string; items: Array<{ key: string; title: string }> }>>,
      default: () => [],
    },
    closeDisabled: {
      type: Boolean,
      default: false,
    },
  },
  emits: ['action', 'close'],
  setup(props, { emit }) {
    return () => h('div', { 'data-testid': 'header-action-cluster' }, [
      ...props.primaryActions.map((item) =>
        h(
          'button',
          {
            key: item.key,
            type: 'button',
            onClick: () => emit('action', item.key),
          },
          `Primary: ${item.title}`,
        ),
      ),
      ...props.moreGroups.flatMap((group) =>
        group.items.map((item) =>
          h(
            'button',
            {
              key: `${group.key}:${item.key}`,
              type: 'button',
              onClick: () => emit('action', item.key),
            },
            `More: ${group.label} / ${item.title}`,
          ),
        ),
      ),
      h(
        'button',
        {
          type: 'button',
          disabled: props.closeDisabled,
          onClick: () => emit('close'),
        },
        'Cluster close',
      ),
    ])
  },
})
