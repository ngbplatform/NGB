import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

vi.mock('../../../../src/ngb/metadata/NgbEntityListPageHeader.vue', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')

  return {
    default: defineComponent({
      props: {
        title: {
          type: String,
          default: '',
        },
      },
      emits: ['back', 'refresh', 'create', 'filter', 'prev', 'next'],
      setup(props, { emit, slots }) {
        return () => h('div', { 'data-testid': 'register-layout-header' }, [
          h('div', `header-title:${props.title}`),
          h('button', { type: 'button', onClick: () => emit('back') }, 'Header back'),
          h('button', { type: 'button', onClick: () => emit('refresh') }, 'Header refresh'),
          h('button', { type: 'button', onClick: () => emit('create') }, 'Header create'),
          h('button', { type: 'button', onClick: () => emit('filter') }, 'Header filter'),
          h('button', { type: 'button', onClick: () => emit('prev') }, 'Header prev'),
          h('button', { type: 'button', onClick: () => emit('next') }, 'Header next'),
          h('div', { 'data-testid': 'header-filters-slot' }, slots.filters?.()),
        ])
      },
    }),
  }
})

vi.mock('../../../../src/ngb/components/register/NgbRegisterGrid.vue', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')

  return {
    default: defineComponent({
      props: {
        rows: {
          type: Array,
          default: () => [],
        },
        storageKey: {
          type: String,
          default: '',
        },
      },
      emits: ['rowActivate'],
      setup(props, { emit }) {
        return () => h('div', { 'data-testid': 'register-layout-grid' }, [
          h('div', `grid-storage:${props.storageKey}`),
          ...(props.rows as Array<{ key: string }>).map((row) =>
            h('button', {
              key: row.key,
              type: 'button',
              onClick: () => emit('rowActivate', row.key),
            }, `Row:${row.key}`),
          ),
        ])
      },
    }),
  }
})

vi.mock('../../../../src/ngb/components/NgbDrawer.vue', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')

  return {
    default: defineComponent({
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
        hideHeader: {
          type: Boolean,
          default: false,
        },
        flushBody: {
          type: Boolean,
          default: false,
        },
        beforeClose: {
          type: Function,
          default: undefined,
        },
      },
      emits: ['update:open'],
      setup(props, { emit, slots }) {
        async function close() {
          const allowed = await props.beforeClose?.()
          if (allowed === false) return
          emit('update:open', false)
        }

        return () => h('div', { 'data-testid': 'register-layout-drawer' }, [
          h('div', `drawer-open:${String(props.open)}`),
          h('div', `drawer-title:${props.title}`),
          h('div', `drawer-subtitle:${props.subtitle}`),
          h('div', `drawer-hide-header:${String(props.hideHeader)}`),
          h('div', `drawer-flush-body:${String(props.flushBody)}`),
          h('div', { 'data-testid': 'drawer-actions-slot' }, slots.actions?.()),
          h('div', { 'data-testid': 'drawer-body-slot' }, slots.default?.()),
          props.open
            ? h('button', { type: 'button', onClick: () => void close() }, 'Drawer close')
            : null,
        ])
      },
    }),
  }
})

import NgbRegisterPageLayout from '../../../../src/ngb/metadata/NgbRegisterPageLayout.vue'

const RegisterLayoutHarness = defineComponent({
  props: {
    blockClose: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const drawerOpen = ref(true)
    const events = ref<string[]>([])

    return () => h('div', [
      h(NgbRegisterPageLayout, {
        title: 'Properties',
        itemsCount: 2,
        total: 20,
        loading: true,
        error: 'Failed to load register rows.',
        warning: 'Some rows are stale.',
        columns: [{ key: 'name', title: 'Name' }],
        rows: [{ key: 'row-1', name: 'Riverfront Tower' }],
        storageKey: 'register:properties',
        drawerOpen: drawerOpen.value,
        drawerTitle: 'Edit property',
        drawerSubtitle: 'Drawer subtitle',
        drawerHideHeader: true,
        drawerFlushBody: true,
        beforeClose: () => !props.blockClose,
        onBack: () => events.value.push('back'),
        onRefresh: () => events.value.push('refresh'),
        onCreate: () => events.value.push('create'),
        onFilter: () => events.value.push('filter'),
        onPrev: () => events.value.push('prev'),
        onNext: () => events.value.push('next'),
        onRowActivate: (id: string) => events.value.push(`row:${id}`),
        'onUpdate:drawerOpen': (value: boolean) => {
          drawerOpen.value = value
          events.value.push(`drawer:${value}`)
        },
      }, {
        filters: () => h('div', { 'data-testid': 'filters-slot' }, 'Filters slot'),
        beforeGrid: () => h('div', { 'data-testid': 'before-grid-slot' }, 'Before grid slot'),
        filterDrawer: () => h('div', { 'data-testid': 'filter-drawer-slot' }, 'Filter drawer slot'),
        drawerActions: () => h('button', { type: 'button' }, 'Drawer action'),
        drawerContent: () => h('div', { 'data-testid': 'drawer-content-slot' }, 'Drawer content'),
      }),
      h('div', { 'data-testid': 'layout-events' }, events.value.join('|') || 'none'),
      h('div', { 'data-testid': 'drawer-state' }, `open:${drawerOpen.value}`),
    ])
  },
})

test('renders banners and slots, forwards header and row events, and coordinates the drawer contract', async () => {
  await page.viewport(1280, 900)

  const view = await render(RegisterLayoutHarness)

  await expect.element(view.getByText('Failed to load register rows.', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Some rows are stale.', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Loading…', { exact: true })).toBeVisible()
  await expect.element(view.getByTestId('filters-slot')).toBeVisible()
  await expect.element(view.getByTestId('before-grid-slot')).toBeVisible()
  await expect.element(view.getByTestId('filter-drawer-slot')).toBeVisible()
  await expect.element(view.getByTestId('drawer-content-slot')).toBeVisible()
  await expect.element(view.getByText('drawer-hide-header:true', { exact: true })).toBeVisible()
  await expect.element(view.getByText('drawer-flush-body:true', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Header back' }).click()
  await view.getByRole('button', { name: 'Header refresh' }).click()
  await view.getByRole('button', { name: 'Header create' }).click()
  await view.getByRole('button', { name: 'Header filter' }).click()
  await view.getByRole('button', { name: 'Header prev' }).click()
  await view.getByRole('button', { name: 'Header next' }).click()
  await view.getByRole('button', { name: 'Row:row-1' }).click()
  await view.getByRole('button', { name: 'Drawer close' }).click()

  await expect.element(view.getByTestId('layout-events')).toHaveTextContent(
    'back|refresh|create|filter|prev|next|row:row-1|drawer:false',
  )
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent('open:false')
})

test('keeps the drawer open when beforeClose blocks the close request', async () => {
  await page.viewport(1280, 900)

  const view = await render(RegisterLayoutHarness, {
    props: {
      blockClose: true,
    },
  })

  await view.getByRole('button', { name: 'Drawer close' }).click()
  await expect.element(view.getByTestId('drawer-state')).toHaveTextContent('open:true')
  await expect.element(view.getByTestId('layout-events')).toHaveTextContent('none')
})
