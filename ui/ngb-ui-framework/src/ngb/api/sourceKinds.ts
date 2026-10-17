import { isNonEmptyGuid } from '../utils/guid'

/** Canonical source kind codes shared by MCP responses and Assistant navigation. */
export const NgbSourceKinds = Object.freeze({
  Catalog: 'catalog',
  Document: 'document',
  GeneralJournalEntry: 'generalJournalEntry',
  Report: 'report',
} as const)

export type NgbSourceKind = typeof NgbSourceKinds[keyof typeof NgbSourceKinds]

export type NgbSourceIdentity = {
  kind: NgbSourceKind
  typeCode: string
  id?: string | null
}

const typeCodePattern = /^[a-zA-Z0-9_.-]{1,128}$/
const supportedKinds = new Set<string>(Object.values(NgbSourceKinds))

export function isNgbSourceKind(value: unknown): value is NgbSourceKind {
  return typeof value === 'string' && supportedKinds.has(value)
}

export function isNgbSourceIdentity(value: unknown): value is NgbSourceIdentity {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false

  const source = value as Partial<NgbSourceIdentity>
  return isNgbSourceKind(source.kind)
    && typeof source.typeCode === 'string'
    && typeCodePattern.test(source.typeCode)
    && (source.id == null || (isNonEmptyGuid(source.id) && source.id === source.id.trim()))
}
