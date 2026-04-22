import { describe, expect, it } from 'vitest'

import type {
  ChartOfAccountEditorShellState,
  ChartOfAccountsMetadataDto,
  ChartOfAccountsPageDto,
} from '../../../../src/ngb/accounting/types'

describe('accounting types', () => {
  it('supports chart of accounts page and metadata contracts', () => {
    const metadata: ChartOfAccountsMetadataDto = {
      accountTypeOptions: [
        { value: 'asset', label: 'Asset' },
      ],
      cashFlowRoleOptions: [
        {
          value: 'operating',
          label: 'Operating',
          supportsLineCode: true,
          requiresLineCode: false,
        },
      ],
      cashFlowLineOptions: [
        {
          value: 'cf-operating-001',
          label: 'Rent collections',
          section: 'Operating',
          allowedRoles: ['operating'],
        },
      ],
    }
    const page: ChartOfAccountsPageDto = {
      items: [
        {
          accountId: 'acc-1',
          code: '1010',
          name: 'Cash',
          accountType: 'asset',
          isActive: true,
          isDeleted: false,
          isMarkedForDeletion: false,
        },
      ],
      offset: 0,
      limit: 50,
      total: 1,
    }
    const shellState: ChartOfAccountEditorShellState = {
      hideHeader: false,
      flushBody: true,
    }

    expect(metadata.cashFlowRoleOptions[0]?.supportsLineCode).toBe(true)
    expect(page.items[0]?.code).toBe('1010')
    expect(shellState.flushBody).toBe(true)
  })
})
