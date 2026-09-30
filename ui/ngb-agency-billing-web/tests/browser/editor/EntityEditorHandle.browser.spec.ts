import { vi } from 'vitest'
import { testEntityEditorHandleForwarding } from '../../../../tests/browser/support/entityEditorHandle'

vi.mock('@ngbplatform/ui/editor', async (importOriginal) => {
  const platform = await importOriginal<typeof import('@ngbplatform/ui/editor')>()
  const { createConfiguredEditorStub } = await import('../../../../tests/browser/support/entityEditorHandle')
  return { ...platform, NgbConfiguredEntityEditor: createConfiguredEditorStub() }
})

import AgencyBillingEntityEditor from '../../../src/editor/AgencyBillingEntityEditor.vue'

testEntityEditorHandleForwarding(AgencyBillingEntityEditor, 'ab.sales_invoice')
