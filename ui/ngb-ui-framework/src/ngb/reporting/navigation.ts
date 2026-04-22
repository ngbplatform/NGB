import { withBackTarget } from '../router/backNavigation'
import { buildExecutionRequest } from './composer'
import type { ReportComposerDraft, ReportDefinitionDto, ReportExecutionRequestDto, ReportFilterValueDto } from './types'

export type ReportRouteContext = {
  reportCode: string
  reportName?: string | null
  request: ReportExecutionRequestDto
}

export type ReportSourceTrail = {
  items: ReportRouteContext[]
}

type ReportTokenPayload = {
  reportCode: string
  parameters?: Record<string, string> | null
  filters?: Record<string, ReportFilterValueDto> | null
}

function normalizeQueryValue(value: unknown): string | null {
  if (Array.isArray(value)) return normalizeQueryValue(value[0] ?? null)
  const raw = String(value ?? '').trim()
  return raw.length > 0 ? raw : null
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value)
}

function encodeBase64UrlJson(value: unknown): string {
  const bytes = new TextEncoder().encode(JSON.stringify(value))
  let binary = ''
  bytes.forEach((byte) => {
    binary += String.fromCharCode(byte)
  })
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function decodeBase64UrlJson<T>(value: unknown): T | null {
  const raw = normalizeQueryValue(value)
  if (!raw) return null

  try {
    const padded = raw.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((raw.length + 3) % 4)
    const binary = atob(padded)
    const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0))
    return JSON.parse(new TextDecoder().decode(bytes)) as T
  } catch {
    return null
  }
}

function normalizeFilterValue(value: unknown): ReportFilterValueDto | null {
  if (!isPlainObject(value) || !('value' in value)) return null
  return {
    value: value.value,
    includeDescendants: !!value.includeDescendants,
  }
}

function normalizeExecutionRequest(value: unknown): ReportExecutionRequestDto | null {
  if (!isPlainObject(value)) return null

  const parameters = isPlainObject(value.parameters)
    ? Object.fromEntries(Object.entries(value.parameters).map(([key, entry]) => [key, String(entry ?? '').trim()]).filter(([, entry]) => entry.length > 0))
    : null

  const filters = isPlainObject(value.filters)
    ? Object.fromEntries(Object.entries(value.filters).map(([key, entry]) => [key, normalizeFilterValue(entry)]).filter((entry): entry is [string, ReportFilterValueDto] => !!entry[1]))
    : null

  const layout = isPlainObject(value.layout) ? (value.layout as ReportExecutionRequestDto['layout']) : null
  const variantCode = typeof value.variantCode === 'string' && value.variantCode.trim().length > 0 ? value.variantCode.trim() : null
  const offset = typeof value.offset === 'number' && Number.isFinite(value.offset) ? value.offset : 0
  const limit = typeof value.limit === 'number' && Number.isFinite(value.limit) ? value.limit : 500
  const cursor = typeof value.cursor === 'string' && value.cursor.trim().length > 0 ? value.cursor.trim() : null

  return {
    layout,
    parameters: parameters && Object.keys(parameters).length > 0 ? parameters : null,
    filters: filters && Object.keys(filters).length > 0 ? filters : null,
    variantCode,
    offset,
    limit,
    cursor,
  }
}

function normalizeRouteContext(value: unknown): ReportRouteContext | null {
  if (!isPlainObject(value)) return null
  const reportCode = String(value.reportCode ?? '').trim()
  if (!reportCode) return null
  const request = normalizeExecutionRequest(value.request)
  if (!request) return null
  const reportName = typeof value.reportName === 'string' && value.reportName.trim().length > 0 ? value.reportName.trim() : null
  return { reportCode, reportName, request }
}

export function encodeReportRouteContextParam(value: ReportRouteContext | null | undefined): string | null {
  return value ? encodeBase64UrlJson(value) : null
}

export function encodeReportSourceTrailParam(value: ReportSourceTrail | null | undefined): string | null {
  return value && value.items.length > 0 ? encodeBase64UrlJson(value) : null
}

export function buildCurrentReportContext(definition: ReportDefinitionDto, draft: ReportComposerDraft): ReportRouteContext {
  return {
    reportCode: definition.reportCode,
    reportName: definition.name,
    request: buildExecutionRequest(definition, draft),
  }
}

export function buildReportPageUrl(
  reportCode: string,
  options?: {
    context?: ReportRouteContext | null
    sourceTrail?: ReportSourceTrail | null
    backTarget?: string | null
  },
): string {
  const path = `/reports/${encodeURIComponent(reportCode)}`
  const query = new URLSearchParams()
  const contextParam = encodeReportRouteContextParam(options?.context)
  const sourceTrailParam = encodeReportSourceTrailParam(options?.sourceTrail)
  if (contextParam) query.set('ctx', contextParam)
  if (sourceTrailParam) query.set('src', sourceTrailParam)
  const suffix = query.toString()
  const url = suffix ? `${path}?${suffix}` : path
  return withBackTarget(url, options?.backTarget ?? null)
}

export function decodeReportRouteContextParam(value: unknown): ReportRouteContext | null {
  return normalizeRouteContext(decodeBase64UrlJson(value))
}

export function decodeReportSourceTrailParam(value: unknown): ReportSourceTrail | null {
  const decoded = decodeBase64UrlJson<{ items?: unknown }>(value)
  if (!decoded || !Array.isArray(decoded.items)) return null
  const items = decoded.items.map(normalizeRouteContext).filter((entry): entry is ReportRouteContext => !!entry)
  return items.length > 0 ? { items } : null
}

export function decodeReportDrilldownTarget(token: string | null | undefined): ReportRouteContext | null {
  const raw = String(token ?? '').trim()
  if (!raw.startsWith('report:')) return null
  const payload = decodeBase64UrlJson<ReportTokenPayload>(raw.slice('report:'.length))
  if (!payload || typeof payload.reportCode !== 'string' || payload.reportCode.trim().length === 0) return null

  return {
    reportCode: payload.reportCode.trim(),
    request: {
      parameters: payload.parameters && Object.keys(payload.parameters).length > 0 ? payload.parameters : null,
      filters: payload.filters && Object.keys(payload.filters).length > 0 ? payload.filters : null,
      layout: null,
      offset: 0,
      limit: 500,
      cursor: null,
    },
  }
}

export function appendSourceTrail(sourceTrail: ReportSourceTrail | null | undefined, currentContext: ReportRouteContext | null | undefined): ReportSourceTrail | null {
  const items = [...(sourceTrail?.items ?? [])]
  if (currentContext) items.push(currentContext)
  return items.length > 0 ? { items } : null
}

export function buildBackToSourceUrl(sourceTrail: ReportSourceTrail | null | undefined, backTarget?: string | null): string | null {
  const items = sourceTrail?.items ?? []
  if (items.length === 0) return null
  const target = items[items.length - 1]!
  const priorItems = items.slice(0, -1)
  return buildReportPageUrl(target.reportCode, {
    context: target,
    sourceTrail: priorItems.length > 0 ? { items: priorItems } : null,
    backTarget: backTarget ?? null,
  })
}
