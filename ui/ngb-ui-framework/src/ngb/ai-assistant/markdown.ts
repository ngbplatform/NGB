import MarkdownIt from 'markdown-it'
import { assistantSourcePath } from './navigation'
import { formatAssistantDates } from './dateFormatting'
import type { AssistantSource } from './types'

const addressPattern = /https?:\/\/[^\s<>"']+|ngb-source:[a-zA-Z0-9_.:-]+/gi

/** Only sources returned by the application can supply a link and its display text. */
export function renderAssistantMarkdown(
  text: string,
  sources: AssistantSource[],
  timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone,
): string {
  const markdown = new MarkdownIt({ html: false, linkify: true, breaks: true })
  const verified = new Map<string, { path: string; title: string }>()

  for (const source of sources) {
    const path = assistantSourcePath(source)
    if (!path) continue
    const reference = `ngb-source:${source.kind}:${source.typeCode}${source.id ? `:${source.id}` : ''}`
    const link = { path, title: source.title }
    verified.set(reference, link)
    verified.set(new URL(path, window.location.origin).href, link)
  }

  function resolve(value: string) {
    if (verified.has(value)) return verified.get(value)
    try {
      return verified.get(new URL(value, window.location.origin).href)
    } catch {
      return undefined
    }
  }

  function hideAddresses(value: string): string {
    return value.replace(addressPattern, address => resolve(address)?.title ?? 'Link unavailable')
  }

  markdown.core.ruler.after('linkify', 'verified-sources', state => {
    for (const block of state.tokens) {
      if (block.type === 'fence' || block.type === 'code_block') block.content = hideAddresses(block.content)
      const tokens = block.children
      if (!tokens) continue

      for (let index = 0; index < tokens.length; index++) {
        const token = tokens[index]!
        if (token.type === 'link_open') {
          const end = tokens.findIndex((candidate, position) => position > index && candidate.type === 'link_close')
          if (end < 0) continue
          const source = resolve(String(token.attrGet('href') ?? ''))
          const label = new state.Token('text', '', 0)
          label.content = source?.title ?? 'Link unavailable'
          if (source) {
            token.attrSet('href', source.path)
            tokens.splice(index + 1, end - index - 1, label)
            index += 2
          } else {
            tokens.splice(index, end - index + 1, label)
          }
        } else if (token.type === 'text' || token.type === 'code_inline') {
          token.content = formatAssistantDates(hideAddresses(token.content), timeZone)
        }
      }
    }
  })
  markdown.renderer.rules.image = (tokens, index) => markdown.utils.escapeHtml(hideAddresses(tokens[index]?.content ?? ''))
  markdown.renderer.rules.table_open = () => '<div class="assistant-table" role="region" aria-label="Table" tabindex="0"><table>\n'
  markdown.renderer.rules.table_close = () => '</table></div>\n'
  return markdown.render(text)
}
