import { describe, expect, it, vi } from 'vitest'

describe('editor config', () => {
  it('throws when the editor framework is not configured yet', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/editor/config')

    expect(() => config.getConfiguredNgbEditor()).toThrow(
      'NGB editor framework is not configured. Call configureNgbEditor(...) during app bootstrap.',
    )
    expect(config.maybeGetConfiguredNgbEditor()).toBeNull()
  })

  it('uses default routing builders until overrides are configured', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/editor/config')

    expect(config.resolveNgbEditorRouting()).toMatchObject({
      buildCatalogListUrl: expect.any(Function),
      buildCatalogFullPageUrl: expect.any(Function),
      buildCatalogCompactPageUrl: expect.any(Function),
      buildDocumentFullPageUrl: expect.any(Function),
      buildDocumentCompactPageUrl: expect.any(Function),
      buildDocumentEffectsPageUrl: expect.any(Function),
      buildDocumentFlowPageUrl: expect.any(Function),
      buildDocumentPrintPageUrl: expect.any(Function),
    })

    const routing = config.resolveNgbEditorRouting()
    expect(routing.buildCatalogListUrl('pm.property')).toBe('/catalogs/pm.property')
    expect(routing.buildCatalogFullPageUrl('pm.property', 'cat/1')).toBe('/catalogs/pm.property/cat%2F1')
    expect(routing.buildCatalogCompactPageUrl('pm.property')).toBe('/catalogs/pm.property?panel=new')
    expect(routing.buildDocumentFullPageUrl('pm.invoice', 'doc/1')).toBe('/documents/pm.invoice/doc%2F1')
    expect(routing.buildDocumentCompactPageUrl('pm.invoice')).toBe('/documents/pm.invoice?panel=new')
    expect(routing.buildDocumentEffectsPageUrl('pm.invoice', 'doc/1')).toBe('/documents/pm.invoice/doc%2F1/effects')
    expect(routing.buildDocumentFlowPageUrl('pm.invoice', 'doc/1')).toBe('/documents/pm.invoice/doc%2F1/flow')
    expect(routing.buildDocumentPrintPageUrl('pm.invoice', 'doc/1', { autoPrint: true })).toBe(
      '/documents/pm.invoice/doc%2F1/print?autoprint=1',
    )
  })

  it('returns configured routing/profile/action helpers and merges audit/effects/print overrides', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/editor/config')

    const sanitizeModelForEditing = vi.fn()
    const syncComputedDisplay = vi.fn()
    const resolveDocumentActions = vi.fn(() => [{
      item: {
        key: 'share',
        title: 'Share',
        icon: 'share',
      },
      run: vi.fn(),
    }])

    const frameworkConfig = {
      routing: {
        buildCatalogListUrl: vi.fn((catalogType: string) => `/custom/catalogs/${catalogType}`),
        buildCatalogFullPageUrl: vi.fn((catalogType: string, id?: string | null) => `/custom/catalogs/${catalogType}/${id ?? 'new'}`),
      },
      loadDocumentById: vi.fn(),
      loadDocumentEffects: vi.fn(),
      loadDocumentGraph: vi.fn(),
      loadEntityAuditLog: vi.fn(),
      audit: {
        hiddenFieldNames: ['internal_code'],
        explicitFieldLabels: {
          amount: 'Amount',
        },
      },
      effects: {
        showDimensionSetIds: true,
      },
      print: {
        hideAuditFields: true,
      },
      resolveEntityProfile: vi.fn(() => ({
        sanitizeWatchFields: ['memo'],
        sanitizeModelForEditing,
        syncComputedDisplay,
      })),
      resolveDocumentActions,
    }

    config.configureNgbEditor(frameworkConfig as never)

    expect(config.getConfiguredNgbEditor()).toBe(frameworkConfig)
    expect(config.resolveNgbEditorRouting().buildCatalogListUrl('pm.property')).toBe('/custom/catalogs/pm.property')
    expect(config.resolveNgbEditorRouting().buildDocumentFullPageUrl('pm.invoice')).toBe('/documents/pm.invoice/new')

    const context = {
      kind: 'document',
      typeCode: 'pm.invoice',
      mode: 'page',
      status: 1,
    }
    const model = {
      memo: 'hello',
    }

    config.sanitizeNgbEditorModelForEditing(context as never, model as never)
    config.syncNgbEditorComputedDisplay(context as never, model as never)

    expect(sanitizeModelForEditing).toHaveBeenCalledWith({ context, model })
    expect(syncComputedDisplay).toHaveBeenCalledWith({ context, model })
    expect(config.resolveNgbEditorDocumentActions({
      context,
      documentId: 'doc-1',
      model,
      uiEffects: null,
      loading: false,
      saving: false,
      navigate: vi.fn(),
    } as never)).toHaveLength(1)

    const auditBehavior = config.resolveNgbEditorAuditBehavior({
      hiddenFieldNames: ['runtime_hidden'],
      explicitFieldLabels: {
        memo: 'Memo',
      },
    })

    expect(auditBehavior.hiddenFieldNames).toEqual([
      'created_at_utc',
      'updated_at_utc',
      'deleted_at_utc',
      'marked_for_deletion_at_utc',
      'internal_code',
      'runtime_hidden',
    ])
    expect(auditBehavior.explicitFieldLabels).toEqual({
      amount: 'Amount',
      memo: 'Memo',
    })
    expect(config.resolveNgbEditorEffectsBehavior({ preferAccountCodes: true } as never)).toEqual({
      showDimensionSetIds: true,
      preferAccountCodes: true,
    })
    expect(config.resolveNgbEditorPrintBehavior({ includeSystemFields: true } as never)).toEqual({
      hideAuditFields: true,
      includeSystemFields: true,
    })
  })
})
