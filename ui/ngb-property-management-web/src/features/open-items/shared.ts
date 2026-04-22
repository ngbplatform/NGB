export type OpenItemsLookupItem = {
  id: string
  label: string
  meta?: string
}

export type ApplyWizardView = 'suggest' | 'result'

export type OpenItemsApplyResultLine = {
  key: string
  applyId: string
  creditDocumentId: string
  creditDocumentType: string
  creditLabel: string
  chargeDocumentId: string
  chargeLabel: string
  appliedOnUtc: string
  amount: number
}

export function fmtMoney(value: number): string {
  const normalized = Math.round((value ?? 0) * 100) / 100
  return normalized.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

export function fmtDateOnly(value: string | null | undefined): string {
  const normalized = String(value ?? '').trim()
  if (!normalized) return '—'

  const date = new Date(`${normalized}T00:00:00Z`)
  if (Number.isNaN(date.getTime())) return normalized
  return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: '2-digit' })
}

export function docLabel(number?: string | null, display?: string | null, fallback?: string | null): string {
  const normalizedNumber = String(number ?? '').trim()
  if (normalizedNumber) return normalizedNumber

  const normalizedDisplay = String(display ?? '').trim()
  if (normalizedDisplay) return normalizedDisplay

  return String(fallback ?? '').trim() || '—'
}

export function applyDocumentLabel(applyNumber?: string | null, applyDisplay?: string | null, applyId?: string | null): string {
  return docLabel(applyNumber, applyDisplay, applyId)
}

export function formatApplyCount(count: number): string {
  return `${count} ${count === 1 ? 'apply' : 'applies'}`
}
