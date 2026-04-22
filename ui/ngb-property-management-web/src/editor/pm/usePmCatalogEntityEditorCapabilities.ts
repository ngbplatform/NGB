import { computed, type ComputedRef, type Ref } from 'vue'

import type { EntityFormModel } from 'ngb-ui-framework'

type UsePmCatalogEntityEditorCapabilitiesArgs = {
  model: Ref<EntityFormModel>
  loading: Ref<boolean>
  saving: Ref<boolean>
  isNew: ComputedRef<boolean>
  isMarkedForDeletion: ComputedRef<boolean>
  isPmPropertyCatalog: ComputedRef<boolean>
}

export function usePmCatalogEntityEditorCapabilities(args: UsePmCatalogEntityEditorCapabilitiesArgs) {
  const canBulkCreateUnits = computed(() => {
    if (!args.isPmPropertyCatalog.value) return false
    if (args.isNew.value) return false
    if (args.loading.value || args.saving.value) return false
    if (args.isMarkedForDeletion.value) return false
    return String(args.model.value.kind ?? '') === 'Building'
  })

  return {
    canBulkCreateUnits,
  }
}
