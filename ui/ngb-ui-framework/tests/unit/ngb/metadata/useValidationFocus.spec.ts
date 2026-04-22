import { ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { useValidationFocus } from '../../../../src/ngb/metadata/useValidationFocus'

function createFocusable() {
  return {
    focus: vi.fn(),
  }
}

function createContainer(attribute: string, value: string, target: { focus: ReturnType<typeof vi.fn> } | null = null) {
  return {
    getAttribute: (key: string) => key === attribute ? value : null,
    querySelector: vi.fn(() => target),
    scrollIntoView: vi.fn(),
  }
}

describe('metadata useValidationFocus', () => {
  it('finds containers by validation attribute and scrolls to them', () => {
    const amountContainer = createContainer('data-validation-key', 'amount')
    const root = {
      querySelectorAll: vi.fn(() => [amountContainer]),
    }

    const validation = useValidationFocus(ref(root as never), {
      attribute: 'data-validation-key',
    })

    expect(validation.containerFor('amount')).toBe(amountContainer)
    expect(validation.containerFor('missing')).toBeNull()
    expect(validation.scrollTo('amount')).toBe(true)
    expect(validation.scrollTo('missing')).toBe(false)
    expect(amountContainer.scrollIntoView).toHaveBeenCalledWith({ block: 'center', behavior: 'smooth' })
  })

  it('focuses the first focusable field inside the matching container', () => {
    const input = createFocusable()
    const amountContainer = createContainer('data-validation-key', 'amount', input)
    const root = {
      querySelectorAll: vi.fn(() => [amountContainer]),
    }

    const validation = useValidationFocus(ref(root as never), {
      attribute: 'data-validation-key',
    })

    expect(validation.focus('amount')).toBe(true)
    expect(amountContainer.scrollIntoView).toHaveBeenCalledWith({ block: 'center', behavior: 'smooth' })
    expect(input.focus).toHaveBeenCalledWith({ preventScroll: true })
    expect(validation.focus('missing')).toBe(false)
  })
})
