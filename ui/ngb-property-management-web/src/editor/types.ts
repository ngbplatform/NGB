import type { EntityEditorHandle } from 'ngb-ui-framework'

export type PmEntityEditorHandle = EntityEditorHandle & {
  openBulkCreateUnitsWizard: () => void
}
