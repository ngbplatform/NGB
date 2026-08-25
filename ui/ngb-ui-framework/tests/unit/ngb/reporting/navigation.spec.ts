import { describe, expect, it } from 'vitest'

import { createComposerDraft } from '../../../../src/ngb/reporting/composer'
import {
  appendSourceTrail,
  buildBackToSourceUrl,
  buildCurrentReportContext,
  buildReportPageUrl,
  decodeReportDrilldownTarget,
  decodeReportRouteContextParam,
  decodeReportSourceTrailParam,
  encodeReportRouteContextParam,
  encodeReportSourceTrailParam,
  type ReportRouteContext,
} from '../../../../src/ngb/reporting/navigation'
import { decodeBackTarget } from '../../../../src/ngb/router/backNavigation'
import { createReportDefinition } from './fixtures'

function createRouteContext(reportCode: string): ReportRouteContext {
  return {
    reportCode,
    reportName: `${reportCode} title`,
    request: {
      parameters: {
        as_of_utc: '2026-04-08',
      },
      filters: {
        status: {
          value: 'open',
          includeDescendants: false,
        },
      },
      layout: null,
      variantCode: null,
      offset: 0,
      limit: 500,
      cursor: null,
    },
  }
}

function encodeBase64UrlJson(value: unknown): string {
  return Buffer.from(JSON.stringify(value), 'utf8')
    .toString('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '')
}

describe('reporting navigation helpers', () => {
  it('builds report urls that round-trip route context, source trail, and back target', () => {
    const definition = createReportDefinition()
    const draft = createComposerDraft(definition)
    draft.parameters.custom = 'tuned'
    draft.filters.property_id.items = [{ id: 'property-1', label: 'Riverfront Tower' }]

    const context = buildCurrentReportContext(definition, draft)
    const sourceTrail = { items: [createRouteContext('pm.parent_report')] }

    const url = buildReportPageUrl(definition.reportCode, {
      context,
      sourceTrail,
      backTarget: '/home?from=dashboard',
    })

    const parsed = new URL(url, 'https://ngb.test')
    expect(parsed.pathname).toBe(`/reports/${encodeURIComponent(definition.reportCode)}`)
    expect(decodeBackTarget(parsed.searchParams.get('back'))).toBe('/home?from=dashboard')
    expect(decodeReportRouteContextParam(parsed.searchParams.get('ctx'))).toEqual({
      ...context,
      request: {
        ...context.request,
        variantCode: null,
        cursor: null,
      },
    })
    expect(decodeReportSourceTrailParam(parsed.searchParams.get('src'))).toEqual(sourceTrail)
  })

  it('builds a back-to-source url from the latest trail item and keeps the earlier trail', () => {
    const first = createRouteContext('pm.first_report')
    const second = createRouteContext('pm.second_report')

    const backUrl = buildBackToSourceUrl({ items: [first, second] }, '/reports/landing')
    expect(backUrl).not.toBeNull()

    const parsed = new URL(backUrl ?? '', 'https://ngb.test')
    expect(parsed.pathname).toBe('/reports/pm.second_report')
    expect(decodeBackTarget(parsed.searchParams.get('back'))).toBe('/reports/landing')
    expect(decodeReportRouteContextParam(parsed.searchParams.get('ctx'))).toEqual(second)
    expect(decodeReportSourceTrailParam(parsed.searchParams.get('src'))).toEqual({ items: [first] })
  })

  it('appends source trail items and decodes report drilldown targets defensively', () => {
    const current = createRouteContext('pm.current_report')
    expect(appendSourceTrail(null, current)).toEqual({ items: [current] })

    const token = `report:${encodeBase64UrlJson({
      reportCode: 'pm.drilldown_report',
      parameters: {
        as_of_utc: '2026-04-08',
        blank: '   ',
      },
      filters: {
        property_id: {
          value: ['property-1', 'property-2'],
          includeDescendants: true,
        },
      },
    })}`

    expect(decodeReportDrilldownTarget(token)).toEqual({
      reportCode: 'pm.drilldown_report',
      request: {
        layout: null,
        parameters: {
          as_of_utc: '2026-04-08',
          blank: '   ',
        },
        filters: {
          property_id: {
            value: ['property-1', 'property-2'],
            includeDescendants: true,
          },
        },
        offset: 0,
        limit: 500,
        cursor: null,
      },
    })

    expect(decodeReportDrilldownTarget('document:abc')).toBeNull()
    expect(decodeReportRouteContextParam('%%%')).toBeNull()
    expect(decodeReportSourceTrailParam('%%%')).toBeNull()
  })

  it('normalizes optional route request fields and removes malformed nested values', () => {
    const token = encodeBase64UrlJson({
      reportCode: '  pm.boundary  ',
      reportName: '  Boundary report  ',
      request: {
        parameters: { keep: ' value ', blank: ' ', empty: null },
        filters: {
          keep: { value: 'open' },
          descendants: { value: ['one'], includeDescendants: true },
          missingValue: { includeDescendants: true },
          invalid: null,
        },
        layout: { columns: ['number'] },
        variantCode: '  compact  ',
        offset: 25,
        limit: 100,
        cursor: '  next-page  ',
      },
    })

    expect(decodeReportRouteContextParam([token])).toEqual({
      reportCode: 'pm.boundary',
      reportName: 'Boundary report',
      request: {
        parameters: { keep: 'value' },
        filters: {
          keep: { value: 'open', includeDescendants: false },
          descendants: { value: ['one'], includeDescendants: true },
        },
        layout: { columns: ['number'] },
        variantCode: 'compact',
        offset: 25,
        limit: 100,
        cursor: 'next-page',
      },
    })

    const fallbackToken = encodeBase64UrlJson({
      reportCode: 'pm.fallback',
      reportName: ' ',
      request: {
        parameters: [],
        filters: 'invalid',
        layout: null,
        variantCode: ' ',
        offset: 'invalid',
        limit: null,
        cursor: ' ',
      },
    })
    expect(decodeReportRouteContextParam(fallbackToken)).toMatchObject({
      reportName: null,
      request: {
        parameters: null,
        filters: null,
        layout: null,
        variantCode: null,
        offset: 0,
        limit: 500,
        cursor: null,
      },
    })
  })

  it('rejects empty, malformed, and structurally invalid route and trail payloads', () => {
    expect(decodeReportRouteContextParam(null)).toBeNull()
    expect(decodeReportRouteContextParam([])).toBeNull()
    expect(decodeReportRouteContextParam(encodeBase64UrlJson(42))).toBeNull()
    expect(decodeReportRouteContextParam(encodeBase64UrlJson({ request: {} }))).toBeNull()
    expect(decodeReportRouteContextParam(encodeBase64UrlJson({ reportCode: ' ', request: {} }))).toBeNull()
    expect(decodeReportRouteContextParam(encodeBase64UrlJson({ reportCode: 'pm.invalid', request: null }))).toBeNull()

    expect(encodeReportRouteContextParam(null)).toBeNull()
    expect(encodeReportSourceTrailParam(null)).toBeNull()
    expect(encodeReportSourceTrailParam({ items: [] })).toBeNull()
    expect(decodeReportSourceTrailParam(encodeBase64UrlJson({ items: 'invalid' }))).toBeNull()
    expect(decodeReportSourceTrailParam(encodeBase64UrlJson({ items: [] }))).toBeNull()
    expect(decodeReportSourceTrailParam(encodeBase64UrlJson({
      items: [null, createRouteContext('pm.valid')],
    }))).toEqual({ items: [createRouteContext('pm.valid')] })
  })

  it('handles minimal and invalid drilldown tokens without leaking malformed context', () => {
    expect(decodeReportDrilldownTarget(null)).toBeNull()
    expect(decodeReportDrilldownTarget('report:%%%')).toBeNull()
    expect(decodeReportDrilldownTarget(`report:${encodeBase64UrlJson({})}`)).toBeNull()
    expect(decodeReportDrilldownTarget(`report:${encodeBase64UrlJson({ reportCode: 42 })}`)).toBeNull()
    expect(decodeReportDrilldownTarget(`report:${encodeBase64UrlJson({ reportCode: ' ' })}`)).toBeNull()
    expect(decodeReportDrilldownTarget(`report:${encodeBase64UrlJson({
      reportCode: ' pm.minimal ',
      parameters: {},
      filters: {},
    })}`)).toEqual({
      reportCode: 'pm.minimal',
      request: {
        parameters: null,
        filters: null,
        layout: null,
        offset: 0,
        limit: 500,
        cursor: null,
      },
    })
  })

  it('handles empty navigation history and builds plain urls without optional state', () => {
    const current = createRouteContext('pm.only')

    expect(buildReportPageUrl('pm report')).toBe('/reports/pm%20report')
    expect(appendSourceTrail(null, null)).toBeNull()
    expect(appendSourceTrail({ items: [current] }, null)).toEqual({ items: [current] })
    expect(buildBackToSourceUrl(null)).toBeNull()
    expect(buildBackToSourceUrl({ items: [] })).toBeNull()

    const url = buildBackToSourceUrl({ items: [current] })
    expect(url).not.toBeNull()
    const parsed = new URL(url!, 'https://ngb.test')
    expect(parsed.pathname).toBe('/reports/pm.only')
    expect(parsed.searchParams.has('src')).toBe(false)
    expect(parsed.searchParams.has('back')).toBe(false)
  })
})
