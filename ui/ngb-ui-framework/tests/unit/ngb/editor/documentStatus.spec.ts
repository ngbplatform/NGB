import { describe, expect, it } from 'vitest'

import {
  documentStatusLabel,
  documentStatusTone,
  documentStatusVisual,
  normalizeDocumentStatusValue,
} from '../../../../src/ngb/editor/documentStatus'

describe('document status helpers', () => {
  it('normalizes numeric values and deletion aliases into known framework statuses', () => {
    expect(normalizeDocumentStatusValue(1)).toBe(1)
    expect(normalizeDocumentStatusValue('2')).toBe(2)
    expect(normalizeDocumentStatusValue('posted')).toBe(2)
    expect(normalizeDocumentStatusValue('marked for deletion')).toBe(3)
    expect(normalizeDocumentStatusValue('marked_for_deletion')).toBe(3)
    expect(normalizeDocumentStatusValue('unexpected')).toBe(1)
  })

  it('derives labels, tones, and visuals from normalized statuses', () => {
    expect(documentStatusLabel('draft')).toBe('Draft')
    expect(documentStatusTone('draft')).toBe('neutral')
    expect(documentStatusVisual('draft')).toBe('saved')

    expect(documentStatusLabel('posted')).toBe('Posted')
    expect(documentStatusTone('posted')).toBe('success')
    expect(documentStatusVisual('posted')).toBe('posted')

    expect(documentStatusLabel('marked-for-deletion')).toBe('Deleted')
    expect(documentStatusTone('marked-for-deletion')).toBe('warn')
    expect(documentStatusVisual('marked-for-deletion')).toBe('marked')
  })
})
