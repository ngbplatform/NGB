import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import NgbPermissionMatrix from '../../../../src/ngb/security/NgbPermissionMatrix.vue'
import type { PermissionAssignmentDto, PermissionDefinitionDto } from '../../../../src/ngb/security/types'

const definitions: PermissionDefinitionDto[] = [
  {
    resourceKind: 'document',
    resourceCode: 'pm.lease',
    actionCode: 'view',
    displayName: 'View Leases',
    description: 'Read lease documents',
    group: 'Documents',
  },
  {
    resourceKind: 'document',
    resourceCode: 'pm.lease',
    actionCode: 'post',
    displayName: 'Post Leases',
    group: 'Documents',
  },
  {
    resourceKind: 'system',
    resourceCode: 'users',
    actionCode: 'manage',
    displayName: 'Manage Users',
    group: 'System',
  },
]

const MatrixHarness = defineComponent({
  props: {
    disabled: {
      type: Boolean,
      default: false,
    },
  },
  setup(props) {
    const value = ref<PermissionAssignmentDto[]>([
      { resourceKind: 'document', resourceCode: 'pm.lease', actionCode: 'view' },
    ])

    return () => h('div', [
      h(NgbPermissionMatrix, {
        modelValue: value.value,
        definitions,
        disabled: props.disabled,
        'onUpdate:modelValue': (next: PermissionAssignmentDto[]) => {
          value.value = next
        },
      }),
      h('div', { 'data-testid': 'permission-value' }, value.value.map((entry) => `${entry.resourceKind}.${entry.resourceCode}.${entry.actionCode}`).join('|')),
    ])
  },
})

test('toggles individual permissions and whole groups', async () => {
  await page.viewport(1280, 900)

  const view = await render(MatrixHarness)

  await expect.element(view.getByTestId('permission-matrix')).toBeVisible()
  await expect.element(view.getByText('View Leases')).toBeVisible()
  await expect.element(view.getByTestId('permission-value')).toHaveTextContent('document.pm.lease.view')

  await view.getByText('Post Leases').click()
  await expect.element(view.getByTestId('permission-value')).toHaveTextContent('document.pm.lease.post')

  await view.getByRole('button', { name: 'Clear' }).click()
  await expect.element(view.getByTestId('permission-value')).not.toHaveTextContent('document.pm.lease.view')
  await expect.element(view.getByTestId('permission-value')).not.toHaveTextContent('document.pm.lease.post')

  await view.getByRole('button', { name: 'Select' }).first().click()
  await expect.element(view.getByTestId('permission-value')).toHaveTextContent('document.pm.lease.post|document.pm.lease.view')

  await view.getByText('View Leases').click()
  await expect.element(view.getByTestId('permission-value')).toHaveTextContent('document.pm.lease.post')
  await expect.element(view.getByTestId('permission-value')).not.toHaveTextContent('document.pm.lease.view')
})

test('filters by every searchable field and renders the empty result', async () => {
  await page.viewport(1280, 900)

  const view = await render(MatrixHarness)
  const filter = view.getByPlaceholder('Filter permissions')

  await filter.fill('read lease')
  await expect.element(view.getByText('View Leases')).toBeVisible()
  await expect.element(view.getByText('Manage Users')).not.toBeInTheDocument()

  await filter.fill('system.users.manage')
  await expect.element(view.getByText('Manage Users')).toBeVisible()

  await filter.fill('does-not-exist')
  await expect.element(view.getByText('No permissions match the current filter.')).toBeVisible()
})

test('ignores programmatically dispatched changes while disabled', async () => {
  await page.viewport(1280, 900)

  const view = await render(MatrixHarness, { props: { disabled: true } })
  const state = view.getByTestId('permission-value')
  const checkboxes = Array.from(document.querySelectorAll<HTMLInputElement>('input[type="checkbox"]'))
  const postCheckbox = checkboxes.find((checkbox) => !checkbox.checked)
  const selectButton = view.getByRole('button', { name: 'Select' }).first().element() as HTMLButtonElement

  expect(postCheckbox).toBeTruthy()
  postCheckbox!.checked = true
  postCheckbox!.dispatchEvent(new Event('change', { bubbles: true }))
  selectButton.dispatchEvent(new MouseEvent('click', { bubbles: true }))

  await expect.element(state).toHaveTextContent('document.pm.lease.view')
  await expect.element(state).not.toHaveTextContent('document.pm.lease.post')
})
