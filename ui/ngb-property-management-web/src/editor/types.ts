import type { EntityEditorHandle } from '@ngbplatform/ui'

export type PmEntityEditorHandle = EntityEditorHandle & {
  openBulkCreateUnitsWizard: () => void
}
