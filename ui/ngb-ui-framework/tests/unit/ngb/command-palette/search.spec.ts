import { describe, expect, it } from 'vitest'

import {
  defaultSearchFields,
  groupOrder,
  normalizeSearchText,
  parseCommandPaletteQuery,
  scoreSearchText,
} from '../../../../src/ngb/command-palette/search'

describe('command palette search helpers', () => {
  it('normalizes search text by lowercasing, stripping punctuation, and collapsing whitespace', () => {
    expect(normalizeSearchText('  Open, Invoice #42!  ')).toBe('open invoice 42')
    expect(normalizeSearchText('Customer___Ledger')).toBe('customer ledger')
    expect(normalizeSearchText('')).toBe('')
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
  })

  it('orders palette groups with actions first and recent last', () => {
    expect(groupOrder('actions')).toBeLessThan(groupOrder('go-to'))
    expect(groupOrder('go-to')).toBeLessThan(groupOrder('reports'))
    expect(groupOrder('reports')).toBeLessThan(groupOrder('recent'))
  })
})
