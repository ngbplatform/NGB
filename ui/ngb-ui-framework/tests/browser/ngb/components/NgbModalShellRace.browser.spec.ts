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
        }, 'Complete modal leave'),
      ])
    },
  })

  return {
    Dialog: passthrough,
    DialogPanel: passthrough,
    TransitionChild: passthrough,
    TransitionRoot: transitionRoot,
  }
})

import NgbModalShell from '../../../../src/ngb/components/NgbModalShell.vue'

const ModalRaceHarness = defineComponent({
  setup() {
    const open = ref(false)

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => {
          open.value = true
        },
      }, 'Reopen modal'),
      h('button', {
        type: 'button',
        onClick: () => {
          open.value = false
        },
      }, 'Close modal'),
      h(NgbModalShell, { open: open.value }),
      h('div', { 'data-testid': 'race-state' }, `open:${String(open.value)}`),
    ])
  },
})

test('does not restore stale focus while the modal is open or reopened before the timer runs', async () => {
  await page.viewport(1280, 900)

  const view = await render(ModalRaceHarness)
  const completeLeave = view.getByRole('button', { name: 'Complete modal leave' }).element() as HTMLButtonElement
  const reopen = view.getByRole('button', { name: 'Reopen modal' }).element() as HTMLButtonElement

  reopen.click()
  await nextTick()
  completeLeave.click()

  ;(view.getByRole('button', { name: 'Close modal' }).element() as HTMLButtonElement).click()
  await nextTick()
  completeLeave.click()
  reopen.click()

  await new Promise((resolve) => window.setTimeout(resolve, 1))
  await expect.element(view.getByTestId('race-state')).toHaveTextContent('open:true')
})
