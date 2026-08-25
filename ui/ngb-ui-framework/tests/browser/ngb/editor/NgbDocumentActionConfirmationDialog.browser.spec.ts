import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import type { DocumentActionConfirmationState } from '../../../../src/ngb/editor/useConfiguredEntityEditorDocumentActions'

vi.mock('../../../../src/ngb/components/NgbConfirmDialog.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      props: {
        open: Boolean,
        title: String,
        message: String,
        confirmText: String,
        danger: Boolean,
        confirmLoading: Boolean,
        confirmDisabled: Boolean,
      },
      emits: ['update:open', 'confirm'],
      setup(props, { emit, slots }) {
        return () => h('div', [
          h('div', { 'data-testid': 'confirm-props' }, [
            props.open,
            props.title,
            props.message,
            props.confirmText,
            props.danger,
            props.confirmLoading,
            props.confirmDisabled,
          ].join('|')),
          slots.default?.(),
          h('button', { type: 'button', onClick: () => emit('update:open', true) }, 'Keep open'),
          h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Close'),
          h('button', { type: 'button', onClick: () => emit('confirm') }, 'Submit confirmation'),
        ])
      },
    }),
  }
})

import NgbDocumentActionConfirmationDialog from '../../../../src/ngb/editor/NgbDocumentActionConfirmationDialog.vue'

const Harness = defineComponent({
  setup() {
    const confirmation = ref<DocumentActionConfirmationState | null>({
      actionCode: 'post',
      title: 'Post document',
      message: 'Posting is final.',
      confirmLabel: 'Post',
      requireReason: true,
      danger: true,
      loading: true,
    })
    const events = ref<string[]>([])

    return () => h('div', [
      h(NgbDocumentActionConfirmationDialog, {
        confirmation: confirmation.value,
        onCancel: () => events.value.push('cancel'),
        onConfirm: (reason: string | null) => events.value.push(`confirm:${reason ?? 'none'}`),
      }),
      h('button', {
        type: 'button',
        onClick: () => {
          confirmation.value = {
            actionCode: 'unpost',
            title: 'Unpost document',
            message: 'Explain why.',
            confirmLabel: 'Unpost',
            requireReason: true,
            danger: false,
            loading: false,
          }
        },
      }, 'Change action'),
      h('button', {
        type: 'button',
        onClick: () => {
          confirmation.value = { actionCode: 'approve' } as DocumentActionConfirmationState
        },
      }, 'Use defaults'),
      h('button', { type: 'button', onClick: () => { confirmation.value = null } }, 'Hide dialog'),
      h('div', { 'data-testid': 'events' }, events.value.join('|')),
    ])
  },
})

test('requires a trimmed reason, resets it for a new action, and only cancels on close', async () => {
  await page.viewport(1280, 900)
  const view = await render(Harness)

  await expect.element(view.getByTestId('confirm-props')).toHaveTextContent('true|Post document|Posting is final.|Post|true|true|true')
  await view.getByRole('button', { name: 'Submit confirmation' }).click()
  await expect.element(view.getByTestId('events')).toHaveTextContent('')

  await view.getByRole('textbox', { name: 'Reason' }).fill('   ')
  await view.getByRole('button', { name: 'Submit confirmation' }).click()
  await expect.element(view.getByTestId('events')).toHaveTextContent('')

  await view.getByRole('textbox', { name: 'Reason' }).fill('  period correction  ')
  await view.getByRole('button', { name: 'Submit confirmation' }).click()
  await expect.element(view.getByTestId('events')).toHaveTextContent('confirm:period correction')

  await view.getByRole('button', { name: 'Keep open' }).click()
  await expect.element(view.getByTestId('events')).not.toHaveTextContent('cancel')
  await view.getByRole('button', { name: 'Close' }).click()
  await expect.element(view.getByTestId('events')).toHaveTextContent('cancel')

  await view.getByText('Change action').click()
  await expect.element(view.getByRole('textbox', { name: 'Reason' })).toHaveValue('')
})

test('uses safe defaults and emits null when a reason is optional', async () => {
  await page.viewport(1280, 900)
  const view = await render(Harness)

  await view.getByText('Use defaults').click()
  await expect.element(view.getByTestId('confirm-props')).toHaveTextContent('true|||Confirm|false|false|false')
  await view.getByRole('button', { name: 'Submit confirmation' }).click()
  await expect.element(view.getByTestId('events')).toHaveTextContent('confirm:none')

  await view.getByText('Hide dialog').click()
  await expect.element(view.getByTestId('confirm-props')).toHaveTextContent('false|||Confirm|false|false|false')
})
