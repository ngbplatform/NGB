import { computed, ref } from 'vue'
import { describe, expect, it } from 'vitest'

import { usePmCatalogEntityEditorCapabilities } from '../../../src/editor/pm/usePmCatalogEntityEditorCapabilities'

describe('property-management catalog editor capabilities', () => {
  it('enables bulk unit creation only for an active persisted building', () => {
    const model = ref<Record<string, unknown>>({ kind: 'Building' })
    const loading = ref(false)
    const saving = ref(false)
    const isNew = ref(false)
    const isMarkedForDeletion = ref(false)
    const isPmPropertyCatalog = ref(true)
    const { canBulkCreateUnits } = usePmCatalogEntityEditorCapabilities({
      model,
      loading,
      saving,
      isNew: computed(() => isNew.value),
      isMarkedForDeletion: computed(() => isMarkedForDeletion.value),
      isPmPropertyCatalog: computed(() => isPmPropertyCatalog.value),
    } as never)

    expect(canBulkCreateUnits.value).toBe(true)
    isPmPropertyCatalog.value = false
    expect(canBulkCreateUnits.value).toBe(false)
    isPmPropertyCatalog.value = true
    isNew.value = true
    expect(canBulkCreateUnits.value).toBe(false)
    isNew.value = false
    loading.value = true
    expect(canBulkCreateUnits.value).toBe(false)
    loading.value = false
    saving.value = true
    expect(canBulkCreateUnits.value).toBe(false)
    saving.value = false
    isMarkedForDeletion.value = true
    expect(canBulkCreateUnits.value).toBe(false)
    isMarkedForDeletion.value = false
    model.value.kind = 'Unit'
    expect(canBulkCreateUnits.value).toBe(false)
    model.value.kind = null
    expect(canBulkCreateUnits.value).toBe(false)
  })
})
