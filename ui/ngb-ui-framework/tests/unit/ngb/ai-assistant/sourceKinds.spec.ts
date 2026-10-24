import { readFileSync } from 'node:fs'
import { expect, it } from 'vitest'
import { NgbSourceKinds, isNgbSourceKind } from '../../../../src/ngb/api/sourceKinds'

it('matches the source kinds in the versioned MCP input and output schemas', () => {
  const contract = JSON.parse(readFileSync(new URL('../../../../../../quality/mcp/tool-contract-3.3.0.json', import.meta.url), 'utf8'))
  const record = contract.find((tool: { name: string }) => tool.name === 'ngb_get_record')
  const search = contract.find((tool: { name: string }) => tool.name === 'ngb_search')
  const codes = Object.values(NgbSourceKinds)

  expect(codes).toEqual(record.inputSchema.properties.kind.enum)
  expect(codes).toEqual(record.outputSchema.properties.sources.items.properties.kind.enum)
  expect(codes).toEqual(search.inputSchema.properties.kind.enum.filter((kind: unknown) => kind !== null))
  expect(codes.every(isNgbSourceKind)).toBe(true)
})

it.each([
  null,
  undefined,
  0,
  '0',
  'Catalog',
  'DOCUMENT',
  'report ',
  'catalogItem',
  'register',
])('rejects noncanonical source kind %s', value => {
  expect(isNgbSourceKind(value)).toBe(false)
})
