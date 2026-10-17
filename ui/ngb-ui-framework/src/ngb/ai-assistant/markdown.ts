import MarkdownIt from 'markdown-it'

/** Model output cannot embed HTML, load images, or create links outside verified application sources. */
export function renderAssistantMarkdown(text: string, sourcePaths: string[]): string {
  const markdown = new MarkdownIt({ html: false, linkify: false, breaks: true })
  const allowed = new Set(sourcePaths.map(path => new URL(path, window.location.origin).href))
  markdown.validateLink = value => {
    try {
      const url = new URL(value, window.location.origin)
      return url.origin === window.location.origin && allowed.has(url.href)
    } catch {
      return false
    }
  }
  markdown.renderer.rules.image = (tokens, index) => markdown.utils.escapeHtml(tokens[index]?.content ?? '')
  return markdown.render(text)
}
