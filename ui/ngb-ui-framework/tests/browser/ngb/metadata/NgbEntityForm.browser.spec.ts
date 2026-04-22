import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import {
  StubEntityFormFieldsBlock,
  StubFormLayout,
  StubFormSection,
} from './stubs'

vi.mock('../../../../src/ngb/components/forms/NgbFormLayout.vue', () => ({
  default: StubFormLayout,
}))

vi.mock('../../../../src/ngb/components/forms/NgbFormSection.vue', () => ({
  default: StubFormSection,
}))

vi.mock('../../../../src/ngb/metadata/NgbEntityFormFieldsBlock.vue', () => ({
  default: StubEntityFormFieldsBlock,
}))

import NgbEntityForm from '../../../../src/ngb/metadata/NgbEntityForm.vue'

const form = {
  sections: [
    {
      title: 'Main',
      rows: [
        {
          fields: [
            {
              key: 'display',
              label: 'Display',
              dataType: 'String',
              uiControl: 1,
              isRequired: false,
              isReadOnly: false,
            },
            {
              key: 'customer_id',
              label: 'Customer',
              dataType: 'String',
              uiControl: 1,
              isRequired: false,
              isReadOnly: false,
            },
          ],
        },
      ],
    },
    {
      title: 'Details',
      rows: [
        {
          fields: [
            {
              key: 'amount',
              label: 'Amount',
              dataType: 'Decimal',
              uiControl: 3,
              isRequired: false,
              isReadOnly: false,
            },
          ],
        },
      ],
    },
  ],
}

const SectionsHarness = defineComponent({
  setup() {
    const formRef = ref<InstanceType<typeof NgbEntityForm> | null>(null)
    const focusResult = ref('pending')
    const firstErrorResult = ref('pending')

    return () => h('div', [
      h(NgbEntityForm, {
        ref: formRef,
        form,
        model: {
          display: 'Invoice INV-001',
          customer_id: 'customer-1',
          amount: 1250,
        },
        entityTypeCode: 'pm.invoice',
        status: 2,
        forceReadonly: true,
        presentation: 'sections',
      }),
      h('button', {
        type: 'button',
        onClick: () => {
          focusResult.value = String(formRef.value?.focusField('customer_id') ?? false)
        },
      }, 'Run focus'),
      h('button', {
        type: 'button',
        onClick: () => {
          firstErrorResult.value = String(formRef.value?.focusFirstError(['missing', 'amount']) ?? false)
        },
      }, 'Run first error'),
      h('div', `focus:${focusResult.value}`),
      h('div', `first-error:${firstErrorResult.value}`),
    ])
  },
})

const FlatHarness = defineComponent({
  setup() {
    return () => h(NgbEntityForm, {
      form,
      model: {
        custom_display: 'Custom display',
      },
      entityTypeCode: 'pm.property',
      presentation: 'flat',
      behavior: {
        findDisplayField: () => ({
          key: 'customer_id',
          label: 'Customer',
          dataType: 'String',
          uiControl: 1,
          isRequired: false,
          isReadOnly: false,
        }),
      },
    })
  },
})

test('renders sectioned layouts and exposes focus helpers through validation targets', async () => {
  const view = await render(SectionsHarness)

  await expect.element(view.getByTestId('form-layout')).toBeVisible()
  await expect.element(view.getByText('Main')).toBeVisible()
  await expect.element(view.getByText('Details')).toBeVisible()
  await expect.element(view.getByText('display-field:display')).toBeVisible()
  expect(document.activeElement?.getAttribute('aria-label')).not.toBe('field-customer_id')

  await view.getByRole('button', { name: 'Run focus' }).click()
  await view.getByRole('button', { name: 'Run first error' }).click()

  await expect.element(view.getByText('focus:true')).toBeVisible()
  await expect.element(view.getByText('first-error:true')).toBeVisible()
  expect(document.activeElement?.getAttribute('aria-label')).toBe('field-amount')
})

test('renders flat presentation as a single fields block and honors behavior display-field override', async () => {
  const view = await render(FlatHarness)

  expect(document.querySelector('[data-testid="form-layout"]')).toBeNull()
  await expect.element(view.getByText('display-field:customer_id')).toBeVisible()
  await expect.element(view.getByText('rows:2')).toBeVisible()
})
