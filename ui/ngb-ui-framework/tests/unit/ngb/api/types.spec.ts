import { describe, expect, it } from 'vitest'

import type { JsonObject, QueryParams } from '../../../../src/ngb/api/types'

describe('api primitive types', () => {
  it('supports serializable query params and json payload shapes', () => {
    const params: QueryParams = {
      offset: 10,
      includeDeleted: false,
      search: 'invoice',
      skipped: null,
    }
    const payload: JsonObject = {
      filters: {
        status: 'open',
        priority: 1,
        active: true,
        nested: null,
      },
      items: ['a', 1, false],
    }

    expect(params.offset).toBe(10)
    expect(payload.filters).toEqual({
      status: 'open',
      priority: 1,
      active: true,
      nested: null,
    })
  })
})
