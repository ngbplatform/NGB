import { page } from 'vitest/browser'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h, reactive, ref } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute, useRouter } from 'vue-router'

import type { EditorErrorState } from '../../../../src/ngb/editor/entityEditorErrors'

const authRecoveryMocks = vi.hoisted(() => ({
  forceRefreshAccessToken: vi.fn(),
  getAccessToken: vi.fn(),
}))

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  forceRefreshAccessToken: authRecoveryMocks.forceRefreshAccessToken,
  getAccessToken: authRecoveryMocks.getAccessToken,
}))

import { httpPost } from '../../../../src/ngb/api/http'
import NgbEntityEditor from '../../../../src/ngb/editor/NgbEntityEditor.vue'
import { normalizeEntityEditorError } from '../../../../src/ngb/editor/entityEditorErrors'
import { useEntityEditorLeaveGuard } from '../../../../src/ngb/editor/useEntityEditorLeaveGuard'

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

const documentForm = {
  sections: [
    {
      title: 'Main',
      rows: [
        {
          fields: [
            {
              key: 'display',
              label: 'Display',
              dataType: 'String',
              uiControl: 1,
              isRequired: true,
              isReadOnly: false,
            },
            {
              key: 'memo',
              label: 'Memo',
              dataType: 'String',
              uiControl: 2,
              isRequired: false,
              isReadOnly: false,
            },
          ],
        },
      ],
    },
  ],
}

const EditorSessionRecoveryHarness = defineComponent({
  setup() {
    const router = useRouter()
    const route = useRoute()
    const model = reactive({
      display: 'Invoice INV-001',
      memo: 'April billing',
    })
    const baseline = ref(JSON.stringify({
      display: model.display,
      memo: model.memo,
    }))
    const loading = ref(false)
    const saving = ref(false)
    const closeCount = ref(0)
    const error = ref<EditorErrorState | null>(null)

    const isDirty = computed(() => JSON.stringify({
      display: model.display,
      memo: model.memo,
    }) !== baseline.value)

    const leaveGuard = useEntityEditorLeaveGuard({
      isDirty,
      loading,
      saving,
      router,
      onClose: () => {
        closeCount.value += 1
      },
    })

    async function saveDocument() {
      saving.value = true
      error.value = null

      try {
        const saved = await httpPost<{ display: string; memo: string }>('/api/framework/editor/document/save', {
          display: model.display,
          memo: model.memo,
        })

        model.display = saved.display
        model.memo = saved.memo
        baseline.value = JSON.stringify({
          display: saved.display,
          memo: saved.memo,
        })
      } catch (cause) {
        error.value = normalizeEntityEditorError(cause)
      } finally {
        saving.value = false
      }
    }

    return () => h('div', { class: 'min-h-screen bg-white' }, [
      h(NgbEntityEditor, {
        kind: 'document',
        mode: 'page',
        title: model.display,
        subtitle: 'Session recovery harness',
        documentStatusLabel: 'Draft',
        documentStatusTone: 'neutral',
        loading: loading.value,
        saving: saving.value,
        documentPrimaryActions: [
          { key: 'save', title: 'Save', icon: 'save', disabled: saving.value },
        ],
        documentMoreActionGroups: [],
        isNew: false,
        isMarkedForDeletion: false,
        displayedError: error.value,
        bannerIssues: error.value?.issues ?? [],
        form: documentForm,
        model,
        entityTypeCode: 'pm.invoice',
        status: 1,
        leaveOpen: leaveGuard.leaveOpen.value,
        onAction: (action: string) => {
          if (action === 'save') void saveDocument()
        },
        onClose: () => {
          leaveGuard.requestClose()
        },
        onCancelLeave: leaveGuard.cancelLeave,
        onConfirmLeave: leaveGuard.confirmLeave,
      }),
      h('button', {
        type: 'button',
        onClick: () => {
          leaveGuard.requestNavigate('/target')
        },
      }, 'Navigate away'),
      h('div', { 'data-testid': 'editor-session-state' }, `route:${route.fullPath};dirty:${String(isDirty.value)};saving:${String(saving.value)};closeCount:${closeCount.value}`),
      h('div', { 'data-testid': 'editor-session-error' }, error.value?.summary ?? ''),
    ])
  },
})

const EditorSessionRecoveryAppRoot = defineComponent({
  setup() {
    return () => h(RouterView)
  },
})

async function renderEditorSessionRecoveryHarness(initialRoute = '/editor') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/editor',
        component: EditorSessionRecoveryHarness,
      },
      {
        path: '/target',
        component: {
          template: '<div data-testid="leave-guard-target">Target page</div>',
        },
      },
    ],
  })

  await router.push(initialRoute)
  await router.isReady()

  const view = await render(EditorSessionRecoveryAppRoot, {
    global: {
      plugins: [router],
    },
  })

  return {
    router,
    view,
  }
}

test('keeps the editor dirty until a refreshed session retries and completes the save', async () => {
  await page.viewport(1440, 900)

  const fetchMock = vi.fn()
  const retryResponse = createDeferred<Response>()

  vi.stubEnv('VITE_API_BASE_URL', 'https://api.example')
  vi.stubGlobal('fetch', fetchMock)

  authRecoveryMocks.getAccessToken
    .mockResolvedValueOnce('expired-token')
    .mockResolvedValueOnce('fresh-token')
  authRecoveryMocks.forceRefreshAccessToken.mockResolvedValueOnce('fresh-token')

  fetchMock
    .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Unauthorized' }), {
      status: 401,
      headers: {
        'content-type': 'application/json',
      },
    }))
    .mockImplementationOnce(async () => await retryResponse.promise)

  const { router, view } = await renderEditorSessionRecoveryHarness()
  const memoField = document.querySelector('[data-validation-key="memo"] textarea') as HTMLTextAreaElement | null
  expect(memoField).not.toBeNull()

  memoField!.value = 'Updated after refresh'
  memoField!.dispatchEvent(new Event('input', { bubbles: true }))

  await expect.element(view.getByTestId('editor-session-state')).toHaveTextContent('route:/editor;dirty:true;saving:false;closeCount:0')

  const saveButton = view.getByRole('button', { name: 'Save' })
  const closeButton = view.getByRole('button', { name: 'Close' })

  await saveButton.click()

  await expect.element(view.getByTestId('editor-session-state')).toHaveTextContent('route:/editor;dirty:true;saving:true;closeCount:0')
  expect((saveButton.element() as HTMLButtonElement).disabled).toBe(true)
  expect((closeButton.element() as HTMLButtonElement).disabled).toBe(true)
  await expect.poll(() => fetchMock.mock.calls.length).toBe(2)

  expect(authRecoveryMocks.forceRefreshAccessToken).toHaveBeenCalledTimes(1)
  expect(fetchMock.mock.calls[1]?.[1]).toEqual(expect.objectContaining({
    method: 'POST',
    headers: expect.objectContaining({
      Authorization: 'Bearer fresh-token',
    }),
  }))

  retryResponse.resolve(new Response(JSON.stringify({
    display: 'Invoice INV-001',
    memo: 'Updated after refresh',
  }), {
    status: 200,
    headers: {
      'content-type': 'application/json',
    },
  }))

  await expect.element(view.getByTestId('editor-session-state')).toHaveTextContent('route:/editor;dirty:false;saving:false;closeCount:0')
  await expect.element(view.getByTestId('editor-session-error')).toHaveTextContent('')

  await view.getByRole('button', { name: 'Navigate away' }).click()
  await expect.element(view.getByTestId('leave-guard-target')).toBeVisible()
  expect(router.currentRoute.value.fullPath).toBe('/target')
})

test('keeps unsaved edits recoverable when the session refresh fails during save', async () => {
  await page.viewport(1440, 900)

  const fetchMock = vi.fn()

  vi.stubEnv('VITE_API_BASE_URL', 'https://api.example')
  vi.stubGlobal('fetch', fetchMock)

  authRecoveryMocks.getAccessToken.mockResolvedValueOnce('expired-token')
  authRecoveryMocks.forceRefreshAccessToken.mockRejectedValueOnce(new Error('refresh failed'))

  fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Your session expired' }), {
    status: 401,
    headers: {
      'content-type': 'application/json',
    },
  }))

  const { view } = await renderEditorSessionRecoveryHarness()
  const memoField = document.querySelector('[data-validation-key="memo"] textarea') as HTMLTextAreaElement | null
  expect(memoField).not.toBeNull()

  memoField!.value = 'Unsaved after session expiry'
  memoField!.dispatchEvent(new Event('input', { bubbles: true }))

  await expect.element(view.getByTestId('editor-session-state')).toHaveTextContent('route:/editor;dirty:true;saving:false;closeCount:0')

  await view.getByRole('button', { name: 'Save' }).click()

  await expect.element(view.getByTestId('editor-session-state')).toHaveTextContent('route:/editor;dirty:true;saving:false;closeCount:0')
  await expect.element(view.getByTestId('editor-session-error')).toHaveTextContent('Your session expired')
  expect(fetchMock).toHaveBeenCalledTimes(1)
  expect(authRecoveryMocks.forceRefreshAccessToken).toHaveBeenCalledTimes(1)

  await view.getByRole('button', { name: 'Close' }).click()
  await expect.element(view.getByText('Discard changes?', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Stay', exact: true }).click()
  await expect.element(view.getByTestId('editor-session-state')).toHaveTextContent('route:/editor;dirty:true;saving:false;closeCount:0')

  await view.getByRole('button', { name: 'Close' }).click()
  await view.getByRole('button', { name: 'Leave', exact: true }).click()
  await expect.element(view.getByTestId('editor-session-state')).toHaveTextContent('route:/editor;dirty:true;saving:false;closeCount:1')
})

beforeEach(() => {
  authRecoveryMocks.getAccessToken.mockReset()
  authRecoveryMocks.forceRefreshAccessToken.mockReset()
})

afterEach(() => {
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})
