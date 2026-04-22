import { describe, expect, it } from 'vitest'

import {
  normalizeCatalogTypeMetadata,
  normalizeDocumentTypeMetadata,
} from '../../../../src/ngb/metadata/normalization'

describe('metadata normalization', () => {
  it('normalizes legacy numeric data types across catalog list, form, and parts', () => {
    const metadata = normalizeCatalogTypeMetadata({
      catalogType: 'pm.property',
      displayName: 'Properties',
      kind: 1,
      list: {
        columns: [
          { key: 'name', label: 'Name', dataType: 1, isSortable: true, align: 1 },
          { key: 'rent', label: 'Rent', dataType: 7, isSortable: false, align: 3 },
        ],
        filters: [
          { key: 'is_active', label: 'Active', dataType: 4 },
        ],
      },
      form: {
        sections: [
          {
            title: 'Main',
            rows: [
              {
                fields: [
                  { key: 'opened_on', label: 'Opened', dataType: 5, uiControl: 0, isRequired: false, isReadOnly: false },
                ],
              },
            ],
          },
        ],
      },
      parts: [
        {
          partCode: 'units',
          title: 'Units',
          list: {
            columns: [
              { key: 'count', label: 'Count', dataType: 2, isSortable: true, align: 2 },
            ],
          },
        },
      ],
    })

    expect(metadata.list?.columns[0]?.dataType).toBe('String')
    expect(metadata.list?.columns[1]?.dataType).toBe('Money')
    expect(metadata.list?.filters?.[0]?.dataType).toBe('Boolean')
    expect(metadata.form?.sections[0]?.rows[0]?.fields[0]?.dataType).toBe('Date')
    expect(metadata.parts?.[0]?.list.columns[0]?.dataType).toBe('Int32')
  })

  it('preserves nullish nested metadata while normalizing document metadata data types', () => {
    const metadata = normalizeDocumentTypeMetadata({
      documentType: 'pm.invoice',
      displayName: 'Invoices',
      kind: 2,
      list: {
        columns: [
          { key: 'posted_at', label: 'Posted At', dataType: 6, isSortable: true, align: 1 },
        ],
        filters: null,
      },
      form: null,
      parts: [
        {
          partCode: 'lines',
          title: 'Lines',
          list: {
            columns: [
              { key: 'amount', label: 'Amount', dataType: 3, isSortable: false, align: 3 },
            ],
            filters: [
              { key: 'account_id', label: 'Account', dataType: 8 },
            ],
          },
        },
      ],
    })

    expect(metadata.list?.columns[0]?.dataType).toBe('DateTime')
    expect(metadata.form).toBeNull()
    expect(metadata.parts?.[0]?.list.columns[0]?.dataType).toBe('Decimal')
    expect(metadata.parts?.[0]?.list.filters?.[0]?.dataType).toBe('Guid')
  })
})
