import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbModalShell from '../../../../src/ngb/components/NgbModalShell.vue'

function modalBackdrop(): HTMLDivElement {
  const backdrop = Array.from(document.querySelectorAll('div.fixed.inset-0')).find((node) => node.childElementCount === 0)
  expect(backdrop).toBeTruthy()
  return backdrop as HTMLDivElement
}

function dispatchBackdropInteraction(target: HTMLElement) {
  target.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }))
  target.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }))
  target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
}

const ModalShellHarness = defineComponent({
  setup() {
    const open = ref(true)
    const closeCount = ref(0)

    return () => h('div', [
      h(NgbModalShell, {
        open: open.value,
        maxWidthClass: 'max-w-[320px]',
        onClose: () => {
          closeCount.value += 1
          open.value = false
        },
      }, {
        default: () => h('div', [
          h('button', { type: 'button' }, 'Focusable action'),
          h('div', { 'data-testid': 'modal-content' }, 'Panel body'),
        ]),
      }),
      h('div', { 'data-testid': 'modal-state' }, `open:${open.value};closeCount:${closeCount.value}`),
    ])
  },
})

const InteractiveModalShellHarness = defineComponent({
  setup() {
    const open = ref(false)
    const closeCount = ref(0)

    return () => h('div', [
      h('button', {
        type: 'button',
        'data-testid': 'modal-opener',
        onClick: () => {
          open.value = true
        },
      }, 'Open modal'),
      h(NgbModalShell, {
        open: open.value,
        onClose: () => {
          closeCount.value += 1
          open.value = false
        },
      }, {
        default: () => h('div', [
          h('button', { type: 'button', 'data-testid': 'modal-primary-action' }, 'Focusable action'),
        ]),
      }),
      h('div', { 'data-testid': 'modal-state' }, `open:${open.value};closeCount:${closeCount.value}`),
    ])
  },
})

test('renders the modal shell, applies max-width classes, and closes from the backdrop', async () => {
  await page.viewport(1280, 900)

  const view = await render(ModalShellHarness)

  await expect.element(view.getByTestId('modal-content')).toBeVisible()
  expect(Array.from(document.querySelectorAll('div')).some((node) => node.className.includes('max-w-[320px]'))).toBe(true)

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))

  await vi.waitFor(() => {
    expect(view.getByTestId('modal-state').element().textContent).toBe('open:false;closeCount:1')
  })
  await vi.waitFor(() => {
    expect(document.body.textContent?.includes('Panel body')).toBe(false)
  })
})

test('closes from the backdrop after being opened by an external launcher button', async () => {
  await page.viewport(1280, 900)

  const view = await render(InteractiveModalShellHarness)
  const opener = view.getByTestId('modal-opener')

  ;(opener.element() as HTMLElement).focus()
  await opener.click()
  await expect.element(view.getByTestId('modal-primary-action')).toBeVisible()
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(view.getByTestId('modal-primary-action').element())
  })

  dispatchBackdropInteraction(modalBackdrop())

  await vi.waitFor(() => {
    expect(view.getByTestId('modal-state').element().textContent).toBe('open:false;closeCount:1')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(opener.element())
  })
})

test('restores launcher focus after Escape closes the modal', async () => {
  await page.viewport(1280, 900)

  const view = await render(InteractiveModalShellHarness)
  const opener = view.getByTestId('modal-opener')

  ;(opener.element() as HTMLElement).focus()
  await opener.click()
  await expect.element(view.getByTestId('modal-primary-action')).toBeVisible()

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))

  await vi.waitFor(() => {
    expect(view.getByTestId('modal-state').element().textContent).toBe('open:false;closeCount:1')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(opener.element())
  })
})
