import { page } from 'vitest/browser'
import { expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'

import NgbAccessAuditPanel from '../../../../src/ngb/security/NgbAccessAuditPanel.vue'

test('renders security audit events with optional actors', async () => {
  await page.viewport(1280, 900)

  const view = await render(NgbAccessAuditPanel, {
    props: {
      events: [
        {
          id: 'audit-1',
          title: 'Role permissions updated',
          actor: 'admin@example.com',
          occurredAt: '2026-08-23 16:15',
        },
        {
          id: 'audit-2',
          title: 'Access policy synchronized',
          actor: null,
          occurredAt: '2026-08-23 16:16',
        },
      ],
    },
  })

  await expect.element(view.getByText('Role permissions updated')).toBeVisible()
  await expect.element(view.getByText('admin@example.com')).toBeVisible()
  await expect.element(view.getByText('Access policy synchronized')).toBeVisible()
  await expect.element(view.getByText('2026-08-23 16:16')).toBeVisible()
})

test('renders the empty state when no audit events are supplied', async () => {
  await page.viewport(1280, 900)

  const view = await render(NgbAccessAuditPanel)

  await expect.element(view.getByText('No security audit events in this view.')).toBeVisible()
})
