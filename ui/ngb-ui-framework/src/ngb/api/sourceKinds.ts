/** Canonical source kind codes shared by MCP responses and Assistant navigation. */
export const NgbSourceKinds = Object.freeze({
  Catalog: 'catalog',
  Document: 'document',
  GeneralJournalEntry: 'generalJournalEntry',
  Report: 'report',
} as const)

export type NgbSourceKind = typeof NgbSourceKinds[keyof typeof NgbSourceKinds]

const supportedKinds = new Set<string>(Object.values(NgbSourceKinds))

export function isNgbSourceKind(value: unknown): value is NgbSourceKind {
  return typeof value === 'string' && supportedKinds.has(value)
}
