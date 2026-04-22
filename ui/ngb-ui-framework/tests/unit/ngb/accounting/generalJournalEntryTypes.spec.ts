import { describe, expect, it } from 'vitest'

import type {
  GeneralJournalEntryDetailsDto,
  GeneralJournalEntryEditorLineModel,
  ReplaceGeneralJournalEntryLinesRequestDto,
} from '../../../../src/ngb/accounting/generalJournalEntryTypes'

describe('general journal entry types', () => {
  it('supports draft details, line replacement payloads, and editor line models', () => {
    const details: GeneralJournalEntryDetailsDto = {
      document: {
        id: 'gje-1',
        display: 'GJE-0001',
        status: 1,
        isMarkedForDeletion: false,
        number: 'GJE-0001',
      },
      dateUtc: '2026-04-08',
      header: {
        journalType: 1,
        source: 2,
        approvalState: 3,
        autoReverse: false,
        createdAtUtc: '2026-04-08T10:00:00Z',
        updatedAtUtc: '2026-04-08T10:05:00Z',
      },
      lines: [
        {
          lineNo: 1,
          side: 1,
          accountId: 'acc-1',
          amount: 120,
          dimensionSetId: 'dim-set-1',
          dimensions: [
            {
              dimensionId: 'property',
              valueId: 'riverfront',
            },
          ],
        },
      ],
      allocations: [
        {
          entryNo: 1,
          debitLineNo: 1,
          creditLineNo: 2,
          amount: 120,
        },
      ],
    }
    const request: ReplaceGeneralJournalEntryLinesRequestDto = {
      updatedBy: 'tester',
      lines: [
        {
          side: 1,
          accountId: 'acc-1',
          amount: 120,
          dimensions: [
            {
              dimensionId: 'property',
              valueId: 'riverfront',
            },
          ],
        },
      ],
    }
    const editorLine: GeneralJournalEntryEditorLineModel = {
      clientKey: 'line-1',
      side: 1,
      account: {
        id: 'acc-1',
        label: '1010 Cash',
      },
      amount: '120.00',
      memo: '',
      dimensions: {
        property: {
          id: 'riverfront',
          label: 'Riverfront Tower',
        },
      },
    }

    expect(details.lines[0]?.dimensions[0]?.valueId).toBe('riverfront')
    expect(request.lines[0]?.amount).toBe(120)
    expect(editorLine.account?.label).toBe('1010 Cash')
  })
})
