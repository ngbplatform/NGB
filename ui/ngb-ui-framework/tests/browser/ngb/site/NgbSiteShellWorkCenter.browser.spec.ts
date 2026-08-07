import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

const mocks = vi.hoisted(() => ({
  getAuthSnapshot: vi.fn(),
  refreshSummary: vi.fn(),
  connectRealtime: vi.fn(),
}))

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  getAuthSnapshot: mocks.getAuthSnapshot,
}))

vi.mock('../../../../src/ngb/work-center/useWorkCenter', () => ({
  useWorkCenter: () => ({
    summary: ref({ attentionCount: 7 }),
    refreshSummary: mocks.refreshSummary,
    connectRealtime: mocks.connectRealtime,
  }),
}))

vi.mock('../../../../src/ngb/work-center/NgbWorkCenterDrawer.vue', () => ({
  default: defineComponent(() => () => h('div', 'Work Center test drawer')),
}))

import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'

function shellProps() {
  return {
    moduleTitle: 'CRM',
    productTitle: 'NGB',
    pinned: [],
    recent: [],
    nodes: [],
    selectedId: null,
  }
}

test('adds notification preferences without caller settings and starts Work Center only for authenticated users', async () => {
  mocks.getAuthSnapshot.mockReturnValueOnce({
    initialized: true,
    authenticated: false,
  })
  await render(NgbSiteShell, { props: shellProps() })
  expect(mocks.refreshSummary).not.toHaveBeenCalled()
  expect(mocks.connectRealtime).not.toHaveBeenCalled()

  mocks.getAuthSnapshot.mockReturnValueOnce({
    initialized: true,
    authenticated: true,
  })
  mocks.refreshSummary.mockRejectedValueOnce(new Error('temporary'))
  await render(NgbSiteShell, { props: shellProps() })

  await vi.waitFor(() => {
    expect(mocks.refreshSummary).toHaveBeenCalledOnce()
    expect(mocks.refreshSummary).toHaveBeenCalledWith(null)
    expect(mocks.connectRealtime).toHaveBeenCalledOnce()
  })
})
