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
  setup() {
    const value = ref<PermissionAssignmentDto[]>([
      { resourceKind: 'document', resourceCode: 'pm.lease', actionCode: 'view' },
    ])

    return () => h('div', [
      h(NgbPermissionMatrix, {
        modelValue: value.value,
        definitions,
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
})

