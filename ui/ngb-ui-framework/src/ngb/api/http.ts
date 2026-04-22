import { forceRefreshAccessToken, getAccessToken } from '../auth/keycloak'
import { readAppEnv } from '../env/runtimeConfig'
import type { JsonValue, QueryParams } from './types'

export type ApiProblemDetails = {
  title?: string
  detail?: string
  status?: number
  type?: string
  instance?: string
  traceId?: string
  [key: string]: JsonValue | undefined
}

export type ApiValidationErrors = Record<string, string[]>

export type ApiValidationIssue = {
  path: string
  message: string
  scope: string
  code?: string | null
}

export type ApiErrorEnvelope = {
  code?: string | null
  kind?: string | null
  context?: Record<string, unknown> | null
  errors?: ApiValidationErrors | null
  issues?: ApiValidationIssue[] | null
}

export type HttpRequestOptions = {
  signal?: AbortSignal
  retryOnUnauthorized?: boolean
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value)
}

function normalizeValidationErrors(value: unknown): ApiValidationErrors | null {
  if (!isRecord(value)) return null

  const result: ApiValidationErrors = {}
  for (const [key, raw] of Object.entries(value)) {
    const messages = Array.isArray(raw)
      ? raw.map((entry) => (entry == null ? '' : String(entry).trim())).filter((entry) => entry.length > 0)
      : raw == null
        ? []
        : [String(raw).trim()].filter((entry) => entry.length > 0)

    if (messages.length > 0) result[key] = messages
  }

  return Object.keys(result).length > 0 ? result : null
}

function normalizeValidationPath(raw: unknown): string {
  let path = typeof raw === 'string' ? raw.trim() : ''
  if (!path) return '_form'
  if (path === '_form') return path

  if (path.startsWith('$')) path = path.slice(1)
  if (path.startsWith('.')) path = path.slice(1)

  const stripPrefix = (value: string, prefix: string) =>
    value.toLowerCase().startsWith(prefix.toLowerCase()) ? value.slice(prefix.length) : value

  path = stripPrefix(path, 'request.')
  path = stripPrefix(path, 'payload.')
  path = stripPrefix(path, 'fields.')

  path = path.replace(/^([^\.]+)\.rows\[(\d+)\]\.(.+)$/i, '$1[$2].$3')
  path = path.replace(/^([^\.]+)\.rows\[(\d+)\]$/i, '$1[$2]')
  path = stripPrefix(path, 'parts.')
  path = path.replace(/\.rows\[(\d+)\]/gi, '[$1]')
  path = path.replace(/\.rows\[\]/gi, '[]')
  path = path.replace(/^\.+|\.+$/g, '')

  if (!path || path.toLowerCase() === 'payload') return '_form'
  return path
}

function inferIssueScope(path: string): string {
  if (!path || path === '_form') return 'form'
  if (/\[(?:\d+)?\]$/.test(path)) return /\[\]$/.test(path) ? 'collection' : 'row'
  return 'field'
}

function normalizeValidationIssue(value: unknown): ApiValidationIssue | null {
  if (!isRecord(value)) return null

  const rawMessage = typeof value.message === 'string'
    ? value.message
    : typeof value.Message === 'string'
      ? value.Message
      : ''
  const message = rawMessage.trim()
  if (!message) return null

  const path = normalizeValidationPath(
    typeof value.path === 'string'
      ? value.path
      : typeof value.Path === 'string'
        ? value.Path
        : ''
  )

  const rawScope = typeof value.scope === 'string'
    ? value.scope.trim()
    : typeof value.Scope === 'string'
      ? value.Scope.trim()
      : ''

  const code = typeof value.code === 'string'
    ? value.code
    : typeof value.Code === 'string'
      ? value.Code
      : null

  return {
    path,
    message,
    scope: rawScope || inferIssueScope(path),
    code,
  }
}

function normalizeValidationIssues(value: unknown): ApiValidationIssue[] | null {
  if (!Array.isArray(value)) return null
  const issues = value
    .map((entry) => normalizeValidationIssue(entry))
    .filter((entry): entry is ApiValidationIssue => !!entry)

  return issues.length > 0 ? issues : null
}

function validationIssuesFromErrors(errors: ApiValidationErrors | null | undefined): ApiValidationIssue[] | null {
  if (!errors) return null

  const issues: ApiValidationIssue[] = []
  for (const [rawPath, messages] of Object.entries(errors)) {
    const path = normalizeValidationPath(rawPath)
    const scope = inferIssueScope(path)
    for (const message of messages ?? []) {
      const text = String(message ?? '').trim()
      if (!text) continue
      issues.push({ path, message: text, scope, code: null })
    }
  }

  return issues.length > 0 ? issues : null
}

function extractErrorEnvelope(body: unknown): ApiErrorEnvelope | null {
  if (!isRecord(body)) return null

  const nested = isRecord(body.error) ? body.error : null
  const flatErrors = normalizeValidationErrors(body.errors)
  const nestedErrors = normalizeValidationErrors(nested?.errors ?? nested?.Errors)
  const flatIssues = normalizeValidationIssues(body.issues)
  const nestedIssues = normalizeValidationIssues(nested?.issues ?? nested?.Issues)
  const context = isRecord(body.context)
    ? body.context
    : isRecord(nested?.context)
      ? nested?.context
      : isRecord(nested?.Context)
        ? nested?.Context
        : null

  const code = typeof body.errorCode === 'string'
    ? body.errorCode
    : typeof nested?.code === 'string'
      ? nested.code
      : typeof nested?.Code === 'string'
        ? nested.Code
        : null

  const kind = typeof body.kind === 'string'
    ? body.kind
    : typeof nested?.kind === 'string'
      ? nested.kind
      : typeof nested?.Kind === 'string'
        ? nested.Kind
        : null

  const errors = nestedErrors ?? flatErrors

  return {
    code,
    kind,
    context,
    errors,
    issues: nestedIssues ?? flatIssues ?? validationIssuesFromErrors(errors),
  }
}

function isGenericProblemDetail(detail: string): boolean {
  const normalized = detail.trim().toLowerCase()
  return normalized === 'one or more validation errors has occurred.'
}

function firstValidationMessage(errors: ApiValidationErrors | null | undefined): string | null {
  if (!errors) return null
  for (const messages of Object.values(errors)) {
    const first = messages.find((entry) => entry.trim().length > 0)
    if (first) return first
  }
  return null
}

function firstValidationIssueMessage(issues: ApiValidationIssue[] | null | undefined): string | null {
  if (!issues) return null
  const first = issues.find((issue) => issue.message.trim().length > 0)
  return first?.message?.trim() || null
}

export class ApiError extends Error {
  readonly status: number
  readonly url: string
  readonly body?: unknown
  readonly problem?: ApiProblemDetails | null
  readonly errorCode?: string | null
  readonly kind?: string | null
  readonly context?: Record<string, unknown> | null
  readonly errors?: ApiValidationErrors | null
  readonly issues?: ApiValidationIssue[] | null

  constructor(args: { message: string; status: number; url: string; body?: unknown }) {
    super(args.message)
    this.name = 'ApiError'
    this.status = args.status
    this.url = args.url
    this.body = args.body

    const envelope = extractErrorEnvelope(args.body)
    this.problem = isRecord(args.body) ? (args.body as ApiProblemDetails) : null
    this.errorCode = envelope?.code ?? null
    this.kind = envelope?.kind ?? null
    this.context = envelope?.context ?? null
    this.errors = envelope?.errors ?? null
    this.issues = envelope?.issues ?? null
  }
}

function apiBaseUrl(): string {
  const base = readAppEnv('VITE_API_BASE_URL')
  return base.length > 0 ? base : window.location.origin
}

function isJsonContentType(contentType: string | null | undefined): boolean {
  const normalized = (contentType ?? '').toLowerCase()
  return normalized.includes('json')
}

function resolveUrl(url: string): string {
  if (/^https?:\/\//i.test(url)) return url
  const path = url.startsWith('/') ? url : `/${url}`
  return new URL(path, apiBaseUrl()).toString()
}

async function tryReadJson(response: Response): Promise<unknown | null> {
  if (!isJsonContentType(response.headers.get('content-type'))) return null
  try {
    return await response.json() as unknown
  } catch {
    return null
  }
}

async function tryReadText(response: Response): Promise<string | null> {
  try {
    return await response.text()
  } catch {
    return null
  }
}

function toApiErrorMessage(status: number, body: unknown): string {
  if (!body) return `HTTP ${status}`

  const envelope = extractErrorEnvelope(body)
  const detail = isRecord(body) && typeof body.detail === 'string' ? body.detail.trim() : ''
  const title = isRecord(body) && typeof body.title === 'string' ? body.title.trim() : ''
  const firstIssue = firstValidationIssueMessage(envelope?.issues)
  const firstError = firstValidationMessage(envelope?.errors)

  if (detail && !isGenericProblemDetail(detail)) return detail
  if (firstIssue) return firstIssue
  if (firstError) return firstError

  if (isRecord(body) && typeof body.message === 'string' && body.message.trim().length > 0) return body.message.trim()
  if (title) return title
  if (typeof envelope?.code === 'string' && envelope.code.length > 0) return `${envelope.code} (HTTP ${status})`

  return `HTTP ${status}`
}

function appendQuery(url: string, query: QueryParams | null | undefined): string {
  if (!query) return url
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    params.set(key, String(value))
  }
  const serialized = params.toString()
  if (!serialized) return url
  return url.includes('?') ? `${url}&${serialized}` : `${url}?${serialized}`
}

async function buildJsonHeaders(body: unknown, accept: string): Promise<Record<string, string>> {
  const headers: Record<string, string> = { Accept: accept }
  if (body != null) headers['Content-Type'] = 'application/json'

  const token = await getAccessToken()
  if (token) headers.Authorization = `Bearer ${token}`

  return headers
}

async function buildResponse(response: Response, resolvedUrl: string): Promise<never> {
  const json = await tryReadJson(response)
  const message = toApiErrorMessage(response.status, json)
  throw new ApiError({ message, status: response.status, url: resolvedUrl, body: json ?? undefined })
}

export async function httpRequest<T>(
  method: string,
  url: string,
  body?: unknown,
  options?: HttpRequestOptions,
): Promise<T> {
  const resolvedUrl = resolveUrl(url)
  const response = await fetch(resolvedUrl, {
    method,
    credentials: 'omit',
    headers: await buildJsonHeaders(body, 'application/json'),
    body: body != null ? JSON.stringify(body) : undefined,
    signal: options?.signal,
  })

  if (response.status === 401 && options?.retryOnUnauthorized !== false) {
    const refreshedToken = await forceRefreshAccessToken().catch(() => null)
    if (refreshedToken) {
      return await httpRequest<T>(method, url, body, { ...options, retryOnUnauthorized: false })
    }
  }

  if (!response.ok) await buildResponse(response, resolvedUrl)
  if (response.status === 204) return undefined as unknown as T

  const json = await tryReadJson(response)
  if (json == null) {
    const text = await tryReadText(response)
    throw new ApiError({
      message: `Expected JSON but got '${response.headers.get('content-type') ?? 'unknown'}' for ${resolvedUrl}`,
      status: response.status,
      url: resolvedUrl,
      body: text ?? undefined,
    })
  }

  return json as T
}

export async function httpGet<T>(
  url: string,
  query?: QueryParams | null,
  options?: HttpRequestOptions,
): Promise<T> {
  return httpRequest<T>('GET', appendQuery(url, query ?? undefined), undefined, options)
}

export async function httpPost<TResponse, TBody = unknown>(
  url: string,
  body?: TBody,
  options?: HttpRequestOptions,
): Promise<TResponse> {
  return httpRequest<TResponse>('POST', url, body, options)
}

export async function httpPut<TResponse, TBody = unknown>(
  url: string,
  body?: TBody,
  options?: HttpRequestOptions,
): Promise<TResponse> {
  return httpRequest<TResponse>('PUT', url, body, options)
}

export async function httpDelete<TResponse, TBody = unknown>(
  url: string,
  body?: TBody,
  options?: HttpRequestOptions,
): Promise<TResponse> {
  return httpRequest<TResponse>('DELETE', url, body, options)
}

export type HttpFileResponse = {
  blob: Blob
  fileName: string | null
  contentType: string | null
}

function parseFileNameFromContentDisposition(value: string | null): string | null {
  const raw = String(value ?? '').trim()
  if (!raw) return null

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(raw)
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1].trim())
    } catch {
      return utf8Match[1].trim()
    }
  }

  const basicMatch = /filename=\"?([^\";]+)\"?/i.exec(raw)
  return basicMatch?.[1]?.trim() || null
}

export async function httpPostFile<TBody = unknown>(url: string, body?: TBody): Promise<HttpFileResponse> {
  return await httpPostFileInternal(url, body, true)
}

async function httpPostFileInternal(url: string, body: unknown, retryOnUnauthorized: boolean): Promise<HttpFileResponse> {
  const resolvedUrl = resolveUrl(url)
  const response = await fetch(resolvedUrl, {
    method: 'POST',
    credentials: 'omit',
    headers: await buildJsonHeaders(body, '*/*'),
    body: body != null ? JSON.stringify(body) : undefined,
  })

  if (response.status === 401 && retryOnUnauthorized) {
    const refreshedToken = await forceRefreshAccessToken().catch(() => null)
    if (refreshedToken) return await httpPostFileInternal(url, body, false)
  }

  if (!response.ok) await buildResponse(response, resolvedUrl)

  return {
    blob: await response.blob(),
    fileName: parseFileNameFromContentDisposition(response.headers.get('content-disposition')),
    contentType: response.headers.get('content-type'),
  }
}
