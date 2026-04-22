import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import {
  StubBadge,
  StubHeaderActionCluster,
  StubIcon,
  StubPageHeader,
} from './stubs'

vi.mock('../../../../src/ngb/components/NgbHeaderActionCluster.vue', () => ({
  default: StubHeaderActionCluster,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/site/NgbPageHeader.vue', () => ({
  default: StubPageHeader,
}))

import NgbEntityEditorHeader from '../../../../src/ngb/editor/NgbEntityEditorHeader.vue'

const DocumentPageHarness = defineComponent({
  setup() {
    const events = ref<string[]>([])
    const push = (value: string) => {
      events.value = [...events.value, value]
    }

    return () => h('div', [
      h(NgbEntityEditorHeader, {
        kind: 'document',
        mode: 'page',
        canBack: true,
        title: 'Invoice INV-001',
        subtitle: 'Ignored in document mode',
        documentStatusLabel: 'Posted',
        documentStatusTone: 'success',
        loading: false,
        saving: false,
        pageActions: [],
        documentPrimaryActions: [{ key: 'save', title: 'Save', icon: 'save' }],
        documentMoreActionGroups: [{ key: 'output', label: 'Output', items: [{ key: 'printDocument', title: 'Print', icon: 'printer' }] }],
        onBack: () => push('back'),
        onClose: () => push('close'),
        onAction: (action: string) => push(`action:${action}`),
      }),
      h('div', { 'data-testid': 'events-log' }, events.value.join('|')),
    ])
  },
})

const DocumentDrawerHarness = defineComponent({
  setup() {
    const events = ref<string[]>([])
    const push = (value: string) => {
      events.value = [...events.value, value]
    }

    return () => h('div', [
      h(NgbEntityEditorHeader, {
        kind: 'document',
        mode: 'drawer',
        canBack: false,
        title: 'Invoice INV-002',
        documentStatusLabel: 'Draft',
        documentStatusTone: 'neutral',
        loading: false,
        saving: false,
        pageActions: [],
        documentPrimaryActions: [{ key: 'togglePost', title: 'Post', icon: 'check' }],
        documentMoreActionGroups: [],
        onClose: () => push('close'),
        onAction: (action: string) => push(`action:${action}`),
      }),
      h('div', { 'data-testid': 'drawer-events-log' }, events.value.join('|')),
    ])
  },
})

const CatalogPageHarness = defineComponent({
  setup() {
    const events = ref<string[]>([])
    const push = (value: string) => {
      events.value = [...events.value, value]
    }

    return () => h('div', [
      h(NgbEntityEditorHeader, {
        kind: 'catalog',
        mode: 'page',
        canBack: true,
        title: 'Property record',
        subtitle: 'Portfolio setup',
        documentStatusLabel: 'Draft',
        documentStatusTone: 'neutral',
        loading: false,
        saving: false,
        pageActions: [{ key: 'copyShareLink', title: 'Share link', icon: 'share' }],
        documentPrimaryActions: [],
        documentMoreActionGroups: [],
        onBack: () => push('back'),
        onClose: () => push('close'),
        onAction: (action: string) => push(`action:${action}`),
      }),
      h('div', { 'data-testid': 'catalog-events-log' }, events.value.join('|')),
    ])
  },
})

test('renders document page header status and forwards clustered actions', async () => {
  const view = await render(DocumentPageHarness)
  const eventsLog = view.getByTestId('events-log')

  await expect.element(view.getByRole('heading', { name: 'Invoice INV-001' })).toBeVisible()
  await expect.element(view.getByText('Posted')).toBeVisible()

  await view.getByRole('button', { name: 'Back' }).click()
  await view.getByRole('button', { name: 'Primary: Save' }).click()
  await view.getByRole('button', { name: 'More: Output / Print' }).click()
  await view.getByRole('button', { name: 'Cluster close' }).click()

  expect(eventsLog.element().textContent ?? '').toBe('back|action:save|action:printDocument|close')
})

test('renders document drawer header inline and forwards action cluster events', async () => {
  const view = await render(DocumentDrawerHarness)
  const eventsLog = view.getByTestId('drawer-events-log')

  await expect.element(view.getByText('Invoice INV-002')).toBeVisible()
  await expect.element(view.getByText('Draft')).toBeVisible()

  await view.getByRole('button', { name: 'Primary: Post' }).click()
  await view.getByRole('button', { name: 'Cluster close' }).click()

  expect(eventsLog.element().textContent ?? '').toBe('action:togglePost|close')
})

test('renders catalog page actions, subtitle, and close button through page header', async () => {
  const view = await render(CatalogPageHarness)
  const eventsLog = view.getByTestId('catalog-events-log')

  await expect.element(view.getByRole('heading', { name: 'Property record' })).toBeVisible()
  await expect.element(view.getByText('Portfolio setup')).toBeVisible()

  await view.getByRole('button', { name: 'Back' }).click()
  await view.getByTitle('Share link').click()
  await view.getByTitle('Close').click()

  expect(eventsLog.element().textContent ?? '').toBe('back|action:copyShareLink|close')
})
