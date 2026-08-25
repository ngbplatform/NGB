import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbIcon from '../../../../src/ngb/primitives/NgbIcon.vue'
import { NGB_ICON_NAMES } from '../../../../src/ngb/primitives/iconNames'

const IconHarness = defineComponent({
  setup() {
    return () => h('div', NGB_ICON_NAMES.map((name) => h('div', {
      key: name,
      'data-testid': `icon-${name}`,
    }, [h(NgbIcon, { name, size: name === 'save' ? 22 : undefined })])))
  },
})

test('renders known icon glyphs and respects the requested size', async () => {
  await page.viewport(1280, 900)

  await render(IconHarness)

  const icons = Array.from(document.querySelectorAll('svg'))
  expect(icons).toHaveLength(NGB_ICON_NAMES.length)

  for (const name of NGB_ICON_NAMES) {
    const icon = document.querySelector(`[data-testid="icon-${name}"] svg`)
    expect(icon, `${name} must render an SVG glyph`).not.toBeNull()
    expect(icon?.childElementCount, `${name} must not render an empty SVG`).toBeGreaterThan(0)
  }

  expect(document.querySelector('[data-testid="icon-save"] svg')?.getAttribute('width')).toBe('22')
  expect(document.querySelectorAll('[data-testid="icon-more-vertical"] circle')).toHaveLength(3)
  expect(document.querySelector('[data-testid="icon-selected-check"] rect')).not.toBeNull()
})
