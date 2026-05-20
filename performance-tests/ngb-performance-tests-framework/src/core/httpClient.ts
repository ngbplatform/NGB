import http, { expectedStatuses, type Params } from 'k6/http';

import { operationSucceeded } from './checks.ts';
import type { NgbPerfEnv } from './env.ts';
import { safeErrorSummary } from './errors.ts';
import { accountingEffectsDuration, documentFlowDuration, documentPostDuration, recordBusinessOperation, reportExecutionDuration } from './metrics.ts';
import { buildTags, mergeTags, type NgbRequestTags } from './requestTags.ts';

export interface AccessTokenProvider {
  getAccessToken(): string;
  invalidateAccessToken?(accessToken: string): void;
}

export interface NgbHttpClientOptions {
  readonly env: NgbPerfEnv;
  readonly tokenProvider: AccessTokenProvider;
  readonly defaultTags?: NgbRequestTags;
  readonly timeout?: string;
}

export interface NgbRequestOptions {
  readonly tags?: NgbRequestTags;
  readonly expectedStatuses?: readonly number[];
  readonly query?: Record<string, QueryValue>;
  readonly body?: unknown;
}

export type QueryValue = string | number | boolean | null | undefined;
export type NgbHttpResponse = ReturnType<typeof http.request>;

export class NgbHttpClient {
  private readonly env: NgbPerfEnv;
  private readonly tokenProvider: AccessTokenProvider;
  private readonly defaultTags: NgbRequestTags;
  private readonly timeout: string;

  constructor(options: NgbHttpClientOptions) {
    this.env = options.env;
    this.tokenProvider = options.tokenProvider;
    this.defaultTags = options.defaultTags ?? {};
    this.timeout = options.timeout ?? '60s';
  }

  get(path: string, options: NgbRequestOptions = {}): NgbHttpResponse {
    return this.request('GET', path, options);
  }

  post(path: string, body?: unknown, options: NgbRequestOptions = {}): NgbHttpResponse {
    return this.request('POST', path, { ...options, body });
  }

  put(path: string, body?: unknown, options: NgbRequestOptions = {}): NgbHttpResponse {
    return this.request('PUT', path, { ...options, body });
  }

  patch(path: string, body?: unknown, options: NgbRequestOptions = {}): NgbHttpResponse {
    return this.request('PATCH', path, { ...options, body });
  }

  delete(path: string, options: NgbRequestOptions = {}): NgbHttpResponse {
    return this.request('DELETE', path, options);
  }

  request(method: string, path: string, options: NgbRequestOptions = {}): NgbHttpResponse {
    const tags = buildTags(mergeTags(
      { vertical: this.env.vertical },
      this.defaultTags,
      options.tags,
    ));
    const url = this.buildUrl(path, options.query);
    const expectedStatusCodes = options.expectedStatuses ?? [200, 201, 202, 204];
    const requestBody = options.body === undefined ? null : JSON.stringify(options.body);
    const canRetryUnauthorized = typeof this.tokenProvider.invalidateAccessToken === 'function';
    const firstToken = this.tokenProvider.getAccessToken();
    const firstResponse = http.request(
      method,
      url,
      requestBody,
      this.buildParams(
        method,
        firstToken,
        tags,
        canRetryUnauthorized ? [...expectedStatusCodes, 401] : expectedStatusCodes,
      ),
    );
    const response = this.shouldRetryUnauthorized(firstResponse)
      ? this.retryAfterUnauthorized(method, url, requestBody, tags, expectedStatusCodes, firstToken)
      : firstResponse;
    const resultTags = { ...tags, status: String(response.status) };
    const ok = operationSucceeded(response, expectedStatusCodes, tags);

    recordBusinessOperation(response.timings.duration, !ok, resultTags);
    this.recordSpecializedDuration(response.timings.duration, resultTags);

    if (!ok) {
      console.warn(`[ngb-perf] HTTP request failed: ${JSON.stringify(safeErrorSummary(response))}`);
    }

    return response;
  }

  private buildParams(
    method: string,
    accessToken: string,
    tags: Record<string, string>,
    expectedStatusCodes: readonly number[],
  ): Params {
    return {
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${accessToken}`,
        ...(method === 'GET' || method === 'DELETE' ? {} : { 'Content-Type': 'application/json' }),
      },
      tags,
      timeout: this.timeout,
      responseCallback: expectedStatuses(...expectedStatusCodes),
    };
  }

  private shouldRetryUnauthorized(response: NgbHttpResponse): boolean {
    return response.status === 401 && typeof this.tokenProvider.invalidateAccessToken === 'function';
  }

  private retryAfterUnauthorized(
    method: string,
    url: string,
    requestBody: string | null,
    tags: Record<string, string>,
    expectedStatusCodes: readonly number[],
    failedAccessToken: string,
  ): NgbHttpResponse {
    this.tokenProvider.invalidateAccessToken?.(failedAccessToken);
    const retryToken = this.tokenProvider.getAccessToken();

    if (retryToken === failedAccessToken) {
      return http.request(
        method,
        url,
        requestBody,
        this.buildParams(method, retryToken, tags, expectedStatusCodes),
      );
    }

    return http.request(
      method,
      url,
      requestBody,
      this.buildParams(method, retryToken, { ...tags, auth_retry: 'true' }, expectedStatusCodes),
    );
  }

  private buildUrl(path: string, query?: Record<string, QueryValue>): string {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    const queryString = query ? buildQueryString(query) : '';
    return `${this.env.apiBaseUrl}${normalizedPath}${queryString}`;
  }

  private recordSpecializedDuration(durationMs: number, tags: Record<string, string>): void {
    switch (tags.area) {
      case 'documents':
        if (tags.operation?.includes('.post')) {
          documentPostDuration.add(durationMs, tags);
        }
        break;
      case 'reports':
        reportExecutionDuration.add(durationMs, tags);
        break;
      case 'accounting':
        accountingEffectsDuration.add(durationMs, tags);
        break;
      case 'document-flow':
        documentFlowDuration.add(durationMs, tags);
        break;
    }
  }
}

function buildQueryString(query: Record<string, QueryValue>): string {
  const parts: string[] = [];

  for (const [key, value] of Object.entries(query)) {
    if (value === null || value === undefined || value === '') {
      continue;
    }

    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  }

  return parts.length > 0 ? `?${parts.join('&')}` : '';
}
