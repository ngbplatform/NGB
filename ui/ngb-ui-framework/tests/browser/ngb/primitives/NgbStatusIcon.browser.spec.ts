import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbStatusIcon from '../../../../src/ngb/primitives/NgbStatusIcon.vue'

const StatusIconHarness = defineComponent({
  setup() {
    return () => h('div', [
      h(NgbStatusIcon, { status: 'active' }),
      h(NgbStatusIcon, { status: 'posted' }),
      h(NgbStatusIcon, { status: 'marked', title: 'Custom marked title' }),
    ])
  },
})

test('renders default and custom titles with status-specific styling', async () => {
  await page.viewport(1280, 900)

  await render(StatusIconHarness)

  const wrappers = Array.from(document.querySelectorAll('span.inline-flex'))
  expect(wrappers).toHaveLength(3)
  expect(wrappers[0]?.getAttribute('title')).toBe('Active')
  expect(wrappers[0]?.className).toContain('text-ngb-muted')
  expect(wrappers[1]?.getAttribute('title')).toBe('Posted')
  expect(wrappers[1]?.className).toContain('text-ngb-success')
  expect(wrappers[2]?.getAttribute('title')).toBe('Custom marked title')
  expect(wrappers[2]?.className).toContain('text-ngb-danger')

  const postedSvg = wrappers[1]?.querySelector('svg')
  expect(postedSvg?.querySelectorAll('path').length).toBeGreaterThan(0)
})
