import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  clearDocumentCopyDraft,
  readDocumentCopyDraft,
  saveDocumentCopyDraft,
} from '../../../../src/ngb/editor/documentCopyDraft'

type GlobalWithCopyDraftStore = typeof globalThis & {
  __ngbDocumentCopyDraftMemoryStore?: Map<string, string>
}

const STORAGE_KEY_PREFIX = 'ngb:document-copy-draft:'

function memoryStore(): Map<string, string> {
  const root = globalThis as GlobalWithCopyDraftStore
  if (!root.__ngbDocumentCopyDraftMemoryStore) {
    root.__ngbDocumentCopyDraftMemoryStore = new Map<string, string>()
  }
  return root.__ngbDocumentCopyDraftMemoryStore
}

describe('document copy draft storage', () => {
  beforeEach(() => {
    memoryStore().clear()
  })

  afterEach(() => {
    vi.useRealTimers()
    memoryStore().clear()
  })

  it('saves and reads a sanitized draft snapshot', () => {
    const fieldsPayload: Record<string, unknown> = {
      title: 'Invoice INV-001',
      amount: 1250,
      approved: true,
      tags: ['rent', undefined, Symbol('skip'), () => 'ignored'],
    }
    const cyclic = {
      nested: 'value',
      count: 7n,
      empty: undefined,
      run: () => 'ignored',
      marker: Symbol('skip'),
    } as Record<string, unknown>
    cyclic.self = cyclic
    fieldsPayload.details = cyclic

    const partRow = {
      line_no: 1,
      amount: 750n,
      note: 'Base rent',
    } as Record<string, unknown>
    partRow.loop = partRow

    const token = saveDocumentCopyDraft({
      documentType: 'pm.invoice',
      fields: fieldsPayload,
      parts: {
        lines: {
          rows: [partRow],
        },
      },
    })

    expect(token).toEqual(expect.any(String))
    expect(token).not.toBeNull()

    const snapshot = readDocumentCopyDraft(token, 'pm.invoice')
    expect(snapshot).toEqual({
      documentType: 'pm.invoice',
      fields: {
        title: 'Invoice INV-001',
        amount: 1250,
        approved: true,
        tags: ['rent', null, null, null],
        details: {
          nested: 'value',
          count: '7',
          run: null,
          marker: null,
          self: null,
        },
      },
      parts: {
        lines: {
          rows: [
            {
              line_no: 1,
              amount: '750',
              note: 'Base rent',
              loop: null,
            },
          ],
        },
      },
    })
  })

  it('rejects mismatched types and clears snapshots explicitly', () => {
    const token = saveDocumentCopyDraft({
      documentType: 'pm.invoice',
      fields: {
        memo: 'April billing',
      },
      parts: null,
    })

    expect(readDocumentCopyDraft(token, 'pm.credit_note')).toBeNull()

    clearDocumentCopyDraft(token)

    expect(readDocumentCopyDraft(token, 'pm.invoice')).toBeNull()
  })

  it('cleans up expired or malformed snapshots before reading and writing', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-08T12:00:00Z'))

    memoryStore().set(
      `${STORAGE_KEY_PREFIX}expired`,
      JSON.stringify({
        version: 1,
        documentType: 'pm.invoice',
        fields: {
          title: 'Old invoice',
        },
        createdAtUtc: '2026-04-08T05:59:59.000Z',
      }),
    )
    memoryStore().set(`${STORAGE_KEY_PREFIX}broken`, '{')

    expect(readDocumentCopyDraft('expired', 'pm.invoice')).toBeNull()
    expect(memoryStore().has(`${STORAGE_KEY_PREFIX}expired`)).toBe(false)
    expect(memoryStore().has(`${STORAGE_KEY_PREFIX}broken`)).toBe(false)

    const freshToken = saveDocumentCopyDraft({
      documentType: 'pm.invoice',
      fields: {
        title: 'Fresh invoice',
      },
      parts: null,
    })

    expect(freshToken).toEqual(expect.any(String))
    expect(Array.from(memoryStore().keys())).toHaveLength(1)
  })
})
