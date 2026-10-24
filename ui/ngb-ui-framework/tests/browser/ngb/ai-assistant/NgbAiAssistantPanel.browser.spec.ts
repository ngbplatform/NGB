import { mount, enableAutoUnmount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { nextTick } from 'vue'
import { page } from 'vitest/browser'
import { afterEach, expect, test, vi } from 'vitest'
import NgbAiAssistantPanel from '../../../../src/ngb/ai-assistant/NgbAiAssistantPanel.vue'
import NgbAiAssistantContent from '../../../../src/ngb/ai-assistant/NgbAiAssistantContent.vue'
import NgbDrawer from '../../../../src/ngb/components/NgbDrawer.vue'

enableAutoUnmount(afterEach)
afterEach(() => vi.restoreAllMocks())

function mountPanel(open = true) {
  return mount(NgbAiAssistantPanel, {
    props: { open },
    attachTo: document.body,
    global: { plugins: [createPinia()] },
  })
}

test('updates width bounds with the viewport and supports accelerated keyboard resizing', async () => {
  await page.viewport(1440, 900)
  const wrapper = mountPanel(false)
  await wrapper.setProps({ open: true })
  const handle = wrapper.get('[role="separator"]')
  await handle.trigger('keydown', { key: 'ArrowLeft', shiftKey: true })
  expect(handle.attributes('aria-valuenow')).toBe('490')
  await handle.trigger('keydown', { key: 'ArrowRight', shiftKey: true })
  expect(handle.attributes('aria-valuenow')).toBe('440')
  const unrelatedKey = new KeyboardEvent('keydown', { key: 'a', cancelable: true, bubbles: true })
  handle.element.dispatchEvent(unrelatedKey)
  expect(unrelatedKey.defaultPrevented).toBe(false)
  expect(handle.attributes('aria-valuenow')).toBe('440')

  await handle.trigger('keydown', { key: 'End' })
  await page.viewport(1100, 900)
  await vi.waitFor(() => expect(handle.attributes('aria-valuemax')).toBe('460'))
  expect(handle.attributes('aria-valuenow')).toBe('460')
  await page.viewport(390, 844)
  await vi.waitFor(() => expect(wrapper.findComponent(NgbDrawer).exists()).toBe(true))
  expect(wrapper.find('[role="separator"]').exists()).toBe(false)
})

test('forwards mobile navigation and drawer dismissal and restores focus after closing', async () => {
  await page.viewport(390, 844)
  const opener = document.createElement('button')
  document.body.append(opener)
  opener.focus()
  const wrapper = mountPanel()
  try {
    const drawer = wrapper.getComponent(NgbDrawer)
    await vi.waitFor(() => expect(wrapper.findComponent(NgbAiAssistantContent).exists()).toBe(true))
    wrapper.getComponent(NgbAiAssistantContent).vm.$emit('navigate', '/reports/example.summary')
    expect(wrapper.emitted('navigate')).toEqual([['/reports/example.summary']])
    drawer.vm.$emit('after-leave')
    expect(document.activeElement).not.toBe(opener)
    drawer.vm.$emit('update:open', false)
    expect(wrapper.emitted('update:open')).toEqual([[false]])
    await wrapper.setProps({ open: false })
    drawer.vm.$emit('after-leave')
    expect(document.activeElement).toBe(opener)
  } finally {
    wrapper.unmount()
    opener.remove()
  }
})

test('ignores secondary pointers and restores prefixed selection styles after capture is lost', async () => {
  await page.viewport(1440, 900)
  vi.spyOn(CSS, 'supports').mockReturnValue(false)
  const wrapper = mountPanel()
  const handle = wrapper.get('[role="separator"]').element as HTMLElement
  const capture = vi.spyOn(handle, 'setPointerCapture').mockImplementation(() => {})
  vi.spyOn(handle, 'hasPointerCapture').mockReturnValue(false)
  const release = vi.spyOn(handle, 'releasePointerCapture').mockImplementation(() => {})
  const initialStyle = document.body.getAttribute('style')
  try {
    document.body.style.setProperty('-webkit-user-select', 'text', 'important')
    document.body.style.cursor = 'crosshair'
    handle.dispatchEvent(new PointerEvent('pointerdown', { button: 2, isPrimary: true }))
    handle.dispatchEvent(new PointerEvent('pointerdown', { button: 0, isPrimary: false }))
    expect(capture).not.toHaveBeenCalled()
    expect(document.body.style.cursor).toBe('crosshair')

    handle.dispatchEvent(new PointerEvent('pointerdown', { button: 0, isPrimary: true, pointerId: 7, clientX: 1000 }))
    expect(document.body.style.getPropertyValue('-webkit-user-select')).toBe('none')
    handle.dispatchEvent(new PointerEvent('lostpointercapture', { pointerId: 7 }))
    expect(release).not.toHaveBeenCalled()
    expect(document.body.style.cursor).toBe('crosshair')
    expect(document.body.style.getPropertyValue('-webkit-user-select')).toBe('text')
    expect(document.body.style.getPropertyPriority('-webkit-user-select')).toBe('important')
  } finally {
    wrapper.unmount()
    if (initialStyle === null) document.body.removeAttribute('style')
    else document.body.setAttribute('style', initialStyle)
  }
})

test('tolerates a queued pointer sequence when the desktop panel has just been replaced', async () => {
  await page.viewport(1440, 900)
  const wrapper = mountPanel()
  const handle = wrapper.get('[role="separator"]').element as HTMLElement
  vi.spyOn(handle, 'setPointerCapture').mockImplementation(() => {})
  vi.spyOn(handle, 'hasPointerCapture').mockReturnValue(false)
  await page.viewport(390, 844)
  await vi.waitFor(() => expect(wrapper.findComponent(NgbDrawer).exists()).toBe(true))

  handle.dispatchEvent(new PointerEvent('pointerdown', { button: 0, isPrimary: true, pointerId: 9, clientX: 400 }))
  handle.dispatchEvent(new PointerEvent('pointermove', { pointerId: 9, clientX: 350 }))
  handle.dispatchEvent(new PointerEvent('pointercancel', { pointerId: 9 }))
  await nextTick()
  expect(document.body.style.cursor).toBe('')

  await page.viewport(1440, 900)
  await vi.waitFor(() => expect(wrapper.find('[role="separator"]').exists()).toBe(true))
  expect(wrapper.get('[role="separator"]').attributes('aria-valuenow')).toBe('320')
})
