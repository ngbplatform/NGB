import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbIcon from '../../../../src/ngb/primitives/NgbIcon.vue'

const IconHarness = defineComponent({
  setup() {
    return () => h('div', [
      h(NgbIcon, { name: 'save', size: 22 }),
      h(NgbIcon, { name: 'more-vertical' }),
      h(NgbIcon, { name: 'selected-check' }),
    ])
  },
})

test('renders known icon glyphs and respects the requested size', async () => {
  await page.viewport(1280, 900)

  await render(IconHarness)

  const icons = Array.from(document.querySelectorAll('svg'))
  expect(icons).toHaveLength(3)
  expect(icons[0]?.getAttribute('width')).toBe('22')
  expect(icons[0]?.querySelectorAll('path').length).toBeGreaterThan(0)
  expect(icons[1]?.querySelectorAll('circle').length).toBe(3)
  expect(icons[2]?.querySelector('rect')).not.toBeNull()
})
