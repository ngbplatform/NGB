import { expect, type Locator, type Page } from '@playwright/test'

export async function expectNoHorizontalPageOverflow(page: Page): Promise<void> {
  const metrics = await page.evaluate(() => ({
    innerWidth: window.innerWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }))

  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.innerWidth + 1)
}

export async function expectCardsToStackVertically(cards: Locator): Promise<void> {
  const count = await cards.count()
  expect(count).toBeGreaterThan(1)

  const first = await cards.nth(0).boundingBox()
  const second = await cards.nth(1).boundingBox()

  expect(first).not.toBeNull()
  expect(second).not.toBeNull()

  expect(second!.y).toBeGreaterThan(first!.y)
}
