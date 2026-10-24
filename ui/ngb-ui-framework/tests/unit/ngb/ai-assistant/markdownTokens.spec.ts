import MarkdownIt from 'markdown-it'
import type Token from 'markdown-it/lib/token.mjs'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { renderAssistantMarkdown } from '../../../../src/ngb/ai-assistant/markdown'

const parser = vi.hoisted(() => ({
  instance: null as MarkdownIt | null,
  transform: null as ((tokens: Token[]) => void) | null,
}))

vi.mock('markdown-it', async importOriginal => {
  const { default: Markdown } = await importOriginal<typeof import('markdown-it')>()
  return {
    default: class extends Markdown {
      constructor(options: MarkdownIt.Options) {
        super(options)
        parser.instance = this
        this.core.ruler.before('linkify', 'test-token-transform', state => {
          for (const token of state.tokens) {
            if (token.children) parser.transform?.(token.children)
          }
        })
      }
    },
  }
})

beforeEach(() => {
  parser.transform = null
  parser.instance = null
  vi.stubGlobal('window', { location: { origin: 'https://app.example' } })
})

afterEach(() => vi.unstubAllGlobals())

it('does not throw when a parser extension produces an unclosed link', () => {
  parser.transform = tokens => {
    const close = tokens.findIndex(token => token.type === 'link_close')
    tokens.splice(close, 1)
  }

  expect(() => renderAssistantMarkdown('[label](/record)', [])).not.toThrow()
})

it('replaces a parser link without an address with unavailable text', () => {
  parser.transform = tokens => {
    const link = tokens.find(token => token.type === 'link_open')!
    link.attrs = null
  }

  const result = renderAssistantMarkdown('[label](/record)', [])
  expect(result).toContain('Link unavailable')
  expect(result).not.toContain('<a')
})

it('renders no image or text for a missing image token', () => {
  renderAssistantMarkdown('Text', [])
  const markdown = parser.instance!
  const result = markdown.renderer.rules.image!([], 0, markdown.options, {}, markdown.renderer)

  expect(result).toBe('')
})
