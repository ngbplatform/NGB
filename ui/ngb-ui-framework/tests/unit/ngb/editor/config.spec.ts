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
    expect(routing.buildCatalogFullPageUrl('pm.property', null)).toBe('/catalogs/pm.property/new')
    expect(routing.buildCatalogCompactPageUrl('pm.property')).toBe('/catalogs/pm.property?panel=new')
    expect(routing.buildCatalogCompactPageUrl('pm.property', 'cat/1')).toBe('/catalogs/pm.property?panel=edit&id=cat%2F1')
    expect(routing.buildDocumentFullPageUrl('pm.invoice', 'doc/1')).toBe('/documents/pm.invoice/doc%2F1')
    expect(routing.buildDocumentFullPageUrl('pm.invoice')).toBe('/documents/pm.invoice/new')
    expect(routing.buildDocumentCompactPageUrl('pm.invoice')).toBe('/documents/pm.invoice?panel=new')
    expect(routing.buildDocumentCompactPageUrl('pm.invoice', 'doc/1')).toBe('/documents/pm.invoice?panel=edit&id=doc%2F1')
    expect(routing.buildDocumentEffectsPageUrl('pm.invoice', 'doc/1')).toBe('/documents/pm.invoice/doc%2F1/effects')
    expect(routing.buildDocumentFlowPageUrl('pm.invoice', 'doc/1')).toBe('/documents/pm.invoice/doc%2F1/flow')
    expect(routing.buildDocumentPrintPageUrl('pm.invoice', 'doc/1', { autoPrint: true })).toBe(
      '/documents/pm.invoice/doc%2F1/print?autoprint=1',
    )
    expect(routing.buildDocumentPrintPageUrl('pm.invoice', 'doc/1')).toBe(
      '/documents/pm.invoice/doc%2F1/print',
    )
  })

  it('returns configured routing/profile helpers and merges audit/effects/print overrides', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/editor/config')

    const sanitizeModelForEditing = vi.fn()
    const syncComputedDisplay = vi.fn()
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

  it('uses every configured routing override and safe empty behavior defaults', async () => {
    vi.resetModules()
    const config = await import('../../../../src/ngb/editor/config')
    const names = [
      'buildCatalogListUrl',
      'buildCatalogFullPageUrl',
      'buildCatalogCompactPageUrl',
      'buildDocumentFullPageUrl',
      'buildDocumentCompactPageUrl',
      'buildDocumentEffectsPageUrl',
      'buildDocumentFlowPageUrl',
      'buildDocumentPrintPageUrl',
    ] as const
    const routing = Object.fromEntries(names.map((name) => [name, vi.fn(() => `/${name}`)]))
    config.configureNgbEditor({
      routing,
      loadDocumentById: vi.fn(),
      loadDocumentEffects: vi.fn(),
      loadDocumentGraph: vi.fn(),
      loadEntityAuditLog: vi.fn(),
    } as never)

    const resolved = config.resolveNgbEditorRouting()
    expect(resolved.buildCatalogListUrl('catalog')).toBe('/buildCatalogListUrl')
    expect(resolved.buildCatalogFullPageUrl('catalog', 'id')).toBe('/buildCatalogFullPageUrl')
    expect(resolved.buildCatalogCompactPageUrl('catalog', 'id')).toBe('/buildCatalogCompactPageUrl')
    expect(resolved.buildDocumentFullPageUrl('document', 'id')).toBe('/buildDocumentFullPageUrl')
    expect(resolved.buildDocumentCompactPageUrl('document', 'id')).toBe('/buildDocumentCompactPageUrl')
    expect(resolved.buildDocumentEffectsPageUrl('document', 'id')).toBe('/buildDocumentEffectsPageUrl')
    expect(resolved.buildDocumentFlowPageUrl('document', 'id')).toBe('/buildDocumentFlowPageUrl')
    expect(resolved.buildDocumentPrintPageUrl('document', 'id')).toBe('/buildDocumentPrintPageUrl')

    const context = { kind: 'document', typeCode: 'pm.invoice', mode: 'page', status: 1 }
    const model = { memo: 'unchanged' }
    expect(config.resolveNgbEditorEntityProfile(context as never)).toEqual({})
    expect(() => config.sanitizeNgbEditorModelForEditing(context as never, model)).not.toThrow()
    expect(() => config.syncNgbEditorComputedDisplay(context as never, model)).not.toThrow()
    expect(config.resolveNgbEditorAuditBehavior()).toEqual({
      hiddenFieldNames: [
        'created_at_utc',
        'updated_at_utc',
        'deleted_at_utc',
        'marked_for_deletion_at_utc',
      ],
      explicitFieldLabels: {},
      actionTitles: {},
    })
    expect(config.resolveNgbEditorEffectsBehavior()).toEqual({})
    expect(config.resolveNgbEditorPrintBehavior()).toEqual({})
  })

  it('keeps server action target navigation independent from editor configuration', async () => {
    vi.resetModules()
    const navigation = await import('../../../../src/ngb/navigation/config')
    const context = { resourceCode: 'pm.invoice', entityId: 'doc/1' }

    navigation.configureNgbNavigation({
      resolveTarget: vi.fn((target) => target.code === 'custom' ? '/custom' : null),
    })

    expect(navigation.resolveNgbNavigationTarget(
      { code: 'custom', parameters: {} },
      context,
    )).toBe('/custom')
    expect(navigation.resolveNgbNavigationTarget(
      { code: 'unknown', parameters: { path: '/explicit/path' } },
      context,
    )).toBe('/explicit/path')
    expect(navigation.resolveNgbNavigationTarget(
      {
        code: 'document.editor',
        parameters: { documentType: 'crm.lead', documentId: 'lead/1' },
      },
      context,
    )).toBe('/documents/crm.lead/lead%2F1')
    expect(navigation.resolveNgbNavigationTarget(
      { code: 'document.effects', parameters: {} },
      context,
    )).toBe('/documents/pm.invoice/doc%2F1/effects')
    expect(navigation.resolveNgbNavigationTarget(
      { code: 'document.flow', parameters: { documentId: 'flow/1' } },
      context,
    )).toBe('/documents/pm.invoice/flow%2F1/flow')
    expect(navigation.resolveNgbNavigationTarget(
      { code: 'document.print', parameters: { documentType: '   ', documentId: 'print/1' } },
      context,
    )).toBe('/documents/pm.invoice/print%2F1/print')
    expect(navigation.resolveNgbNavigationTarget(
      { code: 'unknown', parameters: { path: 'relative' } },
      context,
    )).toBeNull()
    expect(navigation.resolveNgbNavigationTarget(
      { code: 'unknown', parameters: undefined as never },
      context,
    )).toBeNull()
    expect(navigation.resolveNgbNavigationTarget(
      { code: 'document.editor', parameters: undefined as never },
    )).toBeNull()

    navigation.configureNgbNavigation()
  })
})
