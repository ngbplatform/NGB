import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  routerPush: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: mocks.routerPush,
  }),
}))

vi.mock('ngb-ui-framework', async () => {
  const { defineComponent, h } = await import('vue')

  const StubBadge = defineComponent({
    name: 'StubBadge',
    props: {
      tone: { type: String, default: 'neutral' },
    },
    setup(props, { slots }) {
      return () => h('span', { 'data-testid': `badge-${props.tone}` }, slots.default?.())
    },
  })

  const StubIcon = defineComponent({
    name: 'StubIcon',
    props: {
      name: { type: String, required: true },
    },
    setup(props) {
      return () => h('span', { 'data-testid': `icon-${props.name}` })
    },
  })

  const StubPageHeader = defineComponent({
    name: 'StubPageHeader',
    props: {
      title: { type: String, required: true },
    },
    setup(props, { slots }) {
      return () => h('header', { 'data-testid': 'page-header' }, [
        h('h1', props.title),
        h('div', { 'data-testid': 'page-header-secondary' }, slots.secondary?.()),
      ])
    },
  })

  return {
    NgbBadge: StubBadge,
    NgbIcon: StubIcon,
    NgbPageHeader: StubPageHeader,
  }
})

import HomePage from '../../../src/pages/HomePage.vue'

beforeEach(() => {
  mocks.routerPush.mockReset()
})

test('renders the agency billing control-center content', async () => {
  const view = await render(HomePage)

  await expect.element(view.getByTestId('agency-billing-home-page')).toBeVisible()
  await expect.element(view.getByText('Agency Billing control center')).toBeVisible()
  await expect.element(view.getByText('Run time capture, billing, and collection in one workspace.')).toBeVisible()
  await expect.element(view.getByText('Capture Timesheet')).toBeVisible()
  await expect.element(view.getByText('Draft Sales Invoice')).toBeVisible()
  await expect.element(view.getByText('Record Customer Payment')).toBeVisible()
  await expect.element(view.getByText('Revenue & Receivables')).toBeVisible()
})

test('routes launchpad and focus cards through the router', async () => {
  const view = await render(HomePage)

  await view.getByText('Capture Timesheet').click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/documents/ab.timesheet/new')

  await view.getByText('Revenue & Receivables').click()
  expect(mocks.routerPush).toHaveBeenCalledWith('/reports/ab.ar_aging')
})
