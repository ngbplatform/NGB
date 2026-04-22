import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'

const mocks = vi.hoisted(() => ({
  readCookie: vi.fn(),
  readStorageString: vi.fn(),
  writeCookie: vi.fn(),
  writeStorageString: vi.fn(() => true),
}))

vi.mock('../../../../src/ngb/utils/storage', () => ({
  readCookie: mocks.readCookie,
  readStorageString: mocks.readStorageString,
  writeCookie: mocks.writeCookie,
  writeStorageString: mocks.writeStorageString,
}))

import { useTheme } from '../../../../src/ngb/site/useTheme'

function createMatchMedia(matches: boolean) {
  return vi.fn().mockImplementation((query: string) => ({
    matches,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }))
}

const ThemeHarness = defineComponent({
  setup() {
    const theme = useTheme()

    return () => h('div', [
      h('div', { 'data-testid': 'theme-mode' }, theme.mode.value),
      h('div', { 'data-testid': 'theme-resolved' }, theme.resolved.value),
      h('button', { type: 'button', onClick: theme.toggle }, 'Toggle theme'),
    ])
  },
})

beforeEach(() => {
  vi.clearAllMocks()
  document.documentElement.classList.remove('dark')
  mocks.readCookie.mockReturnValue(null)
  mocks.readStorageString.mockReturnValue(null)
  window.matchMedia = createMatchMedia(false) as typeof window.matchMedia
})

afterEach(() => {
  document.documentElement.classList.remove('dark')
})

test('prefers saved local storage over cookies and applies the dark theme class', async () => {
  mocks.readStorageString.mockReturnValue('dark')
  mocks.readCookie.mockReturnValue('light')

  const view = await render(ThemeHarness)

  await expect.element(view.getByTestId('theme-mode')).toHaveTextContent('dark')
  await expect.element(view.getByTestId('theme-resolved')).toHaveTextContent('dark')
  expect(document.documentElement.classList.contains('dark')).toBe(true)
  expect(mocks.writeStorageString).toHaveBeenLastCalledWith('local', 'ngb.theme', 'dark')
  expect(mocks.writeCookie).toHaveBeenLastCalledWith('ngb.theme', 'dark', expect.objectContaining({
    path: '/',
    sameSite: 'Lax',
  }))
})

test('falls back to cookie-backed system mode and toggles to light explicitly', async () => {
  mocks.readCookie.mockReturnValue('system')
  window.matchMedia = createMatchMedia(true) as typeof window.matchMedia

  const view = await render(ThemeHarness)

  await expect.element(view.getByTestId('theme-mode')).toHaveTextContent('system')
  await expect.element(view.getByTestId('theme-resolved')).toHaveTextContent('dark')
  expect(document.documentElement.classList.contains('dark')).toBe(true)

  await view.getByRole('button', { name: 'Toggle theme' }).click()

  await expect.element(view.getByTestId('theme-mode')).toHaveTextContent('light')
  await expect.element(view.getByTestId('theme-resolved')).toHaveTextContent('light')
  expect(document.documentElement.classList.contains('dark')).toBe(false)
  expect(mocks.writeStorageString).toHaveBeenLastCalledWith('local', 'ngb.theme', 'light')
  expect(mocks.writeCookie).toHaveBeenLastCalledWith('ngb.theme', 'light', expect.objectContaining({
    path: '/',
    sameSite: 'Lax',
  }))
})
