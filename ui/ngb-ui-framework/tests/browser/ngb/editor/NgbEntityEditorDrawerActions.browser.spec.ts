import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import { StubIcon } from './stubs'

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

import NgbEntityEditorDrawerActions from '../../../../src/ngb/editor/NgbEntityEditorDrawerActions.vue'

const DrawerActionsHarness = defineComponent({
  setup() {
    const events = ref<string[]>([])
    const push = (value: string) => {
      events.value = [...events.value, value]
    }

    return () => h('div', [
      h(NgbEntityEditorDrawerActions, {
        flags: {
          canSave: true,
          isDirty: true,
          loading: false,
          saving: false,
          canExpand: true,
          canDelete: false,
          canMarkForDeletion: false,
          canUnmarkForDeletion: true,
          canPost: false,
          canUnpost: false,
          canShowAudit: true,
          canShareLink: true,
        },
        extraActions: [
          {
            key: 'preview',
            title: 'Preview',
            icon: 'eye',
            disabled: false,
          },
        ],
        onAction: (action: string) => push(action),
      }),
      h('div', { 'data-testid': 'events-log' }, events.value.join('|')),
    ])
  },
})

const DrawerActionsBusyHarness = defineComponent({
  setup() {
    return () => h(NgbEntityEditorDrawerActions, {
      flags: {
        canSave: true,
        isDirty: false,
        loading: true,
        saving: false,
        canExpand: true,
        canDelete: false,
        canMarkForDeletion: true,
        canUnmarkForDeletion: false,
        canPost: false,
        canUnpost: false,
        canShowAudit: true,
        canShareLink: true,
      },
      extraActions: [
        {
          key: 'preview',
          title: 'Preview',
          icon: 'eye',
          disabled: true,
        },
      ],
    })
  },
})

test('renders drawer action buttons and emits the expected action keys', async () => {
  const view = await render(DrawerActionsHarness)
  const eventsLog = view.getByTestId('events-log')

  await expect.element(view.getByTitle('Open full page')).toBeVisible()
  await expect.element(view.getByTitle('Share link')).toBeVisible()
  await expect.element(view.getByTitle('Audit log')).toBeVisible()
  await expect.element(view.getByTitle('Unmark for deletion')).toBeVisible()
  await expect.element(view.getByTitle('Preview')).toBeVisible()
  await expect.element(view.getByTitle('Save')).toBeVisible()

  await view.getByTitle('Open full page').click()
  await view.getByTitle('Share link').click()
  await view.getByTitle('Audit log').click()
  await view.getByTitle('Unmark for deletion').click()
  await view.getByTitle('Preview').click()
  await view.getByTitle('Save').click()

  expect(eventsLog.element().textContent ?? '').toBe('expand|share|audit|mark|preview|save')
})

test('disables built-in buttons when loading and respects per-action extra disables', async () => {
  const view = await render(DrawerActionsBusyHarness)

  expect((view.getByTitle('Open full page').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Share link').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Audit log').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Mark for deletion').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Save').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Preview').element() as HTMLButtonElement).disabled).toBe(true)
})
