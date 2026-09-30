import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { h } from 'vue'

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: { template: '<span data-testid="shield-icon">shield</span>' },
}))

import NgbAccessDeniedState from '../../../../src/ngb/security/NgbAccessDeniedState.vue'

test('renders safe default copy without an actions region', async () => {
  const view = await render(NgbAccessDeniedState)

  await expect.element(view.getByText('Access denied', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Your current access profile does not allow this operation.', { exact: true })).toBeVisible()
  await expect.element(view.getByTestId('shield-icon')).toBeVisible()
  expect(document.querySelector('button')).toBeNull()
})

test('renders custom copy and the optional actions slot', async () => {
  const view = await render(NgbAccessDeniedState, {
    props: {
      title: 'No billing access',
      message: 'Ask an administrator for the billing.read permission.',
    },
    slots: {
      actions: () => h('button', { type: 'button' }, 'Return home'),
    },
  })

  await expect.element(view.getByText('No billing access', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Ask an administrator for the billing.read permission.', { exact: true })).toBeVisible()
  await expect.element(view.getByRole('button', { name: 'Return home' })).toBeVisible()
})
