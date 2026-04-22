import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

import NgbFormSection from '../../../../../src/ngb/components/forms/NgbFormSection.vue'

const FormSectionHarness = defineComponent({
  props: {
    title: {
      type: String,
      default: undefined,
    },
    description: {
      type: String,
      default: undefined,
    },
  },
  setup(props) {
    return () => h(
      NgbFormSection,
      {
        title: props.title,
        description: props.description,
      },
      {
        default: () => h('input', { 'aria-label': 'Section field' }),
      },
    )
  },
})

test('renders title, description, and section content when props are provided', async () => {
  const view = await render(FormSectionHarness, {
    props: {
      title: 'General',
      description: 'Core document fields',
    },
  })

  await expect.element(view.getByText('General', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Core document fields', { exact: true })).toBeVisible()
  await expect.element(view.getByRole('textbox', { name: 'Section field' })).toBeVisible()
})

test('omits optional heading blocks when title and description are missing', async () => {
  const view = await render(FormSectionHarness)

  await expect.element(view.getByRole('textbox', { name: 'Section field' })).toBeVisible()
  expect(document.querySelector('.text-sm.font-semibold.text-ngb-text')).toBeNull()
  expect(document.querySelector('.text-xs.text-ngb-muted.mt-1')).toBeNull()
})
