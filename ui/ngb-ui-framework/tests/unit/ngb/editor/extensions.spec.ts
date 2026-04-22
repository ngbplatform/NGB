import { describe, expect, it, vi } from 'vitest'

import { runEntityEditorAction } from '../../../../src/ngb/editor/extensions'

describe('entity editor action runner', () => {
  it('executes known handlers and reports whether an action was handled', () => {
    const save = vi.fn()

    expect(runEntityEditorAction('save', { save })).toBe(true)
    expect(save).toHaveBeenCalledTimes(1)
    expect(runEntityEditorAction('missing', { save })).toBe(false)
  })
})
