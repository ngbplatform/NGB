import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h, ref } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { page } from 'vitest/browser'

import NgbEntityEditor from '../../../../src/ngb/editor/NgbEntityEditor.vue'
import NgbEntityEditorDrawerActions from '../../../../src/ngb/editor/NgbEntityEditorDrawerActions.vue'
import { useFeatureStore } from '../../../../src/ngb/features/useFeatureStore'
import { getFeatures } from '../../../../src/ngb/features/api'
import { useAccessStore } from '../../../../src/ngb/security/useAccessStore'
import { contentApi } from '../../../../src/ngb/business-objects/api'

vi.mock('../../../../src/ngb/features/api', () => ({ getFeatures: vi.fn() }))

vi.mock('../../../../src/ngb/business-objects/api', () => ({
  contentApi: {
    summary: vi.fn(),
    attachments: vi.fn(),
    notes: vi.fn(),
  },
  startAttachmentDownload: vi.fn(),
  uploadBytes: vi.fn(),
}))

const editorProps = {
  title: 'Invoice',
  loading: false,
  saving: false,
  isNew: false,
  isMarkedForDeletion: false,
  model: {},
  entityTypeCode: 'invoice',
  auditEntityId: 'object-id',
}

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  const features = useFeatureStore()
  vi.mocked(getFeatures).mockResolvedValue(['Attachments', 'Notes'].map(code => ({
    code, displayName: code, group: 'Attachments & Notes', enabled: true,
  })))
  await features.load()
  useAccessStore().current = {
    userId: null,
    authSubject: 'keycloak-administrator',
    isAuthenticated: true,
    isActive: true,
    isBootstrapAdmin: true,
    accessVersion: 0,
    roles: [],
    permissions: [],
  }
  vi.mocked(contentApi.summary).mockResolvedValue({ attachments: 2, notes: 3 })
  vi.mocked(contentApi.attachments).mockResolvedValue({ items: [], nextCursor: null })
  vi.mocked(contentApi.notes).mockResolvedValue({ items: [], nextCursor: null })
})

test.each(['page', 'drawer'] as const)('document %s places content after output and history, and keeps the drawer open after menu dismissal', async mode => {
  const screen = render(NgbEntityEditor, {
    props: {
      ...editorProps,
      kind: 'document',
      mode,
      documentMoreActionGroups: [
        { key: 'output', label: 'Output', items: [{ key: 'print', title: 'Print', icon: 'printer' }] },
        { key: 'history-and-share', label: 'History & share', items: [{ key: 'audit', title: 'Audit log', icon: 'history' }] },
      ],
    },
  })

  await expect.element(screen.getByTestId('object-content-actions')).not.toBeInTheDocument()
  await screen.getByRole('button', { name: 'More actions' }).click()
  const menu = page.getByRole('menu')
  await expect.element(menu).toHaveTextContent(/Output.*Print.*History & share.*Audit log.*Attachments & Notes.*Attachments.*Notes/s)
  await expect.element(page.getByRole('menuitem', { name: 'Attachments (2)' })).toBeEnabled()
  await page.getByRole('menuitem', { name: 'Notes (3)' }).click()

  await expect.element(page.getByRole('menu')).not.toBeInTheDocument()
  await expect.element(page.getByRole('textbox', { name: 'Add note' })).toBeVisible()
  expect(contentApi.notes).toHaveBeenCalledOnce()
})

test('catalog page uses More and disables content for a user without read permissions', async () => {
  useAccessStore().current!.isBootstrapAdmin = false
  const screen = render(NgbEntityEditor, { props: { ...editorProps, kind: 'catalog', mode: 'page' } })

  await screen.getByRole('button', { name: 'More actions' }).click()
  await expect.element(page.getByText('Attachments & Notes', { exact: true })).toBeVisible()
  await expect.element(page.getByRole('menuitem', { name: 'Attachments (2)' })).toBeDisabled()
  await expect.element(page.getByRole('menuitem', { name: 'Notes (3)' })).toBeDisabled()
  expect(contentApi.notes).not.toHaveBeenCalled()
})

test('catalog drawer exposes reactive content actions to its outer header', async () => {
  const Harness = defineComponent({
    setup() {
      const editor = ref<InstanceType<typeof NgbEntityEditor> | null>(null)
      return () => h('div', [
        h(NgbEntityEditorDrawerActions, {
          flags: {
            canSave: true, isDirty: false, loading: false, saving: false, canExpand: true,
            canDelete: false, canMarkForDeletion: false, canUnmarkForDeletion: false,
            canPost: false, canUnpost: false, canShowAudit: true, canShareLink: true,
          },
          moreGroups: editor.value?.getContentActionGroups() ?? [],
          onAction: (action: string) => editor.value?.handleContentAction(action),
        }),
        h(NgbEntityEditor, { ...editorProps, kind: 'catalog', mode: 'drawer', ref: editor }),
      ])
    },
  })
  const screen = render(Harness)

  await screen.getByRole('button', { name: 'More actions' }).click()
  await page.getByRole('menuitem', { name: 'Attachments (2)' }).click()
  await expect.element(page.getByText('No attachments yet.')).toBeVisible()
  await expect.element(page.getByRole('button', { name: 'Upload file' })).toBeEnabled()
  await expect.element(screen.getByTestId('object-content-actions')).not.toBeInTheDocument()
})

test('new objects and disabled features have no content menu or content requests', async () => {
  const screen = render(NgbEntityEditor, {
    props: { ...editorProps, kind: 'catalog', mode: 'page', isNew: true, auditEntityId: null },
  })
  await expect.element(screen.getByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
  expect(contentApi.summary).not.toHaveBeenCalled()

  screen.unmount()
  useFeatureStore().current = []
  const disabled = render(NgbEntityEditor, { props: { ...editorProps, kind: 'document', mode: 'page' } })
  await expect.element(disabled.getByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
  expect(contentApi.summary).not.toHaveBeenCalled()
})

test.each(['catalog', 'document'] as const)('%s keeps feature-discovery errors until an explicit retry', async kind => {
  const features = useFeatureStore()
  features.reset()
  vi.mocked(getFeatures).mockReset()
  vi.mocked(getFeatures)
    .mockRejectedValueOnce(new Error('Feature discovery unavailable'))
    .mockResolvedValue([{ code: 'Notes', displayName: 'Notes', group: 'Attachments & Notes', enabled: true }])

  const screen = render(NgbEntityEditor, { props: { ...editorProps, kind, mode: 'page' } })

  await screen.getByRole('button', { name: 'More actions' }).click()
  await expect.element(page.getByRole('menuitem', { name: 'Retry available features' })).toBeVisible()
  expect(getFeatures).toHaveBeenCalledOnce()
  expect(features.error).toBe('Feature discovery unavailable')
  expect(contentApi.summary).not.toHaveBeenCalled()

  await screen.rerender({ title: 'Updated title' })
  await expect.element(screen.getByText('Updated title', { exact: true })).toBeVisible()
  expect(getFeatures).toHaveBeenCalledOnce()

  await page.getByRole('menuitem', { name: 'Retry available features' }).click()
  await expect.poll(() => features.isEnabled('Notes')).toBe(true)
  expect(getFeatures).toHaveBeenCalledTimes(2)
  expect(features.error).toBe('')

  await screen.getByRole('button', { name: 'More actions' }).click()
  await expect.element(page.getByRole('menuitem', { name: 'Notes (3)' })).toBeEnabled()
})
