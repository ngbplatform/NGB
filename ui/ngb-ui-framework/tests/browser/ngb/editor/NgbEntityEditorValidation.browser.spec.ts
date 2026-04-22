import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, nextTick, ref, type PropType } from 'vue'

import {
  StubConfirmDialog,
  StubDiscardDialog,
  StubDrawer,
  StubEntityAuditSidebar,
  StubEntityEditorHeader,
} from './stubs'

vi.mock('../../../../src/ngb/components/NgbConfirmDialog.vue', () => ({
  default: StubConfirmDialog,
}))

vi.mock('../../../../src/ngb/components/NgbDrawer.vue', () => ({
  default: StubDrawer,
}))

vi.mock('../../../../src/ngb/metadata/NgbMetadataFieldRenderer.vue', async () => {
  const { defineComponent, h } = await import('vue')

  return {
    default: defineComponent({
      props: {
        field: {
          type: Object as PropType<{ key: string }>,
          required: true,
        },
        modelValue: {
          type: [String, Number, Boolean, Object, Array, null] as PropType<unknown>,
          default: null,
        },
        readonly: {
          type: Boolean,
          default: false,
        },
        disabled: {
          type: Boolean,
          default: false,
        },
      },
      emits: ['update:modelValue'],
      setup(props, { emit }) {
        return () => h('input', {
          type: 'text',
          'aria-label': `field-${props.field.key}`,
          value: String(props.modelValue ?? ''),
          readonly: props.readonly,
          disabled: props.disabled,
          onInput: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).value),
        })
      },
    }),
  }
})

vi.mock('../../../../src/ngb/editor/NgbEntityAuditSidebar.vue', () => ({
  default: StubEntityAuditSidebar,
}))

vi.mock('../../../../src/ngb/editor/NgbEditorDiscardDialog.vue', () => ({
  default: StubDiscardDialog,
}))

vi.mock('../../../../src/ngb/editor/NgbEntityEditorHeader.vue', () => ({
  default: StubEntityEditorHeader,
}))

import NgbEntityEditor from '../../../../src/ngb/editor/NgbEntityEditor.vue'

const baseForm = {
  sections: [
    {
      title: 'Main',
      rows: [
        {
          fields: [
            {
              key: 'customer_id',
              label: 'Customer',
              dataType: 'String',
              uiControl: 1,
              isRequired: true,
              isReadOnly: false,
            },
            {
              key: 'amount',
              label: 'Amount',
              dataType: 'Decimal',
              uiControl: 3,
              isRequired: true,
              isReadOnly: false,
            },
          ],
        },
      ],
    },
  ],
}

const ValidationFocusHarness = defineComponent({
  setup() {
    const editorRef = ref<InstanceType<typeof NgbEntityEditor> | null>(null)
    const model = ref({
      customer_id: '',
      amount: null as number | null,
    })
    const displayedError = ref<{ summary: string; issues: [] } | null>(null)
    const bannerIssues = ref<Array<{
      scope: 'form' | 'field'
      path: string
      label: string
      messages: string[]
    }>>([])
    const errors = ref<Record<string, string | string[] | null> | null>(null)
    const focusResult = ref('pending')

    async function simulateSaveFailure() {
      displayedError.value = {
        summary: 'Could not save invoice.',
        issues: [],
      }
      bannerIssues.value = [
        {
          scope: 'form',
          path: '_form',
          label: 'Validation',
          messages: ['Please review the highlighted fields.'],
        },
        {
          scope: 'field',
          path: 'amount',
          label: 'Amount',
          messages: ['Amount is required.'],
        },
        {
          scope: 'field',
          path: 'customer_id',
          label: 'Customer',
          messages: ['Customer is required.'],
        },
      ]
      errors.value = {
        amount: 'Amount is required.',
        customer_id: 'Customer is required.',
      }

      await nextTick()
      focusResult.value = String(editorRef.value?.focusFirstError(['_form', 'amount', 'customer_id']) ?? false)
    }

    return () => h('div', [
      h(NgbEntityEditor, {
        ref: editorRef,
        kind: 'document',
        mode: 'page',
        title: 'Invoice INV-001',
        loading: false,
        saving: false,
        isNew: false,
        isMarkedForDeletion: false,
        displayedError: displayedError.value,
        bannerIssues: bannerIssues.value,
        form: baseForm,
        model: model.value,
        entityTypeCode: 'pm.invoice',
        errors: errors.value,
      }),
      h('button', {
        type: 'button',
        onClick: simulateSaveFailure,
      }, 'Simulate save failure'),
      h('div', { 'data-testid': 'focus-result' }, `focus:${focusResult.value}`),
    ])
  },
})

test('focuses the first invalid editor control after a save validation failure', async () => {
  await page.viewport(1280, 900)

  const view = await render(ValidationFocusHarness)

  expect(document.activeElement?.getAttribute('aria-label')).not.toBe('field-amount')

  await view.getByRole('button', { name: 'Simulate save failure' }).click()

  await expect.element(view.getByText('Could not save invoice.')).toBeVisible()
  await expect.element(view.getByText('Please review the highlighted fields.')).toBeVisible()
  await expect.element(view.getByTestId('focus-result')).toHaveTextContent('focus:true')
  expect(document.activeElement?.getAttribute('aria-label')).toBe('field-amount')
})
