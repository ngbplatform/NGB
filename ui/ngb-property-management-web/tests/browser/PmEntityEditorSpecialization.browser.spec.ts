import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  tags: new Set<string>(),
  allowedActions: new Set<string>(),
  routerReplace: vi.fn(),
  routerPush: vi.fn(),
  navigateBack: vi.fn(),
  ensureCatalogType: vi.fn(),
  ensureDocumentType: vi.fn(),
  runEntityEditorAction: vi.fn(),
  resolveNavigateOnCreate: vi.fn((value: boolean | undefined) => value !== false),
  toastPush: vi.fn(),
  catalogContext: null as Record<string, any> | null,
  documentContext: null as Record<string, any> | null,
  shellProps: null as Record<string, any> | null,
  capabilitiesArgs: null as Record<string, any> | null,
  capabilities: null as Record<string, any> | null,
  persistenceArgs: null as Record<string, any> | null,
  persistence: null as Record<string, any> | null,
  navigationArgs: null as Record<string, any> | null,
  navigation: null as Record<string, any> | null,
  documentActionArgs: null as Record<string, any> | null,
  documentActions: null as Record<string, any> | null,
  headerArgs: null as Record<string, any> | null,
  header: null as Record<string, any> | null,
  lifecycleArgs: null as Record<string, any> | null,
  lifecycle: null as Record<string, any> | null,
  leaveArgs: null as Record<string, any> | null,
  leave: null as Record<string, any> | null,
  commandArgs: null as Record<string, any> | null,
  pageArgs: null as Record<string, any> | null,
  outputArgs: null as Record<string, any> | null,
  errorArgs: null as Record<string, any> | null,
  errorRef: null as { value: unknown } | null,
  setEditorError: vi.fn(),
  normalizeEditorError: vi.fn(() => ({ summary: 'normalized', issues: [] })),
  dismissFieldIssues: vi.fn(),
  dismissLeaseIssues: vi.fn(),
  canBulkCreateUnits: null as { value: boolean } | null,
  leasePartiesRows: null as { value: unknown[] } | null,
  applyInitialParts: vi.fn(),
  applyPersistedParts: vi.fn(),
  buildSaveParts: vi.fn(),
  buildCopyParts: vi.fn(),
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ fullPath: '/documents/pm.lease/current', query: {}, hash: '' }),
  useRouter: () => ({ replace: mocks.routerReplace, push: mocks.routerPush }),
}))

vi.mock('@ngbplatform/ui', async () => {
  const { computed, defineComponent, h, ref, useAttrs } = await import('vue')
  const yes = () => ref(true)
  const no = () => ref(false)

  return {
    NgbEntityEditor: defineComponent({
      name: 'NgbEntityEditor',
      setup() {
        const attrs = useAttrs()
        return () => {
          const normalized = {
            ...attrs,
            afterFormExtensions: attrs.afterFormExtensions ?? attrs['after-form-extensions'],
            dialogExtensions: attrs.dialogExtensions ?? attrs['dialog-extensions'],
            pageActions: attrs.pageActions ?? attrs['page-actions'],
          } as Record<string, any>
          mocks.shellProps = normalized
          const after = normalized.afterFormExtensions as unknown[] | undefined
          const dialogs = normalized.dialogExtensions as unknown[] | undefined
          return h('div', {
            'data-testid': 'pm-editor-shell',
            'data-after-form-count': String(after?.length ?? 0),
            'data-dialog-count': String(dialogs?.length ?? 0),
          })
        }
      },
    }),
    buildCatalogFullPageUrl: (typeCode: string, id: string) => `/catalogs/${typeCode}/${id}`,
    buildDocumentFullPageUrl: (typeCode: string, id: string) => `/documents/${typeCode}/${id}`,
    navigateBack: mocks.navigateBack,
    normalizeDocumentStatusValue: (value: unknown) => Number(value) || 1,
    resolveNavigateOnCreate: mocks.resolveNavigateOnCreate,
    runEntityEditorAction: mocks.runEntityEditorAction,
    stableStringify: (value: unknown) => JSON.stringify(value),
    useMetadataStore: () => ({
      ensureCatalogType: mocks.ensureCatalogType,
      ensureDocumentType: mocks.ensureDocumentType,
    }),
    useLookupStore: () => ({ resolve: vi.fn() }),
    useToasts: () => ({ push: mocks.toastPush }),
    useEntityEditorBusinessContext: (args: Record<string, any>) => ({
      currentEditorContext: () => ({ kind: args.kind.value, typeCode: args.typeCode.value }),
      hasTag: (tag: string) => mocks.tags.has(tag),
    }),
    useEntityEditorCapabilities: (args: Record<string, any>) => {
      mocks.capabilitiesArgs = args
      const value = {
        canOpenAudit: yes(),
        canShareLink: yes(),
        canOpenEffectsPage: yes(),
        canOpenDocumentFlowPage: yes(),
        canPrintDocument: yes(),
        canMarkForDeletion: yes(),
        canUnmarkForDeletion: no(),
        canDelete: yes(),
        canSave: yes(),
        documentStatusLabel: ref('Draft'),
        documentStatusTone: ref('neutral'),
        title: ref('PM record'),
        subtitle: ref(''),
        auditEntityKind: ref('document'),
        auditEntityId: ref('id'),
        auditEntityTitle: ref('PM record'),
        isReadOnly: no(),
      }
      mocks.capabilities = value
      return value
    },
    useEntityEditorLeaveGuard: (args: Record<string, any>) => {
      mocks.leaveArgs = args
      const value = {
        leaveOpen: no(),
        requestNavigate: vi.fn(),
        requestClose: vi.fn(),
        confirmLeave: vi.fn(),
        cancelLeave: vi.fn(),
      }
      mocks.leave = value
      return value
    },
    useEntityEditorPersistence: (args: Record<string, any>) => {
      mocks.persistenceArgs = args
      const value = {
        load: vi.fn().mockResolvedValue(undefined),
        save: vi.fn().mockResolvedValue(undefined),
        markForDeletion: vi.fn().mockResolvedValue(undefined),
        unmarkForDeletion: vi.fn().mockResolvedValue(undefined),
        deleteEntity: vi.fn().mockResolvedValue(undefined),
        loadDocumentEffectsSnapshot: vi.fn().mockImplementation(async () => {
          if (mocks.documentContext) mocks.documentContext.docEffects.value = { entries: ['loaded'] }
        }),
      }
      mocks.persistence = value
      return value
    },
    useEntityEditorNavigationActions: (args: Record<string, any>) => {
      mocks.navigationArgs = args
      const value = {
        auditOpen: no(),
        fallbackCloseTarget: '/fallback',
        copyShareLink: vi.fn().mockResolvedValue(undefined),
        copyDocument: vi.fn(),
        openDocumentPrintPage: vi.fn(),
        openAuditLog: vi.fn(),
        closeAuditLog: vi.fn(),
        openDocumentEffectsPage: vi.fn(),
        openDocumentFlowPage: vi.fn(),
        openFullPage: vi.fn(),
        openCompactPage: vi.fn(),
        closePage: vi.fn(),
      }
      mocks.navigation = value
      return value
    },
    useConfiguredEntityEditorDocumentActions: (args: Record<string, any>) => {
      mocks.documentActionArgs = args
      const value = {
        documentLifecycleActions: ref({ deletion: null, posting: null }),
        extraPrimaryActions: ref([]),
        extraMoreActionGroups: ref([]),
        handleConfiguredAction: vi.fn(),
        requestDocumentAction: vi.fn(),
        isDocumentActionAllowed: (code: string) => mocks.allowedActions.has(code),
        confirmation: ref(null),
        cancelDocumentActionConfirmation: vi.fn(),
        confirmDocumentAction: vi.fn(),
        executingDocumentAction: no(),
        refreshDocumentActions: vi.fn().mockResolvedValue(undefined),
      }
      mocks.documentActions = value
      return value
    },
    useEntityEditorHeaderActions: (args: Record<string, any>) => {
      mocks.headerArgs = args
      const value = {
        documentPrimaryActions: ref([]),
        documentMoreActionGroups: ref([]),
        handleDocumentHeaderAction: vi.fn(),
      }
      mocks.header = value
      return value
    },
    useEntityEditorLifecycleConfirmations: (args: Record<string, any>) => {
      mocks.lifecycleArgs = args
      const value = {
        markConfirmOpen: no(),
        markConfirmMessage: ref('Mark?'),
        requestMarkForDeletion: vi.fn(),
        cancelMarkForDeletion: vi.fn(),
        confirmMarkForDeletion: vi.fn(),
      }
      mocks.lifecycle = value
      return value
    },
    useEntityEditorCommandPalette: (args: Record<string, any>) => {
      mocks.commandArgs = args
    },
    useEntityEditorPageActions: (args: Record<string, any>) => {
      mocks.pageArgs = args
      return computed(() => args.extraActions?.value ?? [])
    },
    useEntityEditorOutputs: (args: Record<string, any>) => {
      mocks.outputArgs = args
      return {
        flags: computed(() => ({
          dirty: args.isDirty.value,
          loading: args.loading.value,
          saving: args.saving.value,
          canExpand: args.canExpand.value,
          canDelete: args.canDelete.value,
          canMarkForDeletion: args.canMarkForDeletion.value,
          canUnmarkForDeletion: args.canUnmarkForDeletion.value,
          canPost: args.canPost.value,
          canUnpost: args.canUnpost.value,
          canOpenAudit: args.canOpenAudit.value,
          canShareLink: args.canShareLink.value,
          canSave: args.canSave.value,
          ...(args.extraFlags?.value ?? {}),
        })),
      }
    },
  }
})

vi.mock('../../src/components/lease/LeaseTenantsGrid.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ name: 'LeaseTenantsGrid', setup: () => () => h('div') }) }
})

vi.mock('../../src/components/property/PmPropertyBulkCreateUnitsDialog.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ name: 'PmPropertyBulkCreateUnitsDialog', setup: () => () => h('div') }) }
})

vi.mock('../../src/editor/entityProfile', () => ({
  PM_EDITOR_TAGS: { PROPERTY_CATALOG: 'property', LEASE_DOCUMENT: 'lease' },
}))

vi.mock('../../src/editor/pm/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: (context: Record<string, any>) => {
    mocks.catalogContext = context
    return { load: vi.fn(), save: vi.fn(), markForDeletion: vi.fn(), unmarkForDeletion: vi.fn(), deleteEntity: vi.fn() }
  },
}))

vi.mock('../../src/editor/pm/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: (context: Record<string, any>) => {
    mocks.documentContext = context
    return { load: vi.fn(), save: vi.fn() }
  },
}))

vi.mock('../../src/editor/pm/useEntityEditorErrorState', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorErrorState: (args: Record<string, any>) => {
      mocks.errorArgs = args
      const error = ref<unknown>(null)
      mocks.errorRef = error
      return {
        error,
        displayedError: error,
        inlineFieldErrors: ref({}),
        leaseTenantValidation: ref({}),
        bannerIssues: ref([]),
        normalizeEditorError: mocks.normalizeEditorError,
        setEditorError: (value: unknown) => {
          error.value = value
          mocks.setEditorError(value)
        },
        dismissFieldIssues: mocks.dismissFieldIssues,
        dismissLeaseIssues: mocks.dismissLeaseIssues,
      }
    },
  }
})

vi.mock('../../src/editor/pm/useEntityEditorLeasePart', async () => {
  const { computed, ref } = await import('vue')
  return {
    useEntityEditorLeasePart: () => {
      const leasePartiesRows = ref<unknown[]>([])
      mocks.leasePartiesRows = leasePartiesRows
      return {
        leasePartiesRows,
        buildCopyParts: mocks.buildCopyParts,
        applyInitialParts: mocks.applyInitialParts,
        applyPersistedParts: mocks.applyPersistedParts,
        buildSaveParts: mocks.buildSaveParts,
        ensureLeasePartiesInitialized: vi.fn(),
        validateLeasePartiesBeforeSave: vi.fn(() => null),
        isLeaseDocument: computed(() => mocks.tags.has('lease')),
      }
    },
  }
})

vi.mock('../../src/editor/pm/usePmCatalogEntityEditorCapabilities', async () => {
  const { ref } = await import('vue')
  return {
    usePmCatalogEntityEditorCapabilities: () => {
      const canBulkCreateUnits = ref(false)
      mocks.canBulkCreateUnits = canBulkCreateUnits
      return { canBulkCreateUnits }
    },
  }
})

import PmEntityEditor from '../../src/editor/pm/PmEntityEditor.vue'

function resetCapturedState() {
  mocks.tags.clear()
  mocks.allowedActions.clear()
  for (const key of [
    'catalogContext', 'documentContext', 'shellProps', 'capabilitiesArgs', 'capabilities',
    'persistenceArgs', 'persistence', 'navigationArgs', 'navigation', 'documentActionArgs',
    'documentActions', 'headerArgs', 'header', 'lifecycleArgs', 'lifecycle', 'leaveArgs',
    'leave', 'commandArgs', 'pageArgs', 'outputArgs', 'errorRef', 'canBulkCreateUnits',
    'leasePartiesRows', 'errorArgs',
  ] as const) mocks[key] = null as never
}

beforeEach(() => {
  vi.clearAllMocks()
  resetCapturedState()
  mocks.ensureCatalogType.mockResolvedValue({
    catalogType: 'pm.property',
    displayName: 'Property',
    form: { sections: [] },
  })
  mocks.ensureDocumentType.mockResolvedValue({
    documentType: 'pm.lease',
    displayName: 'Lease',
    form: { sections: [] },
  })
  mocks.routerReplace.mockResolvedValue(undefined)
  mocks.routerPush.mockResolvedValue(undefined)
})

describe('PM configured editor specialization', () => {
  it('keeps lease extensions, snapshots, validation dismissal, and metadata ports inside PM', async () => {
    mocks.tags.add('lease')
    const wrapper = mount(PmEntityEditor, {
      props: {
        kind: 'document',
        typeCode: 'pm.lease',
        id: 'lease-id',
        initialParts: { parties: { rows: [] } },
      },
    })

    mocks.documentContext!.docMeta.value = {
      documentType: 'pm.lease',
      displayName: 'Lease',
      form: {
        sections: [{ rows: [{ fields: [
          { key: 'subject', label: 'Subject' },
          { key: '', label: 'Ignored' },
          { key: 'blank', label: '   ' },
          null,
        ] }] }],
      },
      parts: [{ partCode: 'parties', title: 'Parties', list: { columns: [] } }],
    }
    expect(mocks.errorArgs!.fieldLabels.value).toEqual({ subject: 'Subject' })
    mocks.documentContext!.docMeta.value = null
    expect(mocks.errorArgs!.fieldLabels.value).toEqual({})
    mocks.documentContext!.docMeta.value = { form: { sections: [{}, { rows: [{}] }] } }
    expect(mocks.errorArgs!.fieldLabels.value).toEqual({})
    mocks.documentContext!.docMeta.value = {
      form: { sections: [{ rows: [{ fields: [{ key: 'numeric', label: 42 }] }] }] },
      parts: [{ partCode: 'parties' }],
    }
    expect(mocks.errorArgs!.fieldLabels.value).toEqual({})
    mocks.documentContext!.model.value = { subject: 'Initial', stable: 'same' }
    await nextTick()
    mocks.documentContext!.resetInitialSnapshot()
    mocks.errorRef!.value = { summary: 'invalid' }
    mocks.documentContext!.model.value = { subject: 'Changed', stable: 'same', extra: true }
    await nextTick()

    expect(wrapper.get('[data-testid="pm-editor-shell"]').attributes('data-after-form-count')).toBe('1')
    expect(mocks.dismissFieldIssues).toHaveBeenCalledWith('subject')
    expect(mocks.dismissFieldIssues).toHaveBeenCalledWith('extra')
    expect(mocks.capabilitiesArgs!.isDraft.value).toBe(true)
    expect(mocks.errorArgs!.isLeaseDocument.value).toBe(true)
    mocks.documentContext!.resetInitialSnapshot()
    mocks.leasePartiesRows!.value = [{ party_id: 'party-1' }]
    await nextTick()
    expect((wrapper.vm as any).getIsDirty()).toBe(true)
    expect(mocks.dismissLeaseIssues).toHaveBeenCalled()
    mocks.dismissLeaseIssues.mockClear()
    mocks.leasePartiesRows!.value = [{ party_id: 'party-1' }]
    await nextTick()
    expect(mocks.dismissLeaseIssues).not.toHaveBeenCalled()

    mocks.persistenceArgs!.loading.value = true
    mocks.leasePartiesRows!.value = [{ party_id: 'party-2' }]
    await nextTick()
    mocks.persistenceArgs!.loading.value = false
    mocks.persistenceArgs!.saving.value = true
    mocks.leasePartiesRows!.value = [{ party_id: 'party-3' }]
    await nextTick()
    mocks.persistenceArgs!.saving.value = false
    mocks.errorRef!.value = null
    mocks.leasePartiesRows!.value = [{ party_id: 'party-4' }]
    await nextTick()

    const extension = mocks.shellProps!.afterFormExtensions[0]
    extension.componentRef({ focus: true })
    extension.props['onUpdate:modelValue']([{ party_id: 'party-2' }])
    expect(mocks.leasePartiesRows!.value).toEqual([{ party_id: 'party-2' }])
    expect(extension.props).toMatchObject({ readonly: false, errors: {} })

    await mocks.documentContext!.ensureDocumentMetadata('pm.lease')
    expect(mocks.ensureDocumentType).toHaveBeenCalledWith('pm.lease')
    expect(mocks.documentContext).toMatchObject({ leaseEditor: expect.any(Object) })
    wrapper.unmount()
  })

  it('projects property bulk-create actions, dialog updates, output flags, and guards', async () => {
    mocks.tags.add('property')
    const wrapper = mount(PmEntityEditor, {
      props: {
        kind: 'catalog',
        typeCode: 'pm.property',
        id: 'property-id',
        initialFields: { display: 'Main property' },
        expandTo: '/expanded',
      },
    })
    mocks.catalogContext!.catalogMeta.value = { form: { sections: [] } }
    mocks.catalogContext!.model.value = { display: 'Main property' }

    ;(wrapper.vm as any).openBulkCreateUnitsWizard()
    expect(mocks.shellProps!.dialogExtensions[0].props.open).toBe(false)
    mocks.canBulkCreateUnits!.value = true
    await nextTick()

    expect(mocks.shellProps!.pageActions).toEqual([
      expect.objectContaining({ key: 'openBulkCreateUnits', title: 'Bulk create units' }),
    ])
    expect(wrapper.get('[data-testid="pm-editor-shell"]').attributes('data-dialog-count')).toBe('1')
    const dialog = mocks.shellProps!.dialogExtensions[0]
    expect(dialog.props).toMatchObject({
      buildingId: 'property-id',
      buildingDisplay: 'Main property',
      open: false,
    })

    ;(wrapper.vm as any).openBulkCreateUnitsWizard()
    await nextTick()
    expect(mocks.shellProps!.dialogExtensions[0].props.open).toBe(true)
    mocks.shellProps!.dialogExtensions[0].props['onUpdate:open'](false)
    await nextTick()
    expect(mocks.shellProps!.dialogExtensions[0].props.open).toBe(false)
    expect((wrapper.vm as any).getFlags()).toMatchObject({ bulkCreateUnits: true, canExpand: true })
    wrapper.unmount()
  })

  it('routes create callbacks for catalog and document editors and emits persistence lifecycle events', async () => {
    const catalog = mount(PmEntityEditor, {
      props: { kind: 'catalog', typeCode: 'pm.property', id: null, navigateOnCreate: true },
    })
    await mocks.catalogContext!.ensureCatalogMetadata('pm.property')
    await mocks.catalogContext!.onCreated('catalog-created')
    mocks.catalogContext!.onSaved()
    expect(mocks.routerReplace).toHaveBeenCalledWith('/catalogs/pm.property/catalog-created')
    expect(catalog.emitted('created')).toEqual([['catalog-created']])
    expect(catalog.emitted('saved')).toEqual([[]])
    mocks.persistenceArgs!.emitChanged('save')
    mocks.persistenceArgs!.emitDeleted()
    mocks.persistenceArgs!.onMarkedForDeletion()
    mocks.persistenceArgs!.onUnmarkedForDeletion()
    expect(catalog.emitted('changed')).toEqual([['save']])
    expect(catalog.emitted('deleted')).toEqual([[]])
    expect(mocks.toastPush).toHaveBeenCalledTimes(2)
    catalog.unmount()

    mocks.resolveNavigateOnCreate.mockReturnValueOnce(false)
    const document = mount(PmEntityEditor, {
      props: { kind: 'document', typeCode: 'pm.lease', id: null, navigateOnCreate: undefined },
    })
    await mocks.documentContext!.onCreated('document-no-navigation')
    expect(mocks.routerReplace).not.toHaveBeenCalledWith('/documents/pm.lease/document-no-navigation')
    mocks.resolveNavigateOnCreate.mockReturnValueOnce(true)
    await mocks.documentContext!.onCreated('document-created')
    expect(mocks.routerReplace).toHaveBeenCalledWith('/documents/pm.lease/document-created')
    document.unmount()
  })

  it('dispatches every document and catalog lifecycle branch through the correct port', async () => {
    const document = mount(PmEntityEditor, {
      props: { kind: 'document', typeCode: 'pm.lease', id: 'lease-id' },
    })
    mocks.allowedActions.add('unmark_for_deletion')
    ;(document.vm as any).toggleMarkForDeletion()
    expect(mocks.documentActions!.requestDocumentAction).toHaveBeenLastCalledWith('unmark_for_deletion')
    mocks.allowedActions.delete('unmark_for_deletion')
    ;(document.vm as any).toggleMarkForDeletion()
    expect(mocks.documentActions!.requestDocumentAction).toHaveBeenLastCalledWith('mark_for_deletion')
    mocks.allowedActions.add('unpost')
    ;(document.vm as any).togglePost()
    expect(mocks.documentActions!.requestDocumentAction).toHaveBeenLastCalledWith('unpost')
    mocks.allowedActions.delete('unpost')
    ;(document.vm as any).togglePost()
    await (document.vm as any).markForDeletion()
    await (document.vm as any).unmarkForDeletion()
    await (document.vm as any).post()
    await (document.vm as any).unpost()
    expect(mocks.documentActions!.requestDocumentAction.mock.calls.map((entry: unknown[]) => entry[0])).toEqual(
      expect.arrayContaining(['post', 'mark_for_deletion', 'unmark_for_deletion']),
    )
    document.unmount()

    const catalog = mount(PmEntityEditor, {
      props: { kind: 'catalog', typeCode: 'pm.property', id: 'property-id' },
    })
    mocks.capabilities!.canUnmarkForDeletion.value = true
    ;(catalog.vm as any).toggleMarkForDeletion()
    expect(mocks.persistence!.unmarkForDeletion).toHaveBeenCalled()
    mocks.capabilities!.canUnmarkForDeletion.value = false
    mocks.capabilities!.canMarkForDeletion.value = true
    ;(catalog.vm as any).toggleMarkForDeletion()
    expect(mocks.lifecycle!.requestMarkForDeletion).toHaveBeenCalled()
    await (catalog.vm as any).markForDeletion()
    await (catalog.vm as any).unmarkForDeletion()
    expect(mocks.persistence!.markForDeletion).toHaveBeenCalled()
    expect(mocks.persistence!.unmarkForDeletion).toHaveBeenCalled()
    catalog.unmount()
  })

  it('executes dirty-post preparation and applies action documents atomically', async () => {
    const wrapper = mount(PmEntityEditor, {
      props: { kind: 'document', typeCode: 'pm.lease', id: 'lease-id' },
    })
    const beforeExecute = mocks.documentActionArgs!.beforeExecute
    expect(await beforeExecute('unpost')).toBe(true)
    expect(await beforeExecute('post')).toBe(true)

    mocks.documentContext!.model.value = { memo: 'before' }
    mocks.documentContext!.resetInitialSnapshot()
    mocks.documentContext!.model.value = { memo: 'dirty' }
    mocks.persistence!.save.mockImplementationOnce(async () => {
      mocks.errorRef!.value = null
      mocks.documentContext!.resetInitialSnapshot()
    })
    expect(await beforeExecute('post')).toEqual({ proceed: true, refreshState: true })
    expect(mocks.persistence!.save).toHaveBeenCalled()

    mocks.documentContext!.model.value = { memo: 'dirty-again' }
    mocks.errorRef!.value = { summary: 'save failed' }
    expect(await beforeExecute('post')).toEqual({ proceed: false, refreshState: true })

    mocks.documentActionArgs!.applyActionDocument({
      status: 2,
      payload: { fields: { memo: 'server' } },
    })
    expect(mocks.documentContext!.model.value).toEqual({ memo: 'server' })
    expect(wrapper.emitted('changed')).toEqual([[]])
    mocks.documentActionArgs!.applyActionDocument({ status: 2, payload: null })
    expect(mocks.documentContext!.model.value).toEqual({})
    wrapper.unmount()
  })

  it('refreshes document actions on status changes and reports refresh failures', async () => {
    const wrapper = mount(PmEntityEditor, {
      props: { kind: 'document', typeCode: 'pm.lease', id: 'lease-id' },
    })
    mocks.documentContext!.doc.value = { status: 2 }
    await nextTick()
    expect(mocks.documentActions!.refreshDocumentActions).toHaveBeenCalled()

    mocks.documentActions!.refreshDocumentActions.mockRejectedValueOnce(new Error('refresh failed'))
    mocks.documentContext!.doc.value = { status: 1 }
    await nextTick()
    await nextTick()
    expect(mocks.normalizeEditorError).toHaveBeenCalledWith(expect.any(Error))
    expect(mocks.setEditorError).toHaveBeenCalledWith(expect.objectContaining({ summary: 'normalized' }))

    await wrapper.setProps({ kind: 'catalog' })
    mocks.documentContext!.doc.value = { status: 2 }
    await nextTick()
    const calls = mocks.documentActions!.refreshDocumentActions.mock.calls.length
    await wrapper.setProps({ id: null })
    mocks.documentContext!.doc.value = { status: 1 }
    await nextTick()
    expect(mocks.documentActions!.refreshDocumentActions).toHaveBeenCalledTimes(calls)
    wrapper.unmount()
  })

  it('wires header, palette, navigation, confirmations, back, and close events', async () => {
    const document = mount(PmEntityEditor, {
      props: { kind: 'document', typeCode: 'pm.lease', id: 'lease-id', closeTo: '/close' },
    })
    mocks.headerArgs!.onUnhandledAction('custom')
    expect(mocks.documentActions!.handleConfiguredAction).toHaveBeenCalledWith('custom')
    mocks.shellProps!.onAction('save')
    expect(mocks.header!.handleDocumentHeaderAction).toHaveBeenCalledWith('save')
    expect(mocks.commandArgs).toMatchObject({ typeCode: expect.any(Object), title: expect.any(Object) })
    mocks.shellProps!.onBack()
    expect(mocks.navigateBack).toHaveBeenCalledWith(expect.anything(), expect.anything(), '/close')
    mocks.shellProps!.onClose()
    expect(mocks.navigation!.closePage).toHaveBeenCalled()
    mocks.shellProps!.onCloseAuditLog()
    mocks.shellProps!.onCancelLeave()
    mocks.shellProps!.onConfirmLeave()
    mocks.shellProps!.onCancelMarkForDeletion()
    mocks.shellProps!.onConfirmMarkForDeletion()
    mocks.shellProps!.onCancelDocumentAction()
    mocks.shellProps!.onConfirmDocumentAction()
    mocks.leaveArgs!.onClose()
    expect(mocks.navigation!.closeAuditLog).toHaveBeenCalled()
    expect(mocks.leave!.cancelLeave).toHaveBeenCalled()
    expect(mocks.leave!.confirmLeave).toHaveBeenCalled()
    expect(mocks.lifecycle!.cancelMarkForDeletion).toHaveBeenCalled()
    expect(mocks.lifecycle!.confirmMarkForDeletion).toHaveBeenCalled()
    expect(mocks.documentActions!.cancelDocumentActionConfirmation).toHaveBeenCalled()
    expect(mocks.documentActions!.confirmDocumentAction).toHaveBeenCalled()
    expect(document.emitted('close')).toEqual([[]])
    document.unmount()

    const catalog = mount(PmEntityEditor, {
      props: { kind: 'catalog', typeCode: 'pm.property', id: 'property-id' },
    })
    mocks.shellProps!.onAction('openAuditLog')
    expect(mocks.runEntityEditorAction).toHaveBeenCalledWith('openAuditLog', expect.any(Object))
    const handlers = mocks.runEntityEditorAction.mock.calls[0]?.[1]
    handlers.openBulkCreateUnits()
    handlers.openCompactPage()
    await handlers.copyShareLink()
    handlers.openAuditLog()
    handlers.toggleMarkForDeletion()
    await handlers.save()
    expect(mocks.navigation!.openCompactPage).toHaveBeenCalled()
    expect(mocks.navigation!.copyShareLink).toHaveBeenCalled()
    expect(mocks.navigation!.openAuditLog).toHaveBeenCalled()
    expect(mocks.persistence!.save).toHaveBeenCalled()
    catalog.unmount()
  })

  it('evaluates every reactive composition port and fallback branch', async () => {
    const wrapper = mount(PmEntityEditor, {
      props: {
        kind: 'document',
        typeCode: 'pm.lease',
        id: 'lease-id',
        mode: 'drawer',
        initialFields: { memo: 'initial' },
        initialParts: { parties: { rows: [] } },
        expandTo: '/expand',
        compactTo: '/compact',
      },
    })

    expect(mocks.documentContext!.initialFields.value).toEqual({ memo: 'initial' })
    expect(mocks.documentContext!.initialParts.value).toEqual({ parties: { rows: [] } })
    expect(mocks.navigationArgs!.mode.value).toBe('drawer')
    expect(mocks.navigationArgs!.expandTo.value).toBe('/expand')
    expect(mocks.navigationArgs!.compactTo.value).toBe('/compact')
    expect(mocks.navigationArgs!.closeTo.value).toBeNull()
    expect(mocks.documentActionArgs!.currentId.value).toBe('lease-id')
    expect(mocks.documentActionArgs!.loading.value).toBe(false)
    expect(mocks.documentActionArgs!.saving.value).toBe(false)
    expect(mocks.headerArgs!.loading.value).toBe(false)
    expect(mocks.headerArgs!.saving.value).toBe(false)
    expect(mocks.pageArgs!.loading.value).toBe(false)
    expect(mocks.pageArgs!.saving.value).toBe(false)

    mocks.persistenceArgs!.loading.value = true
    expect(mocks.documentActionArgs!.loading.value).toBe(true)
    expect(mocks.headerArgs!.loading.value).toBe(true)
    expect(mocks.pageArgs!.loading.value).toBe(true)
    mocks.persistenceArgs!.loading.value = false
    mocks.persistenceArgs!.saving.value = true
    expect(mocks.documentActionArgs!.saving.value).toBe(true)
    expect(mocks.headerArgs!.saving.value).toBe(true)
    expect(mocks.pageArgs!.saving.value).toBe(true)
    mocks.persistenceArgs!.saving.value = false
    mocks.documentActions!.executingDocumentAction.value = true
    expect(mocks.headerArgs!.saving.value).toBe(true)
    mocks.documentActions!.executingDocumentAction.value = false

    mocks.shellProps!.onBack()
    expect(mocks.navigateBack).toHaveBeenCalledWith(expect.anything(), expect.anything(), '/fallback')
    wrapper.unmount()

    const catalog = mount(PmEntityEditor, {
      props: { kind: 'catalog', typeCode: 'pm.property', id: 'property-id' },
    })
    mocks.catalogContext!.model.value = { display: 'same' }
    mocks.catalogContext!.resetInitialSnapshot()
    mocks.catalogContext!.model.value = { display: 'changed' }
    expect((catalog.vm as any).getIsDirty()).toBe(true)
    mocks.capabilities!.canUnmarkForDeletion.value = false
    mocks.capabilities!.canMarkForDeletion.value = false
    await nextTick()
    ;(catalog.vm as any).toggleMarkForDeletion()
    expect(mocks.lifecycle!.requestMarkForDeletion).not.toHaveBeenCalled()
    catalog.unmount()
  })

  it('exposes navigation, effects, persistence state, and prop synchronization', async () => {
    const wrapper = mount(PmEntityEditor, {
      props: { kind: 'document', typeCode: 'pm.lease', id: 'lease-id' },
    })
    await (wrapper.vm as any).save()
    await (wrapper.vm as any).load()
    ;(wrapper.vm as any).openFullPage()
    ;(wrapper.vm as any).openCompactPage()
    ;(wrapper.vm as any).closePage()
    await (wrapper.vm as any).deleteEntity()
    await (wrapper.vm as any).copyShareLink()
    ;(wrapper.vm as any).copyDocument()
    ;(wrapper.vm as any).printDocument()
    ;(wrapper.vm as any).openAuditLog()
    ;(wrapper.vm as any).openAudit()
    ;(wrapper.vm as any).closeAuditLog()
    expect((wrapper.vm as any).getDocumentEffects()).toBeNull()
    expect(await (wrapper.vm as any).reloadDocumentEffects()).toEqual({ entries: ['loaded'] })
    expect((wrapper.vm as any).getIsDirty()).toBe(false)
    expect((wrapper.vm as any).getCanSave()).toBe(true)
    expect((wrapper.vm as any).getFlags()).toEqual(expect.any(Object))
    expect(mocks.navigation!.openFullPage).toHaveBeenCalled()
    expect(mocks.navigation!.openCompactPage).toHaveBeenCalled()
    expect(mocks.navigation!.openDocumentPrintPage).toHaveBeenCalled()

    await wrapper.setProps({ id: null })
    expect(await (wrapper.vm as any).reloadDocumentEffects()).toBeNull()
    await wrapper.setProps({ kind: 'catalog', id: 'catalog-id' })
    expect(await (wrapper.vm as any).reloadDocumentEffects()).toBeNull()
    wrapper.unmount()
  })
})
