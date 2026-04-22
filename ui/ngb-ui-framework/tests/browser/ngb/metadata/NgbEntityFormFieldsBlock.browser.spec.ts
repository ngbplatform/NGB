import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, reactive } from 'vue'

import {
  StubFormRow,
  StubMetadataFieldRenderer,
} from './stubs'

vi.mock('../../../../src/ngb/components/forms/NgbFormRow.vue', () => ({
  default: StubFormRow,
}))

vi.mock('../../../../src/ngb/metadata/NgbMetadataFieldRenderer.vue', () => ({
  default: StubMetadataFieldRenderer,
}))

import NgbEntityFormFieldsBlock from '../../../../src/ngb/metadata/NgbEntityFormFieldsBlock.vue'

const baseDisplayField = {
  key: 'display',
  label: 'Display',
  dataType: 'String',
  uiControl: 1,
  isRequired: false,
  isReadOnly: false,
}

const baseRows = [
  {
    fields: [
      baseDisplayField,
      {
        key: 'customer_id',
        label: 'Customer',
        dataType: 'Guid',
        uiControl: 1,
        isRequired: true,
        isReadOnly: false,
      },
      {
        key: 'internal_note',
        label: 'Internal note',
        dataType: 'String',
        uiControl: 1,
        isRequired: false,
        isReadOnly: false,
        helpText: 'Visible only to admins',
      },
      {
        key: 'hidden_field',
        label: 'Hidden field',
        dataType: 'String',
        uiControl: 1,
        isRequired: false,
        isReadOnly: false,
      },
      {
        key: 'posted_only',
        label: 'Posted only',
        dataType: 'String',
        uiControl: 1,
        isRequired: false,
        isReadOnly: false,
        readOnlyWhenStatusIn: [2],
      },
    ],
  },
]

const FieldsBlockHarness = defineComponent({
  props: {
    forceReadonly: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const model = reactive<Record<string, unknown>>({
      display: 'Invoice INV-001',
      customer_id: 'customer-1',
      internal_note: 'note-1',
      hidden_field: 'secret',
      posted_only: 'posted-value',
    })

    return () => h('div', [
      h(NgbEntityFormFieldsBlock, {
        rows: baseRows,
        model,
        entityTypeCode: 'pm.invoice',
        status: 2,
        forceReadonly: props.forceReadonly,
        displayField: baseDisplayField,
        errors: {
          customer_id: [' ', 'Customer is required'],
          internal_note: 'Bad note',
        },
        behavior: {
          isFieldHidden: ({ field }) => field.key === 'hidden_field',
          isFieldReadonly: ({ field, forceReadonly, status }) => (
            field.key === 'internal_note'
              ? true
              : Boolean(forceReadonly) || (field.key === 'posted_only' && status === 2)
          ),
        },
      }),
      h('div', { 'data-testid': 'customer-model' }, String(model.customer_id)),
      h('div', { 'data-testid': 'note-model' }, String(model.internal_note)),
    ])
  },
})

test('renders display and visible fields, skips hidden and duplicate display rows, and resolves row hints/errors', async () => {
  const view = await render(FieldsBlockHarness)

  await expect.element(view.getByTestId('metadata-field-renderer:display')).toBeVisible()
  await expect.element(view.getByTestId('metadata-field-renderer:customer_id')).toBeVisible()
  await expect.element(view.getByTestId('metadata-field-renderer:internal_note')).toBeVisible()
  await expect.element(view.getByTestId('metadata-field-renderer:posted_only')).toBeVisible()

  expect(document.querySelector('[data-testid="metadata-field-renderer:hidden_field"]')).toBeNull()
  expect(document.body.textContent?.includes('renderer-key:hidden_field')).toBe(false)
  expect((document.body.textContent?.match(/renderer-key:display/g) ?? []).length).toBe(1)

  await expect.element(view.getByText('row-label:Display')).toBeVisible()
  await expect.element(view.getByText('row-label:Customer')).toBeVisible()
  await expect.element(view.getByText('row-hint:Required')).toBeVisible()
  await expect.element(view.getByText('Customer is required')).toBeVisible()
  await expect.element(view.getByText('row-hint:Visible only to admins')).toBeVisible()
  await expect.element(view.getByText('Bad note')).toBeVisible()
})

test('applies readonly behavior and writes renderer updates back into the shared model', async () => {
  const view = await render(FieldsBlockHarness, {
    props: {
      forceReadonly: false,
    },
  })

  await expect.element(view.getByTestId('metadata-field-renderer:display')).toHaveTextContent('renderer-readonly:false')
  await expect.element(view.getByTestId('metadata-field-renderer:customer_id')).toHaveTextContent('renderer-readonly:false')
  await expect.element(view.getByTestId('metadata-field-renderer:internal_note')).toHaveTextContent('renderer-readonly:true')
  await expect.element(view.getByTestId('metadata-field-renderer:posted_only')).toHaveTextContent('renderer-readonly:true')

  await view.getByRole('button', { name: 'Update field:customer_id' }).click()
  await view.getByRole('button', { name: 'Update field:internal_note' }).click()

  await expect.element(view.getByTestId('customer-model')).toHaveTextContent('updated:customer_id')
  await expect.element(view.getByTestId('note-model')).toHaveTextContent('updated:internal_note')
})
