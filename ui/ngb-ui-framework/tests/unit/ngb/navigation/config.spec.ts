import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  configureNgbNavigation,
  NgbSourceKinds,
  resolveNgbNavigationTarget,
  resolveNgbPageContext,
  type NgbNavigationLocation,
  type NgbSourceIdentity,
} from '../../../../src/navigation'
import { configureNgbEditor, type EditorRoutingConfig } from '../../../../src/ngb/editor/config'
import { buildCatalogFullPageUrl } from '../../../../src/ngb/editor/catalogNavigation'
import { assistantSourcePath } from '../../../../src/ngb/ai-assistant/navigation'
import { encodeReportRouteContextParam } from '../../../../src/ngb/reporting/navigation'

const id = '11111111-1111-4111-8111-111111111111'

function location(path: string, query: NgbNavigationLocation['query'] = {}): NgbNavigationLocation {
  return { path, fullPath: path, name: undefined, params: {}, query, meta: {} }
}

function configureEditor(routing: EditorRoutingConfig = {}): void {
  configureNgbEditor({
    routing,
    loadDocumentById: vi.fn(),
    loadDocumentEffects: vi.fn(),
    loadDocumentGraph: vi.fn(),
    loadEntityAuditLog: vi.fn(),
    documentActions: { loadEditorState: vi.fn(), execute: vi.fn() },
  })
}

beforeEach(() => {
  configureNgbNavigation()
  configureEditor()
})

afterEach(() => {
  configureNgbNavigation()
  configureEditor()
})

describe('semantic page context', () => {
  it.each([
    ['/catalogs/example.customer', { kind: 'catalog', typeCode: 'example.customer' }],
    [`/catalogs/example.customer/${id}`, { kind: 'catalog', typeCode: 'example.customer', id }],
    [`/documents/example.invoice/${id}/`, { kind: 'document', typeCode: 'example.invoice', id }],
    [`/documents/example.invoice/${id}/flow`, { kind: 'document', typeCode: 'example.invoice', id }],
    [`/documents/example.invoice/${id}/effects`, { kind: 'document', typeCode: 'example.invoice', id }],
    ['/reports/example.summary', { kind: 'report', typeCode: 'example.summary' }],
    ['/accounting/general-journal-entries', { kind: 'generalJournalEntry', typeCode: 'general_journal_entry' }],
    [`/accounting/general-journal-entries/${id}`, { kind: 'generalJournalEntry', typeCode: 'general_journal_entry', id }],
  ])('supports the standard route %s', (path, expected) => {
    expect(resolveNgbPageContext(location(path as string))).toEqual(expected)
  })

  it.each(['catalogs', 'documents'])('includes the record selected in a compact %s editor', section => {
    expect(resolveNgbPageContext(location(`/${section}/example.record`, { panel: 'edit', id })))
      .toEqual({ kind: section === 'catalogs' ? 'catalog' : 'document', typeCode: 'example.record', id })
  })

  it.each([
    location('/home'),
    location('/catalogs/%2Fetc'),
    location('/catalogs/%ZZ'),
    location('/catalogs/example.customer/new'),
    location('/catalogs/example.customer/00000000-0000-0000-0000-000000000000'),
    location('/accounting/general-journal-entries/new'),
    location(`/reports/example.summary/${id}`),
    location('/catalogs/example.customer', { panel: 'new', id }),
    location('/catalogs/example.customer', { panel: 'edit', id: 'not-a-record-id' }),
    location('/catalogs/example.customer', { panel: 'edit' }),
  ])('does not invent a context for invalid or unsaved records: %s', route => {
    expect(resolveNgbPageContext(route)).toBeNull()
  })

  it('uses host semantics on arbitrary routes and copies only source identifiers', () => {
    const route = { ...location('/customers/selected'), name: 'Customer', params: { customerId: id } }
    const resolvePageContext = vi.fn((current: NgbNavigationLocation) => ({
      kind: NgbSourceKinds.Catalog,
      typeCode: 'example.customer',
      id: String(current.params.customerId),
      loadedRows: ['private UI data'],
    }))
    configureNgbNavigation({ resolvePageContext })

    expect(resolveNgbPageContext(route)).toEqual({ kind: 'catalog', typeCode: 'example.customer', id })
    expect(resolvePageContext).toHaveBeenCalledWith(route)
  })

  it('distinguishes host opt-out from fallback to standard routes', () => {
    const route = location('/reports/example.summary')
    configureNgbNavigation({ resolvePageContext: () => null })
    expect(resolveNgbPageContext(route)).toBeNull()

    configureNgbNavigation({ resolvePageContext: () => undefined })
    expect(resolveNgbPageContext(route)).toEqual({ kind: 'report', typeCode: 'example.summary' })
  })

  it.each([
    { kind: 'register', typeCode: 'example.customer', id },
    { kind: 'catalog', typeCode: '//invalid', id },
    { kind: 'catalog', typeCode: 'example.customer', id: 'invalid' },
  ])('rejects invalid host context without falling back to a different scope', context => {
    configureNgbNavigation({ resolvePageContext: () => context as NgbSourceIdentity })

    expect(resolveNgbPageContext(location('/reports/example.summary'))).toBeNull()
  })
})

describe('shared record routing', () => {
  it('copies only selected report criteria and rejects criteria for a different report', () => {
    const request = { parameters: { as_of_utc: '2026-10-17' }, filters: { building_id: { value: id } }, limit: 500 }
    const ctx = encodeReportRouteContextParam({ reportCode: 'example.summary', request })!
    expect(resolveNgbPageContext(location('/reports/example.summary', { ctx }))).toEqual({
      kind: 'report', typeCode: 'example.summary', parameters: request.parameters,
      filters: { building_id: { value: id, includeDescendants: false } },
    })
    expect(resolveNgbPageContext(location('/reports/example.other', { ctx })))
      .toEqual({ kind: 'report', typeCode: 'example.other' })
  })

  it('uses the same configured catalog route for the editor and Assistant', () => {
    const path = `/records/example.customer/${id}`
    configureEditor({ buildCatalogFullPageUrl: (typeCode, recordId) => `/records/${typeCode}/${recordId}` })

    expect(buildCatalogFullPageUrl('example.customer', id)).toBe(path)
    expect(assistantSourcePath({ kind: 'catalog', typeCode: 'example.customer', id, title: 'Customer' })).toBe(path)
  })

  it('delegates document targets to their existing builders and preserves target parameters', () => {
    configureEditor({
      buildDocumentFullPageUrl: (typeCode, recordId) => `/records/${typeCode}/${recordId}`,
      buildDocumentEffectsPageUrl: (typeCode, recordId) => `/effects/${typeCode}/${recordId}`,
      buildDocumentFlowPageUrl: (typeCode, recordId) => `/flow/${typeCode}/${recordId}`,
      buildDocumentPrintPageUrl: (typeCode, recordId) => `/print/${typeCode}/${recordId}`,
    })
    const context = { resourceCode: 'ignored', entityId: 'ignored' }
    const parameters = { documentType: 'example.invoice', documentId: id }

    for (const [target, prefix] of [['editor', 'records'], ['effects', 'effects'], ['flow', 'flow'], ['print', 'print']]) {
      expect(resolveNgbNavigationTarget({ code: `document.${target}`, parameters }, context))
        .toBe(`/${prefix}/example.invoice/${id}`)
    }
    expect(assistantSourcePath({ kind: 'document', typeCode: 'example.invoice', id, title: 'Invoice' }))
      .toBe(`/records/example.invoice/${id}`)
  })

  it('keeps the host target override ahead of configured record builders', () => {
    configureEditor({ buildCatalogFullPageUrl: () => '/editor-route' })
    configureNgbNavigation({ resolveTarget: () => '/host-route' })

    expect(assistantSourcePath({ kind: 'catalog', typeCode: 'example.customer', id, title: 'Customer' }))
      .toBe('/host-route')
  })
})
