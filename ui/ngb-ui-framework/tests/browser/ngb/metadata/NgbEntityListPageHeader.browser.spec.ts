import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import { StubIcon, StubPageHeader } from './stubs'

vi.mock('../../../../src/ngb/site/NgbPageHeader.vue', () => ({
  default: StubPageHeader,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

import NgbEntityListPageHeader from '../../../../src/ngb/metadata/NgbEntityListPageHeader.vue'

function buttonByTitle(title: string): HTMLButtonElement {
  const button = document.querySelector(`button[title="${title}"]`) as HTMLButtonElement | null
  if (!button) throw new Error(`Button not found for title: ${title}`)
  return button
}

const HeaderHarness = defineComponent({
  props: {
    disableCreate: {
      type: Boolean,
      default: false,
    },
    showFilter: {
      type: Boolean,
      default: true,
    },
  },
  setup(props) {
    const events = ref<string[]>([])

    return () => h('div', [
      h(NgbEntityListPageHeader, {
        title: 'Invoices',
        canBack: true,
        itemsCount: 12,
        total: 42,
        loading: false,
        disableCreate: props.disableCreate,
        showFilter: props.showFilter,
        filterActive: true,
        disablePrev: false,
        disableNext: true,
        onBack: () => events.value.push('back'),
        onRefresh: () => events.value.push('refresh'),
        onCreate: () => events.value.push('create'),
        onFilter: () => events.value.push('filter'),
        onPrev: () => events.value.push('prev'),
        onNext: () => events.value.push('next'),
      }, {
        filters: () => h('span', { 'data-testid': 'custom-filter-slot' }, 'Custom filters'),
      }),
      h('div', { 'data-testid': 'header-events' }, events.value.join('|')),
    ])
  },
})

test('renders counts, filter slot, and forwards page header actions', async () => {
  await page.viewport(1280, 900)

  const view = await render(HeaderHarness)

  await expect.element(view.getByTestId('stub-page-header')).toBeVisible()
  await expect.element(view.getByText('header-title:Invoices')).toBeVisible()
  await expect.element(view.getByText('12 / 42')).toBeVisible()
  await expect.element(view.getByTestId('custom-filter-slot')).toBeVisible()
  await expect.element(view.getByTestId('icon-plus')).toBeVisible()
  await expect.element(view.getByTestId('icon-filter')).toBeVisible()
  await expect.element(view.getByTestId('icon-refresh')).toBeVisible()
  await expect.element(view.getByTestId('icon-arrow-left')).toBeVisible()
  await expect.element(view.getByTestId('icon-arrow-right')).toBeVisible()

  await view.getByRole('button', { name: 'Header back' }).click()
  buttonByTitle('Create').click()
  buttonByTitle('Filter').click()
  buttonByTitle('Refresh').click()
  buttonByTitle('Previous').click()

  expect(buttonByTitle('Next').disabled).toBe(true)
  await expect.element(view.getByTestId('header-events')).toHaveTextContent('back|create|filter|refresh|prev')
})

test('respects disabled create and hidden filter mode', async () => {
  await page.viewport(1280, 900)

  const view = await render(HeaderHarness, {
    props: {
      disableCreate: true,
      showFilter: false,
    },
  })

  expect(buttonByTitle('Create').disabled).toBe(true)
  expect(document.querySelector('button[title="Filter"]')).toBeNull()
})
