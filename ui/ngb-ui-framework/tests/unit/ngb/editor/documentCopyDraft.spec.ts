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
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
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
    memoryStore().set(
      `${STORAGE_KEY_PREFIX}missing-created-at`,
      JSON.stringify({ version: 1, documentType: 'pm.invoice', fields: {} }),
    )

    expect(readDocumentCopyDraft('expired', 'pm.invoice')).toBeNull()
    expect(memoryStore().has(`${STORAGE_KEY_PREFIX}expired`)).toBe(false)
    expect(memoryStore().has(`${STORAGE_KEY_PREFIX}broken`)).toBe(false)
    expect(memoryStore().has(`${STORAGE_KEY_PREFIX}missing-created-at`)).toBe(false)

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

  it('bounds retained snapshot count and rejects oversized drafts', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-08T12:00:00Z'))
    const tokens: string[] = []

    for (let index = 0; index < 20; index += 1) {
      const token = saveDocumentCopyDraft({
        documentType: 'pm.invoice',
        fields: { index },
        parts: null,
      })
      expect(token).not.toBeNull()
      tokens.push(token!)
      vi.advanceTimersByTime(1_000)
    }

    expect(Array.from(memoryStore().keys())).toHaveLength(16)
    expect(readDocumentCopyDraft(tokens[0], 'pm.invoice')).toBeNull()
    expect(readDocumentCopyDraft(tokens.at(-1), 'pm.invoice')).toMatchObject({
      fields: { index: 19 },
    })
    expect(saveDocumentCopyDraft({
      documentType: 'pm.invoice',
      fields: { payload: 'x'.repeat(600_000) },
      parts: null,
    })).toBeNull()
  })

  it('rejects every invalid snapshot shape and empty tokens', () => {
    const createdAtUtc = new Date().toISOString()
    const invalidPayloads = [
      { version: 2, documentType: 'pm.invoice', fields: {}, createdAtUtc },
      { version: 1, documentType: 42, fields: {}, createdAtUtc },
      { version: 1, documentType: ' ', fields: {}, createdAtUtc },
      { version: 1, documentType: 'pm.invoice', fields: [], createdAtUtc },
      { version: 1, documentType: 'pm.invoice', fields: {}, parts: [], createdAtUtc },
    ]

    invalidPayloads.forEach((payload, index) => {
      memoryStore().set(`${STORAGE_KEY_PREFIX}invalid-${index}`, JSON.stringify(payload))
      expect(readDocumentCopyDraft(`invalid-${index}`)).toBeNull()
    })
    memoryStore().set(`${STORAGE_KEY_PREFIX}invalid-json`, '{')
    expect(readDocumentCopyDraft('invalid-json')).toBeNull()
    expect(readDocumentCopyDraft(null)).toBeNull()
    expect(readDocumentCopyDraft(undefined)).toBeNull()
    expect(readDocumentCopyDraft('   ')).toBeNull()
    expect(() => clearDocumentCopyDraft(null)).not.toThrow()
    expect(() => clearDocumentCopyDraft('   ')).not.toThrow()
  })

  it('uses session storage first and falls back to local storage when session writes fail', () => {
    const session = createStorageMock({ throwOnSet: true })
    const local = createStorageMock()
    vi.stubGlobal('window', { sessionStorage: session, localStorage: local })

    const token = saveDocumentCopyDraft({
      documentType: 'pm.invoice',
      fields: { memo: 'Stored locally' },
      parts: null,
    })
    expect(token).not.toBeNull()
    expect(Object.keys(local)).toEqual([`${STORAGE_KEY_PREFIX}${token}`])
    expect(readDocumentCopyDraft(token, 'pm.invoice')).toMatchObject({
      fields: { memo: 'Stored locally' },
    })

    local.unrelated = 'keep'
    local[`${STORAGE_KEY_PREFIX}missing`] = 42
    saveDocumentCopyDraft({ documentType: 'pm.invoice', fields: {}, parts: null })
    expect(local.unrelated).toBe('keep')
    expect(Object.hasOwn(local, `${STORAGE_KEY_PREFIX}missing`)).toBe(false)

    clearDocumentCopyDraft(token)
    expect(Object.hasOwn(local, `${STORAGE_KEY_PREFIX}${token}`)).toBe(false)
  })

  it('prefers a working session store and falls back to memory when both browser stores reject writes', () => {
    const session = createStorageMock()
    const local = createStorageMock()
    vi.stubGlobal('window', { sessionStorage: session, localStorage: local })

    const sessionToken = saveDocumentCopyDraft({
      documentType: 'pm.invoice',
      fields: { memo: 'Stored in session' },
      parts: null,
    })
    expect(sessionToken).not.toBeNull()
    expect(Object.hasOwn(session, `${STORAGE_KEY_PREFIX}${sessionToken}`)).toBe(true)
    expect(Object.keys(local)).toEqual([])

    vi.stubGlobal('window', {
      sessionStorage: createStorageMock({ throwOnSet: true }),
      localStorage: createStorageMock({ throwOnSet: true }),
    })
    const memoryToken = saveDocumentCopyDraft({
      documentType: 'pm.invoice',
      fields: { memo: 'Stored in memory' },
      parts: null,
    })
    expect(memoryToken).not.toBeNull()
    expect(readDocumentCopyDraft(memoryToken, 'pm.invoice')).toMatchObject({
      fields: { memo: 'Stored in memory' },
    })
  })

  it('rejects snapshots that change between cleanup and parsing', () => {
    const createdAtUtc = new Date().toISOString()
    const valid = JSON.stringify({
      version: 1,
      documentType: 'pm.invoice',
      fields: {},
      createdAtUtc,
    })
    const invalidCreatedAtKey = `${STORAGE_KEY_PREFIX}invalid-created-at`
    const malformedKey = `${STORAGE_KEY_PREFIX}malformed-after-cleanup`
    const readCounts = new Map<string, number>()
    const values = new Map<string, [string, string]>([
      [invalidCreatedAtKey, [valid, JSON.stringify({
        version: 1,
        documentType: 'pm.invoice',
        fields: {},
        createdAtUtc: 42,
      })]],
      [malformedKey, [valid, '{']],
    ])
    const session = {
      [invalidCreatedAtKey]: valid,
      [malformedKey]: valid,
      getItem(key: string) {
        const pair = values.get(key)
        if (!pair) return null
        const count = readCounts.get(key) ?? 0
        readCounts.set(key, count + 1)
        return pair[Math.min(count, pair.length - 1)]
      },
      setItem() {},
      removeItem() {},
    } as unknown as Storage
    vi.stubGlobal('window', {
      sessionStorage: session,
      localStorage: createStorageMock(),
    })

    expect(readDocumentCopyDraft('invalid-created-at')).toBeNull()
    expect(readDocumentCopyDraft('malformed-after-cleanup')).toBeNull()
  })

  it('creates the process-wide memory store lazily', () => {
    const root = globalThis as GlobalWithCopyDraftStore
    delete root.__ngbDocumentCopyDraftMemoryStore

    const token = saveDocumentCopyDraft({
      documentType: 'pm.invoice',
      fields: { memo: 'First draft' },
      parts: null,
    })

    expect(token).not.toBeNull()
    expect(root.__ngbDocumentCopyDraftMemoryStore).toBeInstanceOf(Map)
  })

  it('returns null when even the in-memory store rejects a write and sanitizes absent fields', () => {
    const store = memoryStore()
    vi.spyOn(store, 'set').mockImplementationOnce(() => { throw new Error('memory full') })
    expect(saveDocumentCopyDraft({ documentType: 'pm.invoice', fields: null as never })).toBeNull()

    const token = saveDocumentCopyDraft({ documentType: 'pm.invoice', fields: null as never })
    expect(readDocumentCopyDraft(token)).toEqual({
      documentType: 'pm.invoice',
      fields: {},
      parts: null,
    })
  })
})

function createStorageMock(options: { throwOnSet?: boolean } = {}): Storage & Record<string, unknown> {
  const storage: Record<string, unknown> = {}
  Object.defineProperties(storage, {
    getItem: {
      enumerable: false,
      value: (key: string) => typeof storage[key] === 'string' ? storage[key] : null,
    },
    setItem: {
      enumerable: false,
      value: (key: string, value: string) => {
        if (options.throwOnSet) throw new Error('storage full')
        storage[key] = value
      },
    },
    removeItem: {
      enumerable: false,
      value: (key: string) => { delete storage[key] },
    },
  })
  return storage as Storage & Record<string, unknown>
}
