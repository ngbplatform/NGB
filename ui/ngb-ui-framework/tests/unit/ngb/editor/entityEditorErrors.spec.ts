import { describe, expect, it, vi } from 'vitest'

const ApiErrorMock = vi.hoisted(() => class ApiError extends Error {
  readonly status: number
  readonly url: string
  readonly body?: unknown
  readonly errorCode?: string | null
  readonly context?: Record<string, unknown> | null
  readonly errors?: Record<string, string[]> | null
  readonly issues?: Array<{ path: string; message: string; scope: string; code?: string | null }> | null

  constructor(args: {
    message: string
    status: number
    url: string
    body?: {
      error?: {
        code?: string | null
        context?: Record<string, unknown> | null
        issues?: Array<{ path: string; message: string; scope: string; code?: string | null }> | null
      }
      errors?: Record<string, string[]>
    }
  }) {
    super(args.message)
    this.name = 'ApiError'
    this.status = args.status
    this.url = args.url
    this.body = args.body
    this.errorCode = args.body?.error?.code ?? null
    this.context = args.body?.error?.context ?? null
    this.issues = args.body?.error?.issues ?? null
    this.errors = args.body?.errors ?? null
  }
})

vi.mock('../../../../src/ngb/api/http', () => ({
  ApiError: ApiErrorMock,
}))

import { ApiError } from '../../../../src/ngb/api/http'
import {
  dedupeEntityEditorMessages,
  humanizeEntityEditorFieldKey,
  isEntityEditorFormIssuePath,
  normalizeEntityEditorError,
} from '../../../../src/ngb/editor/entityEditorErrors'

describe('entity editor error helpers', () => {
  it('dedupes messages case-insensitively and humanizes common field keys', () => {
    expect(dedupeEntityEditorMessages([' Required ', 'required', '', 'Still broken'])).toEqual([
      'Required',
      'Still broken',
    ])
    expect(humanizeEntityEditorFieldKey('posted_at_utc')).toBe('Posted At')
    expect(humanizeEntityEditorFieldKey('property_id')).toBe('Property')
    expect(humanizeEntityEditorFieldKey('line12')).toBe('Line 12')
    expect(isEntityEditorFormIssuePath('')).toBe(true)
    expect(isEntityEditorFormIssuePath('_form')).toBe(true)
    expect(isEntityEditorFormIssuePath('customer_id')).toBe(false)
  })

  it('normalizes api validation issues into grouped editor issues with highlight summary', () => {
    const error = new ApiError({
      message: 'Validation failed',
      status: 422,
      url: '/api/documents/pm.invoice/doc-1',
      body: {
        error: {
          code: 'validation_failed',
          context: {
            entity: 'pm.invoice',
          },
          issues: [
            {
              path: 'customer_id',
              message: 'Required',
              scope: 'field',
              code: 'required',
            },
            {
              path: 'customer_id',
              message: ' required ',
              scope: 'field',
            },
            {
              path: '',
              message: 'Document cannot be saved yet.',
              scope: '',
            },
          ],
        },
      },
    })

    const normalized = normalizeEntityEditorError(error, {
      resolveIssueLabel: (path) => {
        if (path === 'customer_id') return 'Customer'
        if (path === '_form') return 'Validation'
        return path
      },
    })

    expect(normalized).toEqual({
      summary: 'Please fix the highlighted fields.',
      issues: [
        {
          path: 'customer_id',
          label: 'Customer',
          scope: 'field',
          messages: ['Required'],
          code: 'required',
        },
        {
          path: '_form',
          label: 'Validation',
          scope: 'form',
          messages: ['Document cannot be saved yet.'],
          code: null,
        },
      ],
      errorCode: 'validation_failed',
      status: 422,
      context: {
        entity: 'pm.invoice',
      },
    })
  })

  it('keeps the original summary for form-only validation and plain runtime errors', () => {
    const validationError = new ApiError({
      message: 'Cannot post document.',
      status: 400,
      url: '/api/documents/pm.invoice/doc-1/post',
      body: {
        errors: {
          _form: [' Cannot post document. ', 'cannot post document.'],
        },
      },
    })

    expect(normalizeEntityEditorError(validationError)).toEqual({
      summary: 'Cannot post document.',
      issues: [
        {
          path: '_form',
          label: 'Validation',
          scope: 'form',
          messages: ['Cannot post document.'],
          code: null,
        },
      ],
      errorCode: null,
      status: 400,
      context: null,
    })

    expect(normalizeEntityEditorError(new Error('Network unavailable'))).toEqual({
      summary: 'Network unavailable',
      issues: [],
      errorCode: null,
      status: null,
      context: null,
    })
  })
})
