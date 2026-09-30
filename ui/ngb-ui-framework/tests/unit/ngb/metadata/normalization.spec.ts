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

  it('normalizes string, unknown, invalid numeric, and omitted nested values safely', () => {
    const metadata = normalizeCatalogTypeMetadata({
      catalogType: 'pm.sparse',
      displayName: 'Sparse',
      kind: 1,
      list: {
        columns: [
          { key: 'trimmed', label: 'Trimmed', dataType: ' Date ', isSortable: true, align: 1 },
          { key: 'blank', label: 'Blank', dataType: ' ', isSortable: true, align: 1 },
          { key: 'unknown_number', label: 'Unknown number', dataType: 99, isSortable: true, align: 1 },
          { key: 'invalid_number', label: 'Invalid number', dataType: Number.NaN, isSortable: true, align: 1 },
          { key: 'invalid_object', label: 'Invalid object', dataType: {} as never, isSortable: true, align: 1 },
        ],
        filters: undefined,
      },
      form: {
        sections: [
          { rows: [{ fields: undefined }] },
          { rows: undefined },
        ],
      },
      parts: [
        { partCode: 'empty', title: 'Empty', list: null as never },
      ],
    })

    expect(metadata.list?.columns.map((column) => column.dataType)).toEqual([
      'Date',
      'Unknown',
      'Unknown',
      'Unknown',
      'Unknown',
    ])
    expect(metadata.list?.filters).toEqual([])
    expect(metadata.form?.sections[0]?.rows?.[0]?.fields).toEqual([])
    expect(metadata.parts?.[0]?.list).toBeNull()

    const omitted = normalizeDocumentTypeMetadata({
      documentType: 'pm.empty',
      displayName: 'Empty',
      kind: 2,
      list: { columns: undefined, filters: undefined },
      form: { sections: undefined },
      parts: undefined,
    })
    expect(omitted.list?.columns).toEqual([])
    expect(omitted.list?.filters).toEqual([])
    expect(omitted.form?.sections).toEqual([])
    expect(omitted.parts).toEqual([])

    expect(normalizeCatalogTypeMetadata({
      catalogType: 'pm.empty-catalog',
      displayName: 'Empty Catalog',
      kind: 1,
      parts: undefined,
    }).parts).toEqual([])
  })
})
