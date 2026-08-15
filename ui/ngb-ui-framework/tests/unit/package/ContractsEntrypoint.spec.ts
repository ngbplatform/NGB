import { describe, expect, it } from 'vitest'

import { ReportRowKind } from '@ngbplatform/ui/contracts'

describe('@ngbplatform/ui/contracts', () => {
  it('provides a runtime-safe contracts entrypoint for Node tooling', () => {
    expect(ReportRowKind.Detail).toBe(3)
  })
})
