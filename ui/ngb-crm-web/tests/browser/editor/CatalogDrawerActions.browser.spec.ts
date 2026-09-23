import { vi } from 'vitest'

// Exercise the public HTTP client without contacting an identity provider.
vi.mock('keycloak-js', () => ({
  default: class {
    authenticated = false
    async init() { return false }
  },
}))

import CRMEntityEditor from '../../../src/editor/CRMEntityEditor.vue'
import { testCatalogDrawerActions } from '../../../../tests/browser/support/catalogDrawerActions'

testCatalogDrawerActions(CRMEntityEditor, ['crm.account', 'crm.contact', 'crm.product', 'crm.opportunity_stage'])
