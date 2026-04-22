import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, onErrorCaptured, ref } from 'vue'

type BootstrapScenario = {
  name: string
  expectedMessage: string
  loadInvoke: () => Promise<() => void>
}

async function renderBoundaryHarness(invoke: () => void) {
  const Child = defineComponent({
    setup() {
      invoke()
      return {}
    },
    render() {
      return h('div', { 'data-testid': 'bootstrap-ready' }, 'ready')
    },
  })

  const Boundary = defineComponent({
    setup() {
      const error = ref('none')

      onErrorCaptured((cause) => {
        error.value = cause instanceof Error ? cause.message : String(cause)
        return false
      })

      return () => h('div', [
        h('div', { 'data-testid': 'bootstrap-error' }, error.value),
        error.value === 'none' ? h(Child) : null,
      ])
    },
  })

  return await render(Boundary)
}

const scenarios: BootstrapScenario[] = [
  {
    name: 'command palette bootstrap guard',
    expectedMessage: 'NGB command palette is not configured. Call configureNgbCommandPalette(...) during app bootstrap.',
    loadInvoke: async () => {
      const config = await import('../../../../src/ngb/command-palette/config')
      return () => {
        config.getConfiguredNgbCommandPalette()
      }
    },
  },
  {
    name: 'lookup bootstrap guard',
    expectedMessage: 'NGB lookup framework is not configured. Call configureNgbLookup(...) during app bootstrap.',
    loadInvoke: async () => {
      const config = await import('../../../../src/ngb/lookup/config')
      return () => {
        config.getConfiguredNgbLookup()
      }
    },
  },
  {
    name: 'metadata bootstrap guard',
    expectedMessage: 'NGB metadata framework is not configured. Call configureNgbMetadata(...) during app bootstrap.',
    loadInvoke: async () => {
      const config = await import('../../../../src/ngb/metadata/config')
      return () => {
        config.getConfiguredNgbMetadata()
      }
    },
  },
  {
    name: 'reporting bootstrap guard',
    expectedMessage: 'NGB reporting framework is not configured. Call configureNgbReporting(...) during app bootstrap.',
    loadInvoke: async () => {
      const config = await import('../../../../src/ngb/reporting/config')
      return () => {
        config.getConfiguredNgbReporting()
      }
    },
  },
  {
    name: 'editor bootstrap guard',
    expectedMessage: 'NGB editor framework is not configured. Call configureNgbEditor(...) during app bootstrap.',
    loadInvoke: async () => {
      const config = await import('../../../../src/ngb/editor/config')
      return () => {
        config.getConfiguredNgbEditor()
      }
    },
  },
]

for (const scenario of scenarios) {
  test(`surfaces the ${scenario.name} error through a browser-visible boundary`, async () => {
    await page.viewport(1280, 900)

    vi.resetModules()
    const invoke = await scenario.loadInvoke()
    const view = await renderBoundaryHarness(invoke)

    await expect.element(view.getByTestId('bootstrap-error')).toHaveTextContent(scenario.expectedMessage)
    expect(document.body.textContent?.includes('ready')).toBe(false)
  })
}
