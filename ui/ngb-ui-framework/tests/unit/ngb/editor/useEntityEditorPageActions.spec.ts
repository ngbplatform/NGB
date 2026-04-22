import { computed, ref } from 'vue'
import { describe, expect, it } from 'vitest'

import { useEntityEditorPageActions } from '../../../../src/ngb/editor/useEntityEditorPageActions'

function createArgs() {
  const kind = ref<'catalog' | 'document'>('catalog')
  const mode = ref<'page' | 'drawer'>('page')
  const compactTo = ref<string | null>('/catalogs/pm.property?panel=edit&id=property-1')
  const loading = ref(false)
  const saving = ref(false)
  const isNew = ref(false)
  const isMarkedForDeletion = ref(true)
  const canSave = ref(true)
  const canShareLink = ref(true)
  const canOpenAudit = ref(true)
  const canMarkForDeletion = ref(false)
  const canUnmarkForDeletion = ref(true)
  const extraActions = ref([{ key: 'refreshData', title: 'Refresh data', icon: 'refresh' as const }])

  return {
    state: {
      kind,
      mode,
      compactTo,
      loading,
      saving,
      isNew,
      isMarkedForDeletion,
      canSave,
      canShareLink,
      canOpenAudit,
      canMarkForDeletion,
      canUnmarkForDeletion,
      extraActions,
    },
    args: {
      kind: computed(() => kind.value),
      mode: computed(() => mode.value),
      compactTo: computed(() => compactTo.value),
      loading: computed(() => loading.value),
      saving: computed(() => saving.value),
      isNew: computed(() => isNew.value),
      isMarkedForDeletion: computed(() => isMarkedForDeletion.value),
      canSave: computed(() => canSave.value),
      canShareLink: computed(() => canShareLink.value),
      canOpenAudit: computed(() => canOpenAudit.value),
      canMarkForDeletion: computed(() => canMarkForDeletion.value),
      canUnmarkForDeletion: computed(() => canUnmarkForDeletion.value),
      extraActions: computed(() => extraActions.value),
    },
  }
}

describe('entity editor page actions', () => {
  it('builds catalog page actions including restore semantics and extra actions', () => {
    const { args } = createArgs()

    const actions = useEntityEditorPageActions(args)

    expect(actions.value).toEqual([
      {
        key: 'openCompactPage',
        title: 'Open compact page',
        icon: 'panel-right',
        disabled: false,
      },
      {
        key: 'copyShareLink',
        title: 'Share link',
        icon: 'share',
        disabled: false,
      },
      {
        key: 'openAuditLog',
        title: 'Audit log',
        icon: 'history',
        disabled: false,
      },
      {
        key: 'toggleMarkForDeletion',
        title: 'Unmark for deletion',
        icon: 'trash-restore',
        disabled: false,
      },
      {
        key: 'save',
        title: 'Restore to edit',
        icon: 'save',
        disabled: false,
      },
      {
        key: 'refreshData',
        title: 'Refresh data',
        icon: 'refresh',
      },
    ])
  })

  it('returns no page actions for documents or non-page modes and disables actions while busy', () => {
    const { args, state } = createArgs()
    const actions = useEntityEditorPageActions(args)

    state.loading.value = true
    expect(actions.value.map((item) => item.disabled)).toEqual([true, true, true, true, true, undefined])

    state.loading.value = false
    state.kind.value = 'document'
    expect(actions.value).toEqual([])

    state.kind.value = 'catalog'
    state.mode.value = 'drawer'
    expect(actions.value).toEqual([])
  })
})
