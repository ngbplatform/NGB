import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbLogo from '../../../../src/ngb/site/NgbLogo.vue'

const LogoHarness = defineComponent({
  props: {
    title: {
      type: String,
      default: undefined,
    },
  },
  setup(props) {
    return () => h(NgbLogo, {
      title: props.title,
      class: 'logo-mark',
      'data-testid': 'ngb-logo',
    })
  },
})

test('trims the accessible title and forwards attrs to the root svg', async () => {
  const view = await render(LogoHarness, {
    props: {
      title: '  NGB Platform  ',
    },
  })

  await expect.element(view.getByRole('img', { name: 'NGB Platform' })).toBeVisible()
  expect(document.querySelector('svg')?.getAttribute('class')).toContain('logo-mark')
  expect(document.querySelector('svg title')?.textContent).toBe('NGB Platform')
})

test('falls back to the default title when the provided title is blank', async () => {
  const view = await render(LogoHarness, {
    props: {
      title: '   ',
    },
  })

  await expect.element(view.getByRole('img', { name: 'NGB' })).toBeVisible()
  expect(document.querySelector('svg title')?.textContent).toBe('NGB')
})
