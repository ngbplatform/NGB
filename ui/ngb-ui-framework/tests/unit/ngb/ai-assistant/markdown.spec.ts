import { afterEach, expect, it, vi } from 'vitest'
import { renderAssistantMarkdown } from '../../../../src/ngb/ai-assistant/markdown'
import { assistantSourcePath } from '../../../../src/ngb/ai-assistant/navigation'

afterEach(() => vi.unstubAllGlobals())

it('formats timestamps in the user time zone without shifting date-only values', () => {
  const result = renderAssistantMarkdown(
    '2026-05-10T20:43:34.293462Z; 2026-01-10T20:43:34Z; 2026-05-10; 2026-05-11T01:00:00+02:00',
    [], 'America/New_York')

  expect(result).toContain('05/10/2026, 04:43:34 PM')
  expect(result).toContain('01/10/2026, 03:43:34 PM')
  expect(result).toContain('05/10/2026;')
  expect(result).toContain('05/10/2026, 07:00:00 PM')
  expect(result).not.toContain('293462')
})

it('preserves invalid dates and timestamps without a known offset', () => {
  const result = renderAssistantMarkdown('2026-02-30; 2026-05-10T20:43:34', [], 'UTC')
  expect(result).toContain('2026-02-30')
  expect(result).toContain('2026-05-10T20:43:34')
})

it('renders Markdown tables in a keyboard-accessible scroll region and does not append discovered sources', () => {
  vi.stubGlobal('window', { location: { origin: 'https://app.example' } })
  const result = renderAssistantMarkdown('| Name | Amount |\n| --- | ---: |\n| Rent | 20 |',
    [{ kind: 'report', typeCode: 'example.summary', title: 'Unrequested report' }])
  expect(result).toContain('class="assistant-table" role="region"')
  expect(result).toContain('<table>')
  expect(result).toContain('<th style="text-align:right">Amount</th>')
  expect(result).not.toContain('Unrequested report')
})

it('renders useful formatting while refusing HTML, remote images and invented links', () => {
  vi.stubGlobal('window', { location: { origin: 'https://app.example' } })
  const result = renderAssistantMarkdown(
    '**Seven** records. <script>alert(1)</script> ![tracking](https://evil.example/pixel) '
    + '[bad](javascript:alert(1)) [remote](https://evil.example/) [invented](/catalogs/fake) '
    + '[source](/reports/pm.aging)', [{ kind: 'report', typeCode: 'pm.aging', title: 'Aging' }])

  expect(result).toContain('<strong>Seven</strong>')
  expect(result).not.toContain('<script>')
  expect(result).not.toContain('<img')
  expect(result).not.toContain('href="https://evil')
  expect(result).not.toContain('href="javascript:')
  expect(result).not.toContain('href="/catalogs/fake')
  expect(result).toContain('href="/reports/pm.aging"')
  expect(result).toContain('>Aging</a>')
})

it('resolves verified record references and replaces model labels with the actual Display', () => {
  vi.stubGlobal('window', { location: { origin: 'https://app.example' } })
  const id = '11111111-1111-4111-8111-111111111111'
  const display = 'Receivable Payment RP-2026-000014 8/24/2025'
  const path = `/documents/pm.receivable_payment/${id}`
  const sources = [{ kind: 'document' as const, typeCode: 'pm.receivable_payment', id, title: display }]
  const result = renderAssistantMarkdown(
    `[wrong title](ngb-source:document:pm.receivable_payment:${id}) https://app.example${path}`, sources)

  expect(result.match(new RegExp(`href="${path}"`, 'g'))).toHaveLength(2)
  expect(result.match(new RegExp(`>${display}</a>`, 'g'))).toHaveLength(2)
  expect(result).not.toContain('wrong title')
  expect(result).not.toContain('https://')
})

it('never displays invented URLs or accepts references supplied only by the model', () => {
  vi.stubGlobal('window', { location: { origin: 'https://app.example' } })
  const result = renderAssistantMarkdown(
    'https://app.noble-gateway.com/pm.receivable_payment/RP-2026-000014 '
    + '[Payment](ngb-source:document:pm.receivable_payment:11111111-1111-4111-8111-111111111111) '
    + '`https://invented.example/document`\n\n```text\nhttps://invented.example/document\n```', [])

  expect(result).not.toContain('href=')
  expect(result).not.toContain('https://')
  expect(result).not.toContain('ngb-source:')
  expect(result).toContain('Link unavailable')
})

it('uses only validated sources and existing navigation targets', () => {
  const id = '11111111-1111-4111-8111-111111111111'
  expect(assistantSourcePath({ kind: 'catalog', typeCode: 'pm.property', id, title: 'Building' }))
    .toBe(`/catalogs/pm.property/${id}`)
  expect(assistantSourcePath({ kind: 'document', typeCode: '//evil.example', id, title: 'Bad' })).toBeNull()
  expect(assistantSourcePath({ kind: 'generalJournalEntry', typeCode: 'general_journal_entry', id, title: 'Journal entry' }))
    .toBe(`/accounting/general-journal-entries/${id}`)
})
