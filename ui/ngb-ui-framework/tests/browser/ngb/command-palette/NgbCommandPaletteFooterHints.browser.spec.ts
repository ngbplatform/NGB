import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbCommandPaletteFooterHints from '../../../../src/ngb/command-palette/NgbCommandPaletteFooterHints.vue'

const FooterHarness = defineComponent({
  props: {
    primaryModifier: { type: String, required: true },
  },
  setup(props) {
    return () => h(NgbCommandPaletteFooterHints, props)
  },
})

test('renders navigation, open-in-new-tab, and focus shortcuts', async () => {
  await page.viewport(1024, 800)

  const view = await render(FooterHarness, {
    props: {
      primaryModifier: 'Cmd',
    },
  })

  await expect.element(view.getByText('Navigate')).toBeVisible()
  await expect.element(view.getByText('Open')).toBeVisible()
  await expect.element(view.getByText('New tab')).toBeVisible()
  await expect.element(view.getByText('Close')).toBeVisible()
  await expect.element(view.getByText('Focus')).toBeVisible()

  const shortcuts = document.body.textContent ?? ''
  expect(shortcuts).toContain('Cmd')
  expect(shortcuts).toContain('K')
  expect(shortcuts).toContain('Esc')
})
