import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbBadge from '../../../../src/ngb/primitives/NgbBadge.vue'

const BadgeHarness = defineComponent({
  setup() {
    return () => h('div', [
      h(NgbBadge, { tone: 'neutral' }, () => 'Neutral'),
      h(NgbBadge, { tone: 'success' }, () => 'Success'),
      h(NgbBadge, { tone: 'danger' }, () => 'Danger'),
    ])
  },
})

test('renders badge content for different tones', async () => {
  await page.viewport(1280, 900)

  await render(BadgeHarness)

  const badges = Array.from(document.querySelectorAll('span.inline-flex'))
  expect(badges).toHaveLength(3)
  expect(badges[0]?.className).toContain('bg-ngb-neutral-subtle')
  expect(badges[1]?.className).toContain('text-ngb-success')
  expect(badges[2]?.className).toContain('text-ngb-danger')
  expect(document.body.textContent?.includes('Neutral')).toBe(true)
  expect(document.body.textContent?.includes('Success')).toBe(true)
  expect(document.body.textContent?.includes('Danger')).toBe(true)
})
