import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, nextTick, ref } from 'vue'

vi.mock('@headlessui/vue', () => {
  const passthrough = defineComponent({
    setup(_props, { slots }) {
      return () => h('div', slots.default?.())
    },
  })

  const transitionRoot = defineComponent({
    emits: ['afterLeave'],
    setup(_props, { emit, slots }) {
      return () => h('div', [
        slots.default?.(),
        h('button', {
          type: 'button',
          onClick: () => emit('afterLeave'),
        }, 'Complete drawer leave'),
      ])
    },
  })

  return {
    Dialog: passthrough,
    DialogPanel: passthrough,
    DialogTitle: passthrough,
    TransitionChild: passthrough,
    TransitionRoot: transitionRoot,
  }
})

import NgbDrawer from '../../../../src/ngb/components/NgbDrawer.vue'

const DrawerRaceHarness = defineComponent({
  setup() {
    const open = ref(false)

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => {
          open.value = true
        },
      }, 'Reopen drawer'),
      h('button', {
        type: 'button',
        onClick: () => {
          open.value = false
        },
      }, 'Close drawer'),
      h(NgbDrawer, { open: open.value, title: 'Race-safe drawer' }),
      h('div', { 'data-testid': 'drawer-race-state' }, `open:${String(open.value)}`),
    ])
  },
})

test('does not restore stale focus while the drawer is open or reopened before the timer runs', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerRaceHarness)
  const completeLeave = view.getByRole('button', { name: 'Complete drawer leave' }).element() as HTMLButtonElement
  const reopen = view.getByRole('button', { name: 'Reopen drawer' }).element() as HTMLButtonElement

  reopen.click()
  await nextTick()
  completeLeave.click()

  ;(view.getByRole('button', { name: 'Close drawer' }).element() as HTMLButtonElement).click()
  await nextTick()
  completeLeave.click()
  reopen.click()

  await new Promise((resolve) => window.setTimeout(resolve, 1))
  await expect.element(view.getByTestId('drawer-race-state')).toHaveTextContent('open:true')
})
