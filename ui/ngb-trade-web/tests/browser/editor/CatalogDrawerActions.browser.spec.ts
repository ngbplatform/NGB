import { vi } from 'vitest'

// Exercise the public HTTP client without contacting an identity provider.
vi.mock('keycloak-js', () => ({
  default: class {
    authenticated = false
    async init() { return false }
  },
}))

import TradeEntityEditor from '../../../src/editor/TradeEntityEditor.vue'
import { testCatalogDrawerActions } from '../../../../tests/browser/support/catalogDrawerActions'

testCatalogDrawerActions(TradeEntityEditor, ['trd.party'])
