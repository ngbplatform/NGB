import { mount, enableAutoUnmount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { nextTick } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import NgbAiAssistantContent from '../../../../src/ngb/ai-assistant/NgbAiAssistantContent.vue'
import { getAssistantSettings, streamAssistant } from '../../../../src/ngb/ai-assistant/api'
import { useAiAssistant } from '../../../../src/ngb/ai-assistant/useAiAssistant'
import { useFeatureStore } from '../../../../src/ngb/features/useFeatureStore'
import type { AssistantEvent } from '../../../../src/ngb/ai-assistant/types'

vi.mock('../../../../src/ngb/ai-assistant/api', () => ({ getAssistantSettings: vi.fn(), streamAssistant: vi.fn() }))

enableAutoUnmount(afterEach)

beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(getAssistantSettings).mockResolvedValue({
    provider: 'Ollama', model: 'local', maxMessages: 20, maxMessageCharacters: 8000,
    maxInputBytes: 32768, maxOutputCharacters: 16384,
  })
  vi.mocked(streamAssistant).mockImplementation(async (_, publish) => {
    publish({ type: 'text', text: 'Answer' })
    publish({ type: 'done' })
  })
})

async function mountContent() {
  const pinia = createPinia()
  useFeatureStore(pinia).current = ['Mcp', 'AiAssistant'].map(code => ({ code, displayName: code, group: 'Integrations', enabled: true }))
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/reports/example.summary', component: { template: '<div />' } }],
  })
  await router.push('/reports/example.summary')
  await router.isReady()
  const wrapper = mount(NgbAiAssistantContent, { attachTo: document.body, global: { plugins: [pinia, router] } })
  return { wrapper, assistant: useAiAssistant(pinia) }
}

test('submits with Enter, preserves IME composition and allows opting out of page context', async () => {
  const { wrapper } = await mountContent()
  expect(wrapper.text()).toContain('Focus on this section')
  await wrapper.get('input[type="checkbox"]').setValue(false)
  const input = wrapper.get('textarea')
  await input.setValue('  Question  ')
  await input.trigger('keydown', { key: 'Enter', isComposing: true })
  expect(streamAssistant).not.toHaveBeenCalled()
  expect((input.element as HTMLTextAreaElement).value).toBe('  Question  ')

  await input.trigger('keydown', { key: 'Enter' })
  await vi.waitFor(() => expect(streamAssistant).toHaveBeenCalledOnce())
  expect(vi.mocked(streamAssistant).mock.calls[0]![0]).toMatchObject({ context: null, messages: [{ role: 'user', text: 'Question' }] })
  await vi.waitFor(() => expect(document.activeElement).toBe(input.element))
  expect((input.element as HTMLTextAreaElement).value).toBe('')
})

test('rejects empty and duplicate form submissions while a response is pending', async () => {
  let finish!: () => void
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, publish) => {
    await new Promise<void>(resolve => { finish = resolve })
    publish({ type: 'done' })
  })
  const { wrapper } = await mountContent()
  await wrapper.get('form').trigger('submit')
  expect(streamAssistant).not.toHaveBeenCalled()
  await wrapper.get('textarea').setValue('Question')
  await wrapper.get('form').trigger('submit')
  await vi.waitFor(() => expect(streamAssistant).toHaveBeenCalledOnce())
  await wrapper.get('textarea').setValue('Duplicate')
  await wrapper.get('form').trigger('submit')
  expect(streamAssistant).toHaveBeenCalledOnce()
  finish()
  await vi.waitFor(() => expect(wrapper.find('[role="status"]').exists()).toBe(false))
})

test('restores the question after settings fail and offers retry only after a failed turn exists', async () => {
  vi.mocked(getAssistantSettings).mockRejectedValueOnce(new Error('Settings unavailable'))
  const { wrapper } = await mountContent()
  await wrapper.get('textarea').setValue('Question')
  await wrapper.get('form').trigger('submit')
  await vi.waitFor(() => expect(wrapper.get('[role="alert"]').text()).toBe('Settings unavailable'))
  await vi.waitFor(() => expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('Question'))
  expect(wrapper.find('[role="alert"] button').exists()).toBe(false)

  vi.mocked(streamAssistant).mockImplementationOnce(async (_, publish) => {
    publish({ type: 'text', text: 'Partial answer' })
    throw new Error('Disconnected')
  })
  await wrapper.get('form').trigger('submit')
  await vi.waitFor(() => expect(wrapper.text()).toContain('This answer is incomplete.'))
  expect(wrapper.get('[role="alert"] button').text()).toBe('Retry')
})

test('leaves modified clicks to the browser and only emits local application navigation', async () => {
  const { wrapper, assistant } = await mountContent()
  await assistant.send('Question')
  await nextTick()
  const reply = wrapper.get('.assistant-markdown')
  await reply.trigger('click')
  expect(wrapper.emitted('navigate')).toBeUndefined()

  // Exercise delegated clicks, including links inserted by a host integration.
  const anchor = document.createElement('a')
  anchor.href = '/reports/example.summary?tab=details#total'
  reply.element.append(anchor)
  for (const modifier of [{ button: 1 }, { ctrlKey: true }, { metaKey: true }, { shiftKey: true }, { altKey: true }]) {
    const click = new MouseEvent('click', { bubbles: true, cancelable: true, ...modifier })
    // Prevent real browser navigation after observing the component's handling.
    anchor.addEventListener('click', event => event.preventDefault(), { once: true })
    anchor.dispatchEvent(click)
  }
  expect(wrapper.emitted('navigate')).toBeUndefined()
  anchor.click()
  expect(wrapper.emitted('navigate')).toEqual([['/reports/example.summary?tab=details#total']])

  anchor.href = 'https://external.example/report'
  anchor.click()
  expect(wrapper.emitted('navigate')).toHaveLength(1)
})

test('follows new output only while the reader remains near the bottom', async () => {
  let publish!: (event: AssistantEvent) => void
  let finish!: () => void
  vi.mocked(streamAssistant).mockImplementationOnce(async (_, callback) => {
    publish = callback
    await new Promise<void>(resolve => { finish = resolve })
    publish({ type: 'done' })
  })
  const { wrapper, assistant } = await mountContent()
  const content = wrapper.element as HTMLElement
  content.style.height = '500px'
  const scroll = wrapper.get('ol').element.parentElement!
  const pending = assistant.send('Question')
  await vi.waitFor(() => expect(streamAssistant).toHaveBeenCalledOnce())
  publish({ type: 'text', text: Array.from({ length: 100 }, (_, index) => `Paragraph ${index}`).join('\n\n') })
  await vi.waitFor(() => expect(scroll.scrollTop).toBeGreaterThan(0))

  scroll.scrollTop = 0
  scroll.dispatchEvent(new Event('scroll'))
  publish({ type: 'text', text: '\n\nMore output' })
  await nextTick()
  await nextTick()
  expect(scroll.scrollTop).toBe(0)

  scroll.scrollTop = scroll.scrollHeight
  scroll.dispatchEvent(new Event('scroll'))
  publish({ type: 'text', text: '\n\nFinal output' })
  await vi.waitFor(() => expect(scroll.scrollTop + scroll.clientHeight).toBe(scroll.scrollHeight))
  finish()
  await pending
  wrapper.unmount()
  expect(() => scroll.dispatchEvent(new Event('scroll'))).not.toThrow()
})
