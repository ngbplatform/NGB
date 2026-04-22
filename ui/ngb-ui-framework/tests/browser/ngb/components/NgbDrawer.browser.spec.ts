import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbDrawer from '../../../../src/ngb/components/NgbDrawer.vue'

function rect(locator: { element(): Element }): DOMRect {
  return locator.element().getBoundingClientRect()
}

function text(locator: { element(): Element }): string {
  return locator.element().textContent?.trim() ?? ''
}

function dispatchBackdropInteraction(target: HTMLElement) {
  target.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }))
  target.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }))
  target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
}

function dispatchKeyboard(target: EventTarget | null | undefined, key: string, shiftKey = false) {
  target?.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    shiftKey,
    bubbles: true,
    cancelable: true,
  }))
}

const DrawerLayoutHarness = defineComponent({
  setup() {
    return () => h(
      NgbDrawer,
      {
        open: true,
        title: 'Notifications',
        subtitle: 'Updates and alerts',
      },
      {
        actions: () => h('button', { type: 'button' }, 'Save'),
        default: () => h('div', { class: 'min-h-[1400px]', 'data-testid': 'drawer-content' }, 'Drawer body'),
        footer: () => h('button', { type: 'button' }, 'Footer action'),
      },
    )
  },
})

const HiddenHeaderDrawerHarness = defineComponent({
  setup() {
    return () => h(
      NgbDrawer,
      {
        open: true,
        title: 'Main menu',
        hideHeader: true,
        showClose: false,
        side: 'left',
      },
      {
        default: () => h('div', [
          h('div', 'Navigation shortcuts'),
          h('button', { type: 'button' }, 'Open destination'),
        ]),
      },
    )
  },
})

const DrawerCloseHarness = defineComponent({
  props: {
    blockClose: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const open = ref(true)

    async function beforeClose() {
      return !props.blockClose
    }

    return () => h('div', [
      h('div', { 'data-testid': 'drawer-open-state' }, open.value ? 'state:open' : 'state:closed'),
      h(
        NgbDrawer,
        {
          open: open.value,
          title: 'Settings',
          subtitle: 'Workspace options',
          beforeClose,
          'onUpdate:open': (value: boolean) => {
            open.value = value
          },
        },
        {
          default: () => h('div', 'Drawer content'),
        },
      ),
    ])
  },
})

const DrawerInteractiveHarness = defineComponent({
  setup() {
    const open = ref(false)
    const closeCount = ref(0)

    return () => h('div', [
      h('button', {
        type: 'button',
        'data-testid': 'drawer-opener',
        onClick: () => {
          open.value = true
        },
      }, 'Open drawer'),
      h(
        NgbDrawer,
        {
          open: open.value,
          title: 'Workspace settings',
          subtitle: 'Environment controls',
          'onUpdate:open': (value: boolean) => {
            if (!value && open.value) closeCount.value += 1
            open.value = value
          },
        },
        {
          default: () => h('button', {
            type: 'button',
            'data-testid': 'drawer-primary-action',
          }, 'Primary action'),
        },
      ),
      h('div', { 'data-testid': 'drawer-state' }, `open:${open.value};closeCount:${closeCount.value}`),
    ])
  },
})

const DrawerFocusTrapHarness = defineComponent({
  setup() {
    const open = ref(false)

    return () => h('div', [
      h('button', {
        type: 'button',
        'data-testid': 'drawer-trap-before',
      }, 'Before drawer'),
      h('button', {
        type: 'button',
        'data-testid': 'drawer-trap-opener',
        onClick: () => {
          open.value = true
        },
      }, 'Open trapped drawer'),
      h('button', {
        type: 'button',
        'data-testid': 'drawer-trap-after',
      }, 'After drawer'),
      h(
        NgbDrawer,
        {
          open: open.value,
          title: 'Keyboard drawer',
          subtitle: 'Focus trap',
          'onUpdate:open': (value: boolean) => {
            open.value = value
          },
        },
        {
          default: () => h('div', { class: 'space-y-3' }, [
            h('button', { type: 'button', 'data-testid': 'drawer-trap-first' }, 'First action'),
            h('button', { type: 'button', 'data-testid': 'drawer-trap-second' }, 'Second action'),
          ]),
          footer: () => h('button', { type: 'button', 'data-testid': 'drawer-trap-footer' }, 'Footer action'),
        },
      ),
    ])
  },
})

test('renders a right-aligned drawer panel with header, body, and footer', async () => {
  await page.viewport(1440, 900)

  const view = await render(DrawerLayoutHarness)

  const panel = view.getByTestId('drawer-panel')
  const header = view.getByTestId('drawer-header')
  const body = view.getByTestId('drawer-body')

  await expect.element(panel).toBeVisible()
  await expect.element(header).toBeVisible()
  await expect.element(body).toBeVisible()
  await expect.element(view.getByTestId('drawer-overlay')).toBeVisible()
  await expect.element(view.getByText('Notifications', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Updates and alerts', { exact: true })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Save' })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Footer action' })).toBeVisible()

  const panelRect = rect(panel)
  expect(Math.round(window.innerWidth - panelRect.right)).toBeLessThanOrEqual(1)
  expect(Math.round(panelRect.width)).toBeLessThanOrEqual(520)
  expect(Math.round(panelRect.width)).toBeGreaterThan(400)
  expect((body.element() as HTMLElement).scrollHeight).toBeGreaterThan((body.element() as HTMLElement).clientHeight)
})

test('retains an accessible dialog title when the visual header is hidden', async () => {
  await page.viewport(1280, 900)

  const view = await render(HiddenHeaderDrawerHarness)

  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()
  expect(view.getByRole('dialog', { name: 'Main menu' }).element()).toBeTruthy()
  expect(document.body.textContent ?? '').toContain('Navigation shortcuts')
  expect(document.querySelector('[data-testid="drawer-header"]')).toBeNull()
})

test('closes when the close button is pressed and beforeClose allows it', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerCloseHarness)

  expect(text(view.getByTestId('drawer-open-state'))).toBe('state:open')
  await expect.element(view.getByRole('button', { name: 'Close' })).toBeVisible()

  await view.getByRole('button', { name: 'Close' }).click()

  await vi.waitFor(() => {
    expect(text(view.getByTestId('drawer-open-state'))).toBe('state:closed')
  })
  await vi.waitFor(() => {
    expect(document.querySelector('[data-testid="drawer-panel"]')).toBeNull()
  })
})

test('stays open when beforeClose blocks the close request', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerCloseHarness, {
    props: {
      blockClose: true,
    },
  })

  expect(text(view.getByTestId('drawer-open-state'))).toBe('state:open')
  await view.getByRole('button', { name: 'Close' }).click()

  expect(text(view.getByTestId('drawer-open-state'))).toBe('state:open')
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()
})

test('closes from the overlay when beforeClose allows it', async () => {
  await page.viewport(1280, 900)

  const overlayView = await render(DrawerCloseHarness)
  await expect.element(overlayView.getByTestId('drawer-overlay')).toBeVisible()
  dispatchBackdropInteraction(overlayView.getByTestId('drawer-overlay').element() as HTMLDivElement)

  await vi.waitFor(() => {
    expect(text(overlayView.getByTestId('drawer-open-state'))).toBe('state:closed')
  })

})

test('closes from Escape when beforeClose allows it', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerCloseHarness)
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))

  await vi.waitFor(() => {
    expect(text(view.getByTestId('drawer-open-state'))).toBe('state:closed')
  })
})

test('moves focus into the drawer and restores it to the launcher after overlay close', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerInteractiveHarness)
  const opener = view.getByTestId('drawer-opener')

  ;(opener.element() as HTMLElement).focus()
  await opener.click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()

  await vi.waitFor(() => {
    expect(view.getByTestId('drawer-panel').element().contains(document.activeElement)).toBe(true)
  })

  dispatchBackdropInteraction(view.getByTestId('drawer-overlay').element() as HTMLDivElement)

  await vi.waitFor(() => {
    expect(text(view.getByTestId('drawer-state'))).toBe('open:false;closeCount:1')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(opener.element())
  })
})

test('restores launcher focus after Escape closes an interactive drawer', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerInteractiveHarness)
  const opener = view.getByTestId('drawer-opener')

  ;(opener.element() as HTMLElement).focus()
  await opener.click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))

  await vi.waitFor(() => {
    expect(text(view.getByTestId('drawer-state'))).toBe('open:false;closeCount:1')
  })
  await vi.waitFor(() => {
    expect(document.activeElement).toBe(opener.element())
  })
})

test('keeps keyboard tab navigation trapped inside the drawer panel', async () => {
  await page.viewport(1280, 900)

  const view = await render(DrawerFocusTrapHarness)
  const opener = view.getByTestId('drawer-trap-opener')

  ;(opener.element() as HTMLElement).focus()
  await opener.click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()

  const panel = view.getByTestId('drawer-panel').element() as HTMLElement

  for (const step of [
    { key: 'Tab', shiftKey: false },
    { key: 'Tab', shiftKey: false },
    { key: 'Tab', shiftKey: false },
    { key: 'Tab', shiftKey: false },
    { key: 'Tab', shiftKey: true },
    { key: 'Tab', shiftKey: true },
  ]) {
    dispatchKeyboard(document.activeElement, step.key, step.shiftKey)
    await vi.waitFor(() => {
      expect(panel.contains(document.activeElement)).toBe(true)
    })
  }

  expect(panel.contains(document.activeElement)).toBe(true)
  expect(document.activeElement).not.toBe(view.getByTestId('drawer-trap-before').element())
  expect(document.activeElement).not.toBe(view.getByTestId('drawer-trap-after').element())
})
