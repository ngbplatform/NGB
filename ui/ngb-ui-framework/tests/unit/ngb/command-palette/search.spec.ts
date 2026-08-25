import { describe, expect, it } from 'vitest'

import {
  defaultSearchFields,
  groupOrder,
  normalizeSearchText,
  parseCommandPaletteQuery,
  prefixToScope,
  scoreSearchText,
} from '../../../../src/ngb/command-palette/search'

describe('command palette search helpers', () => {
  it('normalizes search text by lowercasing, stripping punctuation, and collapsing whitespace', () => {
    expect(normalizeSearchText('  Open, Invoice #42!  ')).toBe('open invoice 42')
    expect(normalizeSearchText('Customer___Ledger')).toBe('customer ledger')
    expect(normalizeSearchText('')).toBe('')
    expect(normalizeSearchText(null)).toBe('')
    expect(normalizeSearchText(undefined)).toBe('')
    expect(normalizeSearchText(' !!! ')).toBe('')
  })

  it('parses scope prefixes while preserving raw queries', () => {
    expect(parseCommandPaletteQuery('  > post document ')).toEqual({
      rawQuery: '  > post document ',
      query: 'post document',
      scope: 'commands',
    })
    expect(parseCommandPaletteQuery('/payables')).toEqual({
      rawQuery: '/payables',
      query: 'payables',
      scope: 'pages',
    })
    expect(parseCommandPaletteQuery('invoice 42')).toEqual({
      rawQuery: 'invoice 42',
      query: 'invoice 42',
      scope: null,
    })
    expect(parseCommandPaletteQuery(null as never)).toEqual({
      rawQuery: '',
      query: '',
      scope: null,
    })

    expect([
      prefixToScope('>'),
      prefixToScope('/'),
      prefixToScope('#'),
      prefixToScope(':'),
      prefixToScope('@'),
      prefixToScope('?'),
    ]).toEqual(['commands', 'pages', 'reports', 'documents', 'catalogs', null])

    expect(parseCommandPaletteQuery('# trial balance')).toMatchObject({ query: 'trial balance', scope: 'reports' })
    expect(parseCommandPaletteQuery(': invoice')).toMatchObject({ query: 'invoice', scope: 'documents' })
    expect(parseCommandPaletteQuery('@ customer')).toMatchObject({ query: 'customer', scope: 'catalogs' })
  })

  it('scores exact, prefix, word-prefix, and contains matches in descending quality order', () => {
    const fields = defaultSearchFields('Customer Invoice', 'April recurring rent')

    const exact = scoreSearchText('customer invoice', fields)
    const prefix = scoreSearchText('customer', fields)
    const wordPrefix = scoreSearchText('invoice', fields)
    const contains = scoreSearchText('curr', fields)

    expect(exact).toBeGreaterThan(prefix)
    expect(prefix).toBeGreaterThan(wordPrefix)
    expect(wordPrefix).toBeGreaterThan(contains)
    expect(contains).toBeGreaterThan(0)
    expect(scoreSearchText('missing', fields)).toBe(0)
    expect(scoreSearchText('', fields)).toBe(0)
    expect(scoreSearchText('customer', [])).toBe(0)
    expect(scoreSearchText('customer', [{ value: null, exact: 1, prefix: 1, wordPrefix: 1, contains: 1 }])).toBe(0)
  })

  it('drops blank default fields and applies lower weights to secondary fields', () => {
    expect(defaultSearchFields('', ' ', null, undefined)).toEqual([])
    expect(defaultSearchFields('Primary', 'Secondary')).toEqual([
      { value: 'Primary', exact: 1, prefix: 0.92, wordPrefix: 0.88, contains: 0.76 },
      { value: 'Secondary', exact: 0.86, prefix: 0.8, wordPrefix: 0.76, contains: 0.68 },
    ])
  })

  it('orders palette groups with actions first and recent last', () => {
    expect(groupOrder('actions')).toBeLessThan(groupOrder('go-to'))
    expect(groupOrder('go-to')).toBeLessThan(groupOrder('reports'))
    expect(groupOrder('reports')).toBeLessThan(groupOrder('recent'))
    expect([
      groupOrder('actions'),
      groupOrder('go-to'),
      groupOrder('documents'),
      groupOrder('catalogs'),
      groupOrder('reports'),
      groupOrder('recent'),
      groupOrder('unknown' as never),
    ]).toEqual([0, 1, 2, 3, 4, 5, 99])
  })
})
