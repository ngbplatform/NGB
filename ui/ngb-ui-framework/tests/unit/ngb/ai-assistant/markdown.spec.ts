import { afterEach, expect, it, vi } from 'vitest'
import { renderAssistantMarkdown } from '../../../../src/ngb/ai-assistant/markdown'
import { assistantSourcePath } from '../../../../src/ngb/ai-assistant/navigation'

afterEach(() => vi.unstubAllGlobals())

it('renders verified timestamps, exact byte sizes and literal filenames and notes without interpreting their contents', () => {
  const prefix = '{{ngb-value:abcdef012345-'
  const result = renderAssistantMarkdown(`${prefix}0}} | ${prefix}1}} | ${prefix}2}} | ${prefix}3}}`, [], 'America/New_York', [
    { reference: `${prefix}0}}`, kind: 'timestamp', value: '2026-05-10T20:43:34.293462Z' },
    { reference: `${prefix}1}}`, kind: 'bytes', value: '723275' },
    { reference: `${prefix}2}}`, kind: 'text', value: 'Screenshot 2026-10-02 at 5.46.52 PM.png' },
    { reference: `${prefix}3}}`, kind: 'text', value: '2026-05-10 | **literal** <img src=x> [click](https://evil.example)' },
  ])

  expect(result).toContain('05/10/2026, 04:43:34 PM')
  expect(result).toContain('723,275 bytes')
  expect(result).toContain('Screenshot 2026-10-02 at 5.46.52 PM.png')
  expect(result).toContain('2026-05-10 | **literal** &lt;img src=x&gt;')
  expect(result).not.toContain('<img')
  expect(result).not.toContain('<a ')
  expect(result).not.toContain('<strong>')
})

it('does not accept display references supplied only by the model', () => {
  const result = renderAssistantMarkdown('{{ngb-value:abcdef012345-0}}', [])
  expect(result).toContain('Value unavailable')
  expect(result).not.toContain('ngb-value:')
})

it('formats explicit UTC prose without converting dates embedded in filenames or code', () => {
  const result = renderAssistantMarkdown(
    '05/10/2026 at 20:43:35 UTC\n\nScreenshot 2026-10-02 at 5.46.52 PM.png\n\n`2026-10-02.json`',
    [], 'America/New_York')
  expect(result).toContain('05/10/2026, 04:43:35 PM')
  expect(result).toContain('Screenshot 2026-10-02 at 5.46.52 PM.png')
  expect(result).toContain('<code>2026-10-02.json</code>')
})

it('preserves wide tables as tables with every requested column', () => {
  const result = renderAssistantMarkdown('| Lease | Tenant | Property | Start | End | Rent |\n| --- | --- | --- | --- | --- | --- |\n| A | B | C | D | E | F |', [])
  expect(result).toContain('<table>')
  expect(result).toContain('<th>Tenant</th>')
  expect(result).toContain('<th>Rent</th>')
  expect(result.match(/<td>/g)).toHaveLength(6)
})

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

it('opens only a verified audit view and rejects invented views', () => {
  vi.stubGlobal('window', { location: { origin: 'https://app.example' } })
  const id = '11111111-1111-4111-8111-111111111111'
  const source = { kind: 'document' as const, typeCode: 'example.record', id, title: 'Audit history', view: 'audit' as const }
  expect(assistantSourcePath(source)).toBe(`/documents/example.record/${id}?view=audit`)
  const result = renderAssistantMarkdown(`[Audit](ngb-source:document:example.record:${id}:audit)`, [source])
  expect(result).toContain(`href="/documents/example.record/${id}?view=audit"`)
  expect(renderAssistantMarkdown(`[Audit](ngb-source:document:example.record:${id}:audit)`, [{ ...source, view: null }]))
    .not.toContain('href=')
  expect(assistantSourcePath({ ...source, kind: 'report' })).toBeNull()
})
