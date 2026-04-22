import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h, ref } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute, useRouter } from 'vue-router'

import {
  StubConfirmDialog,
  StubDiscardDialog,
  StubDrawer,
  StubEntityAuditSidebar,
  StubEntityEditorHeader,
  StubEntityForm,
} from './stubs'

vi.mock('../../../../src/ngb/components/NgbConfirmDialog.vue', () => ({
  default: StubConfirmDialog,
}))

vi.mock('../../../../src/ngb/components/NgbDrawer.vue', () => ({
  default: StubDrawer,
}))

vi.mock('../../../../src/ngb/metadata/NgbEntityForm.vue', () => ({
  default: StubEntityForm,
}))

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
import { useEntityEditorLeaveGuard } from '../../../../src/ngb/editor/useEntityEditorLeaveGuard'

const ExtensionBlock = defineComponent({
  props: {
    label: {
      type: String,
      default: '',
    },
  },
  setup(props) {
    return () => h('div', props.label)
  },
})

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
              uiControl: 0,
              isRequired: false,
              isReadOnly: false,
            },
          ],
        },
      ],
    },
  ],
}

const EditorExposeHarness = defineComponent({
  setup() {
    const editorRef = ref<InstanceType<typeof NgbEntityEditor> | null>(null)
    const focusFieldResult = ref('pending')
    const focusFirstErrorResult = ref('pending')

    return () => h('div', [
      h(NgbEntityEditor, {
        ref: editorRef,
        kind: 'document',
        mode: 'drawer',
        title: 'Invoice INV-001',
        subtitle: 'April billing',
        documentStatusLabel: 'Posted',
        documentStatusTone: 'success',
        loading: false,
        saving: false,
        pageActions: [{ key: 'refresh', title: 'Refresh', icon: 'refresh' }],
        documentPrimaryActions: [{ key: 'save', title: 'Save', icon: 'save' }],
        documentMoreActionGroups: [{ key: 'more', label: 'More', items: [] }],
        isNew: false,
        isMarkedForDeletion: false,
        form: baseForm,
        model: {
          customer_id: 'customer-1',
        },
        entityTypeCode: 'pm.invoice',
        status: 2,
        isReadOnly: true,
      }),
      h('button', {
        type: 'button',
        onClick: () => {
          focusFieldResult.value = String(editorRef.value?.focusField('customer_id') ?? false)
        },
      }, 'Run focus field'),
      h('button', {
        type: 'button',
        onClick: () => {
          focusFirstErrorResult.value = String(editorRef.value?.focusFirstError(['notes', 'amount']) ?? false)
        },
      }, 'Run focus first error'),
      h('div', `Focus field result: ${focusFieldResult.value}`),
      h('div', `Focus first error result: ${focusFirstErrorResult.value}`),
    ])
  },
})

const EditorBannersHarness = defineComponent({
  setup() {
    return () => h(NgbEntityEditor, {
      kind: 'catalog',
      mode: 'page',
      title: 'Property record',
      loading: false,
      saving: false,
      isNew: false,
      isMarkedForDeletion: true,
      displayedError: {
        summary: 'Could not save property.',
        issues: [],
      },
      bannerIssues: [
        {
          scope: 'form',
          path: '_form',
          label: 'Validation',
          messages: ['Please review the highlighted fields.'],
        },
        {
          scope: 'field',
          path: 'name',
          label: 'Name',
          messages: ['Required'],
        },
      ],
      form: baseForm,
      model: {
        name: '',
      },
      entityTypeCode: 'pm.property',
      errors: {
        name: 'Required',
      },
      afterFormExtensions: [
        {
          key: 'after-extension',
          component: ExtensionBlock,
          props: {
            label: 'After extension',
          },
        },
      ],
      dialogExtensions: [
        {
          key: 'dialog-extension',
          component: ExtensionBlock,
          props: {
            label: 'Dialog extension',
          },
        },
      ],
    }, {
      'after-form': () => h('div', 'After form slot'),
      dialogs: () => h('div', 'Dialog slot'),
    })
  },
})

const EditorDedupedValidationBannerHarness = defineComponent({
  setup() {
    return () => h(NgbEntityEditor, {
      kind: 'document',
      mode: 'page',
      title: 'Timesheet T-2026-000004',
      loading: false,
      saving: false,
      isNew: false,
      isMarkedForDeletion: false,
      displayedError: {
        summary: 'An invoice draft or posted invoice already exists for this timesheet.',
        issues: [],
      },
      bannerIssues: [
        {
          scope: 'form',
          path: '_form',
          label: 'Validation',
          messages: ['An invoice draft or posted invoice already exists for this timesheet.'],
        },
      ],
      form: baseForm,
      model: {
        customer_id: 'customer-1',
      },
      entityTypeCode: 'ab.timesheet',
    })
  },
})

const EditorEventsHarness = defineComponent({
  setup() {
    const events = ref<string[]>([])

    function push(value: string) {
      events.value = [...events.value, value]
    }

    return () => h('div', [
      h(NgbEntityEditor, {
        kind: 'document',
        mode: 'page',
        title: 'Invoice INV-001',
        loading: false,
        saving: false,
        isNew: false,
        isMarkedForDeletion: false,
        form: baseForm,
        model: {
          customer_id: 'customer-1',
        },
        entityTypeCode: 'pm.invoice',
        documentPrimaryActions: [{ key: 'save', title: 'Save', icon: 'save' }],
        auditOpen: true,
        auditEntityKind: 2,
        auditEntityId: 'doc-1',
        auditEntityTitle: 'Invoice INV-001',
        leaveOpen: true,
        markConfirmOpen: true,
        markConfirmMessage: 'Remove invoice?',
        onBack: () => push('back'),
        onClose: () => push('close'),
        onAction: (action: string) => push(`action:${action}`),
        onCloseAuditLog: () => push('closeAuditLog'),
        onCancelLeave: () => push('cancelLeave'),
        onConfirmLeave: () => push('confirmLeave'),
        onCancelMarkForDeletion: () => push('cancelMarkForDeletion'),
        onConfirmMarkForDeletion: () => push('confirmMarkForDeletion'),
      }),
      h('div', { 'data-testid': 'events-log' }, events.value.join('|')),
    ])
  },
})

const GuardedEditorHarness = defineComponent({
  setup() {
    const router = useRouter()
    const route = useRoute()
    const dirty = ref(true)
    const loading = ref(false)
    const saving = ref(false)
    const closeCount = ref(0)

    const leaveGuard = useEntityEditorLeaveGuard({
      isDirty: computed(() => dirty.value),
      loading,
      saving,
      router,
      onClose: () => {
        closeCount.value += 1
      },
    })

    return () => h('div', [
      h(NgbEntityEditor, {
        kind: 'document',
        mode: 'page',
        title: 'Invoice INV-001',
        loading: loading.value,
        saving: saving.value,
        isNew: false,
        isMarkedForDeletion: false,
        form: baseForm,
        model: {
          customer_id: 'customer-1',
        },
        entityTypeCode: 'pm.invoice',
        leaveOpen: leaveGuard.leaveOpen.value,
        onCancelLeave: leaveGuard.cancelLeave,
        onConfirmLeave: leaveGuard.confirmLeave,
      }),
      h('button', {
        type: 'button',
        onClick: () => leaveGuard.requestNavigate('/target'),
      }, 'Request navigate'),
      h('button', {
        type: 'button',
        onClick: () => leaveGuard.requestClose(),
      }, 'Request close'),
      h('button', {
        type: 'button',
        onClick: () => {
          dirty.value = false
        },
      }, 'Mark clean'),
      h('div', { 'data-testid': 'guard-state' }, `route:${route.fullPath};dirty:${String(dirty.value)};closeCount:${closeCount.value}`),
    ])
  },
})

const GuardedEditorAppRoot = defineComponent({
  setup() {
    return () => h(RouterView)
  },
})

async function renderGuardedEditorHarness() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/editor',
        component: GuardedEditorHarness,
      },
      {
        path: '/target',
        component: {
          template: '<div data-testid="leave-guard-target">Target page</div>',
        },
      },
    ],
  })

  await router.push('/editor')
  await router.isReady()

  const view = await render(GuardedEditorAppRoot, {
    global: {
      plugins: [router],
    },
  })

  return { router, view }
}

test('forwards editor form presentation and exposed focus helpers', async () => {
  const view = await render(EditorExposeHarness)
  const form = view.getByTestId('entity-form')

  await expect.element(view.getByText('Invoice INV-001')).toBeVisible()
  await expect.element(view.getByText('April billing')).toBeVisible()
  await expect.element(form).toBeVisible()

  expect(form.element().getAttribute('data-entity-type')).toBe('pm.invoice')
  expect(form.element().getAttribute('data-presentation')).toBe('flat')
  expect(form.element().getAttribute('data-status')).toBe('2')
  expect(form.element().getAttribute('data-readonly')).toBe('true')

  await view.getByRole('button', { name: 'Run focus field' }).click()
  await view.getByRole('button', { name: 'Run focus first error' }).click()

  await expect.element(view.getByText('Focus field result: true')).toBeVisible()
  await expect.element(view.getByText('Focus first error result: true')).toBeVisible()
  await expect.element(view.getByText('Last focus: customer_id')).toBeVisible()
  await expect.element(view.getByText('Last error keys: notes|amount')).toBeVisible()
})

test('renders deletion and validation banners plus form and dialog extensions', async () => {
  const view = await render(EditorBannersHarness)
  const form = view.getByTestId('entity-form')

  await expect.element(view.getByText('Deleted')).toBeVisible()
  await expect.element(view.getByText('This record is marked for deletion. Restore it to edit or post.')).toBeVisible()
  await expect.element(view.getByText('Could not save property.')).toBeVisible()
  await expect.element(view.getByText('Please review the highlighted fields.')).toBeVisible()
  await expect.element(view.getByText('Name:')).toBeVisible()
  await expect.element(view.getByText('After extension')).toBeVisible()
  await expect.element(view.getByText('After form slot')).toBeVisible()
  await expect.element(view.getByText('Dialog extension')).toBeVisible()
  await expect.element(view.getByText('Dialog slot')).toBeVisible()

  expect(form.element().getAttribute('data-presentation')).toBe('sections')
  expect(form.element().getAttribute('data-status')).toBe('none')
})

test('dedupes identical summary and form-level validation banner text', async () => {
  await render(EditorDedupedValidationBannerHarness)

  const pageText = document.body.textContent ?? ''
  const message = 'An invoice draft or posted invoice already exists for this timesheet.'
  expect(countOccurrences(pageText, message)).toBe(1)
})

test('re-emits header, audit, discard, and mark-for-deletion dialog events', async () => {
  const view = await render(EditorEventsHarness)
  const eventsLog = view.getByTestId('events-log')

  await view.getByRole('button', { name: 'Header back' }).click()
  await view.getByRole('button', { name: 'Header close' }).click()
  await view.getByRole('button', { name: 'Header action' }).click()
  await view.getByRole('button', { name: 'Audit back' }).click()
  await view.getByRole('button', { name: 'Audit close' }).click()
  await view.getByRole('button', { name: 'Drawer close' }).click()
  await view.getByRole('button', { name: 'Leave cancel' }).click()
  await view.getByRole('button', { name: 'Leave confirm' }).click()
  await view.getByRole('button', { name: 'Mark cancel' }).click()
  await view.getByRole('button', { name: 'Mark confirm' }).click()

  await expect.element(eventsLog).toBeVisible()
  expect(eventsLog.element().textContent ?? '').toBe(
    'back|close|action:save|closeAuditLog|closeAuditLog|closeAuditLog|cancelLeave|confirmLeave|cancelMarkForDeletion|confirmMarkForDeletion',
  )
})

test('integrates the leave guard with dirty navigation and dirty close confirmation flows', async () => {
  const { router, view } = await renderGuardedEditorHarness()

  await view.getByRole('button', { name: 'Request navigate' }).click()
  await expect.element(view.getByTestId('discard-dialog')).toBeVisible()
  await expect.element(view.getByTestId('guard-state')).toHaveTextContent('route:/editor;dirty:true;closeCount:0')

  await view.getByRole('button', { name: 'Leave cancel' }).click()
  await expect.element(view.getByTestId('guard-state')).toHaveTextContent('route:/editor;dirty:true;closeCount:0')

  await view.getByRole('button', { name: 'Request navigate' }).click()
  await view.getByRole('button', { name: 'Leave confirm' }).click()
  await expect.element(view.getByTestId('leave-guard-target')).toBeVisible()
  expect(router.currentRoute.value.fullPath).toBe('/target')

  await router.push('/editor')
  await view.getByRole('button', { name: 'Request close' }).click()
  await expect.element(view.getByTestId('discard-dialog')).toBeVisible()
  await view.getByRole('button', { name: 'Leave confirm' }).click()
  await expect.element(view.getByTestId('guard-state')).toHaveTextContent('route:/editor;dirty:true;closeCount:1')

  await view.getByRole('button', { name: 'Mark clean' }).click()
  await view.getByRole('button', { name: 'Request navigate' }).click()
  expect(router.currentRoute.value.fullPath).toBe('/target')
})

function countOccurrences(source: string, value: string): number {
  if (!value) return 0

  const escaped = value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return (source.match(new RegExp(escaped, 'g')) ?? []).length
}
