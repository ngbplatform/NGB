import { describe, expect, it, vi } from 'vitest'
import { encodeBackTarget, withBackTarget } from '../../../../src/ngb/router/backNavigation'

describe('document navigation helpers', () => {
  it('delegates document urls to configured routing and derives default opening behavior from table parts', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/editor/config')
    const navigation = await import('../../../../src/ngb/editor/documentNavigation')

    config.configureNgbEditor({
      loadDocumentById: vi.fn(),
      loadDocumentEffects: vi.fn(),
      loadDocumentGraph: vi.fn(),
      loadEntityAuditLog: vi.fn(),
      routing: {
        buildDocumentFullPageUrl: (documentType, id) => `/custom/documents/${documentType}/${id ?? 'new'}`,
        buildDocumentCompactPageUrl: (documentType, id) =>
          `/custom/documents/${documentType}?panel=${id ? 'edit' : 'new'}${id ? `&id=${id}` : ''}`,
        buildDocumentEffectsPageUrl: (documentType, id) => `/custom/documents/${documentType}/${id}/effects`,
        buildDocumentFlowPageUrl: (documentType, id) => `/custom/documents/${documentType}/${id}/flow`,
        buildDocumentPrintPageUrl: (documentType, id, options) =>
          `/custom/documents/${documentType}/${id}/print${options?.autoPrint ? '?autoprint=1' : ''}`,
      },
    } as never)

    expect(navigation.documentHasTables({ parts: [{ key: 'lines' }] } as never)).toBe(true)
    expect(navigation.documentHasTables({ parts: [] } as never)).toBe(false)
    expect(navigation.shouldOpenDocumentInFullPageByDefault({ parts: [{ key: 'lines' }] } as never)).toBe(true)
    expect(navigation.buildDocumentFullPageUrl('pm.invoice', 'doc-1')).toBe('/custom/documents/pm.invoice/doc-1')
    expect(navigation.buildDocumentCompactPageUrl('pm.invoice')).toBe('/custom/documents/pm.invoice?panel=new')
    expect(navigation.buildDocumentEffectsPageUrl('pm.invoice', 'doc-1')).toBe('/custom/documents/pm.invoice/doc-1/effects')
    expect(navigation.buildDocumentFlowPageUrl('pm.invoice', 'doc-1')).toBe('/custom/documents/pm.invoice/doc-1/flow')
    expect(navigation.buildDocumentPrintPageUrl('pm.invoice', 'doc-1', { autoPrint: true })).toBe(
      '/custom/documents/pm.invoice/doc-1/print?autoprint=1',
    )
  })

  it('builds fallback targets, resolves navigate-on-create, and flattens form field keys', async () => {
    const navigation = await import('../../../../src/ngb/editor/documentNavigation')

    expect(navigation.buildEntityFallbackCloseTarget('catalog', 'pm.property')).toBe('/catalogs/pm.property')
    expect(navigation.buildEntityFallbackCloseTarget('document', 'pm.invoice')).toBe('/documents/pm.invoice')
    expect(navigation.resolveNavigateOnCreate(undefined, 'page')).toBe(true)
    expect(navigation.resolveNavigateOnCreate(undefined, 'drawer')).toBe(false)
    expect(navigation.resolveNavigateOnCreate(false, 'page')).toBe(false)

    const fields = navigation.listFormFields({
      sections: [
        {
          rows: [
            {
              fields: [{ key: 'number' }, { key: 'memo' }],
            },
            {
              fields: [{ key: 'memo' }, { key: '  ' }, {}],
            },
          ],
        },
      ],
    })

    expect(fields).toEqual([
      { key: 'number' },
      { key: 'memo' },
      { key: 'memo' },
      { key: '  ' },
      {},
    ])
    expect(navigation.formMetadataFieldKeys({
      sections: [
        {
          rows: [
            {
              fields: [{ key: 'number' }, { key: ' memo ' }, { key: '' }],
            },
          ],
        },
      ],
    } as never)).toEqual(['number', 'memo'])
  })

  it('prefers compact editor source targets when reopening documents from related pages', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/editor/config')
    const navigation = await import('../../../../src/ngb/editor/documentNavigation')

    config.configureNgbEditor({
      loadDocumentById: vi.fn(),
      loadDocumentEffects: vi.fn(),
      loadDocumentGraph: vi.fn(),
      loadEntityAuditLog: vi.fn(),
      routing: {
        buildDocumentFullPageUrl: (documentType, id) => `/documents-full/${documentType}/${id ?? 'new'}`,
        buildDocumentCompactPageUrl: (documentType, id) =>
          `/documents/${documentType}?panel=${id ? 'edit' : 'new'}${id ? `&id=${id}` : ''}`,
        buildDocumentEffectsPageUrl: (documentType, id) => `/documents-full/${documentType}/${id}/effects`,
        buildDocumentFlowPageUrl: (documentType, id) => `/documents-full/${documentType}/${id}/flow`,
        buildDocumentPrintPageUrl: (documentType, id) => `/documents-full/${documentType}/${id}/print`,
      },
    } as never)

    const compactSource = '/documents/pm.invoice?search=late&panel=edit&id=doc-1&trash=deleted'
    const fullPageWithCompactBack = withBackTarget('/documents-full/pm.invoice/doc-1', compactSource)

    expect(navigation.resolveCompactDocumentSourceTarget(
      { query: { back: encodeBackTarget(compactSource) } } as never,
      '/documents/pm.invoice?panel=edit&id=doc-1',
    )).toBe(compactSource)

    expect(navigation.resolveCompactDocumentSourceTarget(
      { query: { back: encodeBackTarget(fullPageWithCompactBack) } } as never,
      '/documents/pm.invoice?panel=edit&id=doc-1',
    )).toBe(compactSource)

    expect(navigation.resolveDocumentReopenTarget(
      {
        fullPath: '/documents-full/pm.invoice/doc-1/effects',
        query: { back: encodeBackTarget(fullPageWithCompactBack) },
      } as never,
      'pm.invoice',
      'doc-1',
    )).toBe(compactSource)

    expect(navigation.resolveDocumentReopenTarget(
      {
        fullPath: '/documents-full/pm.invoice/doc-1/effects',
        query: { back: encodeBackTarget('/reports/pm.aging') },
      } as never,
      'pm.invoice',
      'doc-1',
    )).toBe('/reports/pm.aging')
  })
})
