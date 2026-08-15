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

vi.mock('../../../../src/ngb/work-center/useWorkCenter', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../../../src/ngb/work-center/useWorkCenter')>(),
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

test('adds notification preferences without caller settings and delegates session handling to Work Center', async () => {
  mocks.refreshSummary.mockReset().mockResolvedValue(undefined)
  mocks.connectRealtime.mockReset().mockResolvedValue(undefined)
  mocks.getAuthSnapshot.mockReturnValueOnce({
    initialized: true,
    authenticated: false,
  })
  await render(NgbSiteShell, { props: shellProps() })
  await vi.waitFor(() => {
    expect(mocks.refreshSummary).toHaveBeenCalledOnce()
    expect(mocks.connectRealtime).toHaveBeenCalledOnce()
  })

  mocks.getAuthSnapshot.mockReturnValueOnce({
    initialized: true,
    authenticated: true,
  })
  mocks.refreshSummary.mockClear()
  mocks.connectRealtime.mockClear()
  mocks.refreshSummary.mockRejectedValueOnce(new Error('temporary'))
  await render(NgbSiteShell, { props: shellProps() })

  await vi.waitFor(() => {
    expect(mocks.refreshSummary).toHaveBeenCalledOnce()
    expect(mocks.refreshSummary).toHaveBeenCalledWith()
    expect(mocks.connectRealtime).toHaveBeenCalledOnce()
  })
})
