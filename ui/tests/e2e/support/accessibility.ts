import AxeBuilder from '@axe-core/playwright'
import { expect, type Page } from '@playwright/test'

export async function expectNoAccessibilityViolations(
  page: Page,
  rootSelector: string,
): Promise<void> {
  const results = await new AxeBuilder({ page })
    .include(rootSelector)
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze()

  expect(
    results.violations.map((violation) => ({
      id: violation.id,
      impact: violation.impact,
      description: violation.description,
      nodes: violation.nodes.map((node) => node.target),
    })),
  ).toEqual([])
}
