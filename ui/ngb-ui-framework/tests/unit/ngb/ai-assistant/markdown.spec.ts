import { afterEach, expect, it, vi } from 'vitest'
import { renderAssistantMarkdown } from '../../../../src/ngb/ai-assistant/markdown'
import { assistantPageContext, assistantSourcePath } from '../../../../src/ngb/ai-assistant/navigation'

afterEach(() => vi.unstubAllGlobals())

it('renders useful formatting while refusing HTML, remote images and invented links', () => {
  vi.stubGlobal('window', { location: { origin: 'https://app.example' } })
  const result = renderAssistantMarkdown(
    '**Seven** records. <script>alert(1)</script> ![tracking](https://evil.example/pixel) '
    + '[bad](javascript:alert(1)) [remote](https://evil.example/) [invented](/catalogs/fake) '
    + '[source](/reports/pm.aging)', ['/reports/pm.aging'])

  expect(result).toContain('<strong>Seven</strong>')
  expect(result).not.toContain('<script>')
  expect(result).not.toContain('<img')
  expect(result).not.toContain('href="https://evil')
  expect(result).not.toContain('href="javascript:')
  expect(result).not.toContain('href="/catalogs/fake')
  expect(result).toContain('href="/reports/pm.aging"')
})

it('uses only validated page identifiers and existing navigation targets', () => {
  const id = '11111111-1111-4111-8111-111111111111'
  expect(assistantPageContext(`/catalogs/pm.property/${id}`)).toEqual({ kind: 'catalog', typeCode: 'pm.property', id })
  expect(assistantPageContext('/reports/pm.aging')).toEqual({ kind: 'report', typeCode: 'pm.aging' })
  expect(assistantPageContext('/catalogs/%2Fetc')).toBeNull()
  expect(assistantPageContext('/catalogs/pm.property/new')).toBeNull()
  expect(assistantSourcePath({ kind: 'catalog', typeCode: 'pm.property', id, title: 'Building' }))
    .toBe(`/catalogs/pm.property/${id}`)
  expect(assistantSourcePath({ kind: 'document', typeCode: '//evil.example', id, title: 'Bad' })).toBeNull()
  expect(assistantPageContext(`/accounting/general-journal-entries/${id}`))
    .toEqual({ kind: 'generalJournalEntry', typeCode: 'general_journal_entry', id })
  expect(assistantPageContext('/accounting/general-journal-entries/new')).toBeNull()
  expect(assistantSourcePath({ kind: 'generalJournalEntry', typeCode: 'general_journal_entry', id, title: 'Journal entry' }))
    .toBe(`/accounting/general-journal-entries/${id}`)
})
