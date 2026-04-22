import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbPickerNavButton from '../../../../src/ngb/primitives/NgbPickerNavButton.vue'

const PickerNavButtonHarness = defineComponent({
  props: {
    direction: {
      type: String,
      required: true,
    },
  },
  setup(props) {
    const clicks = ref(0)

    return () => h('div', [
      h(NgbPickerNavButton, {
        direction: props.direction,
        onClick: () => {
          clicks.value += 1
        },
      }),
      h('div', { 'data-testid': 'click-count' }, `count:${clicks.value}`),
    ])
  },
})

test('renders the previous chevron and emits clicks', async () => {
  const view = await render(PickerNavButtonHarness, {
    props: {
      direction: 'prev',
    },
  })

  await view.getByRole('button').click()

  await expect.element(view.getByTestId('click-count')).toHaveTextContent('count:1')
  expect(document.querySelector('path')?.getAttribute('d')).toBe('M15 18l-6-6 6-6')
})

test('renders the next chevron when configured for forward navigation', async () => {
  const view = await render(PickerNavButtonHarness, {
    props: {
      direction: 'next',
    },
  })

  await expect.element(view.getByRole('button')).toBeVisible()
  expect(document.querySelector('path')?.getAttribute('d')).toBe('M9 6l6 6-6 6')
})
