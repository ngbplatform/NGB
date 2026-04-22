import { describe, expect, it } from 'vitest'

import {
  alignFromDto,
  buildMetadataRegisterColumns,
  buildMetadataRegisterRows,
  formatRegisterValue,
  prettifyRegisterTitle,
  tryFormatDateOnly,
} from '../../../../src/ngb/metadata/register'

describe('metadata register', () => {
  it('maps alignment, prettifies id labels, and formats date-only values', () => {
    expect(alignFromDto(1)).toBe('left')
    expect(alignFromDto(2)).toBe('center')
    expect(alignFromDto(3)).toBe('right')
    expect(prettifyRegisterTitle('Property Id', 'property_id')).toBe('Property')
    expect(prettifyRegisterTitle('Account Id', 'bank_account_id')).toBe('Account')
    expect(tryFormatDateOnly('2026-04-08')).not.toBeNull()
    expect(tryFormatDateOnly('oops')).toBeNull()
  })

  it('formats register values for booleans, numerics, dates, datetimes, references, and objects', () => {
    expect(formatRegisterValue('Boolean', true)).toBe('Yes')
    expect(formatRegisterValue('Int32', 1250.9)).toBe('1,251')
    expect(formatRegisterValue('Money', 1250)).toBe('1,250.00')
    expect(formatRegisterValue('Date', '2026-04-08')).not.toBe('2026-04-08')
    expect(formatRegisterValue('DateTime', '2026-04-08T13:45:00Z')).not.toBe('2026-04-08T13:45:00Z')
    expect(formatRegisterValue('String', { id: 'property-1', display: 'Riverfront Tower' })).toBe('Riverfront Tower')
    expect(formatRegisterValue('Json', { posted: true })).toBe('{"posted":true}')
    expect(formatRegisterValue('String', null)).toBe('—')
  })

  it('builds register columns with option labels and override formatters', () => {
    const columns = buildMetadataRegisterColumns({
      columns: [
        {
          key: 'status',
          label: 'Status',
          dataType: 'String',
          isSortable: true,
          widthPx: 180,
          align: 1,
        },
        {
          key: 'billing_frequency',
          label: 'Billing Frequency',
          dataType: 'Int32',
          isSortable: true,
          widthPx: 180,
          align: 1,
          options: [
            { value: '1', label: 'Manual' },
            { value: '4', label: 'Monthly' },
          ],
        },
        {
          key: 'amount',
          label: 'Amount',
          dataType: 'Money',
          isSortable: false,
          widthPx: null,
          align: 3,
        },
      ],
      optionLabelsByColumnKey: {
        status: new Map([['open', 'Open']]),
      },
      formatOverride: (column, value) => column.key === 'amount' ? `USD ${String(value)}` : null,
    })

    expect(columns[0]).toMatchObject({
      key: 'status',
      title: 'Status',
      width: 180,
      align: 'left',
      sortable: true,
    })
    expect(columns[0]?.format?.('open')).toBe('Open')
    expect(columns[1]?.format?.(4)).toBe('Monthly')
    expect(columns[2]?.format?.(1250)).toBe('USD 1250')
  })

  it('builds register rows from payload fields and allows row extension hooks', () => {
    const rows = buildMetadataRegisterRows({
      items: [
        {
          id: 'doc-1',
          status: 2,
          isDeleted: false,
          isMarkedForDeletion: true,
          payload: {
            fields: {
              number: 'INV-001',
              amount: 1250,
            },
          },
        },
      ],
      columns: [
        { key: 'number', label: 'Number', dataType: 'String', isSortable: true, align: 1 },
        { key: 'amount', label: 'Amount', dataType: 'Money', isSortable: false, align: 3 },
      ],
      mapFieldValue: (column, rawValue) => column.key === 'amount' ? Number(rawValue ?? 0) * 2 : rawValue,
      extendRow: (row) => {
        row.summary = `${String(row.number)}:${String(row.amount)}`
      },
    })

    expect(rows).toEqual([
      {
        key: 'doc-1',
        isDeleted: false,
        isMarkedForDeletion: true,
        status: 2,
        number: 'INV-001',
        amount: 2500,
        summary: 'INV-001:2500',
      },
    ])
  })
})
