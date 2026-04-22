import { computed, nextTick, ref, type ComputedRef, type Ref } from 'vue'

import {
  dedupeEntityEditorMessages,
  humanizeEntityEditorFieldKey,
  isEntityEditorFormIssuePath,
  normalizeEntityEditorError,
  type EditorErrorIssue,
  type EditorErrorState,
} from 'ngb-ui-framework'

export type LeaseTenantFieldKey = 'party_id' | 'role' | 'is_primary' | 'ordinal'
type LeaseTenantRowErrors = Partial<Record<LeaseTenantFieldKey, string[]>>

export type LeaseTenantValidation = {
  summary: string[]
  rowErrors: Record<number, LeaseTenantRowErrors>
  focusTarget?: { rowIndex: number; field: LeaseTenantFieldKey } | null
}

type FormHandle = {
  focusField?: (path: string) => boolean
  focusFirstError?: (keys: string[]) => boolean
}

type LeaseGridHandle = {
  focusFirstError?: (validation: LeaseTenantValidation | null) => boolean
}

type LeaseTenantRow = {
  party_id?: unknown
  role?: unknown
  is_primary?: unknown
  ordinal?: unknown
}

type UseEntityEditorErrorStateArgs = {
  fieldLabels: ComputedRef<Record<string, string>>
  isLeaseDocument: ComputedRef<boolean>
  leasePartiesRows: Ref<LeaseTenantRow[]>
  loading: Ref<boolean>
  saving: Ref<boolean>
  formRef: Ref<FormHandle | null>
  leaseGridRef: Ref<LeaseGridHandle | null>
}

function dedupeMessages(messages: string[]): string[] {
  return dedupeEntityEditorMessages(messages)
}

function humanizeFieldKey(key: string): string {
  return humanizeEntityEditorFieldKey(key)
}

function leasePartyFieldLabel(key: string): string {
  switch (key) {
    case 'party_id':
      return 'Party'
    case 'role':
      return 'Role'
    case 'is_primary':
      return 'Primary'
    case 'ordinal':
      return 'Line No'
    default:
      return humanizeFieldKey(key)
  }
}

function isFormIssuePath(path: string): boolean {
  return isEntityEditorFormIssuePath(path)
}

function isLeasePartIssuePath(path: string): boolean {
  return path === 'parties' || /^parties(?:\[\d+\]|\.\d+|\[\])\./.test(path)
}

function pushTenantRowMessages(target: Record<number, LeaseTenantRowErrors>, rowIndex: number, field: LeaseTenantFieldKey, messages: string[]) {
  if (!Number.isFinite(rowIndex) || rowIndex < 0) return
  const entry = (target[rowIndex] ??= {})
  const current = entry[field] ?? []
  entry[field] = dedupeMessages([...current, ...messages])
}

function rowPartyId(row: LeaseTenantRow): string | null {
  if (!row?.party_id) return null
  if (typeof row.party_id === 'string') return row.party_id.trim() || null
  if (
    typeof row.party_id === 'object'
    && row.party_id
    && 'id' in row.party_id
    && typeof row.party_id.id === 'string'
  ) return row.party_id.id.trim() || null
  return null
}

function exactLeaseRowIssue(issuePath: string): { rowIndex: number; field: LeaseTenantFieldKey } | null {
  const match = issuePath.match(/^parties(?:\[(\d+)\]|\.(\d+))\.(party_id|role|is_primary|ordinal)$/)
  if (!match) return null

  const indexRaw = match[1] ?? match[2]
  const rowIndex = Number(indexRaw)
  const field = match[3] as LeaseTenantFieldKey
  if (!Number.isFinite(rowIndex)) return null

  return { rowIndex, field }
}

function wildcardLeaseField(issuePath: string): LeaseTenantFieldKey | null {
  const match = issuePath.match(/^parties\[\]\.(party_id|role|is_primary|ordinal)$/)
  return (match?.[1] as LeaseTenantFieldKey | undefined) ?? null
}

export function useEntityEditorErrorState(args: UseEntityEditorErrorStateArgs) {
  const error = ref<EditorErrorState | null>(null)
  const dismissedIssueKeys = ref<Record<string, true>>({})

  function resolveIssueLabel(path: string): string {
    const raw = String(path ?? '').trim()
    if (!raw || raw === '_form') return 'Validation'

    if (raw === 'parties') return 'Tenants'

    const leaseRowMatch = raw.match(/^parties(?:\[(\d+)\]|\.(\d+))\.(.+)$/)
    if (leaseRowMatch) {
      const indexRaw = leaseRowMatch[1] ?? leaseRowMatch[2]
      const subKey = leaseRowMatch[3] ?? ''
      const index = Number(indexRaw)
      const ordinal = Number.isFinite(index) ? index + 1 : null
      const rowLabel = ordinal ? `Tenant #${ordinal}` : 'Tenant'
      return `${rowLabel} / ${leasePartyFieldLabel(subKey)}`
    }

    return args.fieldLabels.value[raw] ?? humanizeFieldKey(raw)
  }

  function normalizeEditorError(cause: unknown): EditorErrorState {
    return normalizeEntityEditorError(cause, { resolveIssueLabel })
  }

  function issueDismissKey(issue: Pick<EditorErrorIssue, 'path' | 'scope'>): string {
    return `${issue.scope}:${issue.path}`
  }

  function focusFirstValidationError() {
    const current = displayedError.value
    if (!current) return

    for (const issue of current.issues) {
      if (isLeasePartIssuePath(issue.path)) {
        if (args.leaseGridRef.value?.focusFirstError?.(leaseTenantValidation.value)) return
        continue
      }

      if (isFormIssuePath(issue.path)) continue
      if (args.formRef.value?.focusField?.(issue.path)) return
    }

    if (args.leaseGridRef.value?.focusFirstError?.(leaseTenantValidation.value)) return
    void args.formRef.value?.focusFirstError?.(Object.keys(inlineFieldErrors.value))
  }

  function resetDismissedIssues() {
    dismissedIssueKeys.value = {}
  }

  function setEditorError(value: EditorErrorState | null) {
    error.value = value
    resetDismissedIssues()
    if (value) {
      void nextTick(() => {
        focusFirstValidationError()
      })
    }
  }

  const visibleIssues = computed<EditorErrorIssue[]>(() => {
    const dismissed = dismissedIssueKeys.value
    return (error.value?.issues ?? []).filter((issue) => !dismissed[issueDismissKey(issue)])
  })

  const displayedError = computed<EditorErrorState | null>(() => {
    const current = error.value
    if (!current) return null
    if (current.issues.length > 0 && visibleIssues.value.length === 0) return null
    return {
      ...current,
      issues: visibleIssues.value,
    }
  })

  const inlineFieldErrors = computed<Record<string, string>>(() => {
    const result: Record<string, string> = {}
    for (const issue of displayedError.value?.issues ?? []) {
      if (isLeasePartIssuePath(issue.path)) continue
      if (isFormIssuePath(issue.path)) continue
      if (issue.scope !== 'field') continue
      if (!(issue.path in args.fieldLabels.value)) continue
      if (result[issue.path]) continue
      const first = issue.messages.find((message) => message.trim().length > 0)
      if (first) result[issue.path] = first
    }
    return result
  })

  function duplicateTenantRowIndexes(): number[] {
    const byId = new Map<string, number[]>()
    for (let index = 0; index < args.leasePartiesRows.value.length; index++) {
      const id = rowPartyId(args.leasePartiesRows.value[index] ?? {})
      if (!id) continue
      const bucket = byId.get(id) ?? []
      bucket.push(index)
      byId.set(id, bucket)
    }
    return Array.from(byId.values())
      .filter((rows) => rows.length > 1)
      .flat()
      .sort((left, right) => left - right)
  }

  function duplicateOrdinalRowIndexes(): number[] {
    const byOrdinal = new Map<number, number[]>()
    for (let index = 0; index < args.leasePartiesRows.value.length; index++) {
      const ordinal = Number(args.leasePartiesRows.value[index]?.ordinal)
      if (!Number.isFinite(ordinal)) continue
      const bucket = byOrdinal.get(ordinal) ?? []
      bucket.push(index)
      byOrdinal.set(ordinal, bucket)
    }
    return Array.from(byOrdinal.values())
      .filter((rows) => rows.length > 1)
      .flat()
      .sort((left, right) => left - right)
  }

  function roleMismatchRowIndexes(): number[] {
    const result: number[] = []
    for (let index = 0; index < args.leasePartiesRows.value.length; index++) {
      const row = args.leasePartiesRows.value[index]
      if (row?.is_primary && String(row?.role ?? '') !== 'PrimaryTenant') result.push(index)
    }
    return result
  }

  function primaryFlagMismatchRowIndexes(): number[] {
    const result: number[] = []
    for (let index = 0; index < args.leasePartiesRows.value.length; index++) {
      const row = args.leasePartiesRows.value[index]
      if (String(row?.role ?? '') === 'PrimaryTenant' && !row?.is_primary) result.push(index)
    }
    return result
  }

  const leaseTenantValidation = computed<LeaseTenantValidation | null>(() => {
    if (!args.isLeaseDocument.value) return null

    const summary: string[] = []
    const rowErrors: Record<number, LeaseTenantRowErrors> = {}
    let focusTarget: { rowIndex: number; field: LeaseTenantFieldKey } | null = null

    for (const issue of displayedError.value?.issues ?? []) {
      if (!isLeasePartIssuePath(issue.path)) continue

      const exact = exactLeaseRowIssue(issue.path)
      if (exact) {
        pushTenantRowMessages(rowErrors, exact.rowIndex, exact.field, issue.messages)
        focusTarget ??= exact
        continue
      }

      if (issue.path === 'parties') {
        summary.push(...issue.messages)

        const primaryRows = args.leasePartiesRows.value
          .map((row, index) => ({ row, index }))
          .filter((entry) => !!entry.row?.is_primary)
          .map((entry) => entry.index)

        if (primaryRows.length > 1) {
          for (const rowIndex of primaryRows) pushTenantRowMessages(rowErrors, rowIndex, 'is_primary', issue.messages)
          focusTarget ??= { rowIndex: primaryRows[0], field: 'is_primary' }
        } else if (primaryRows.length === 0 && args.leasePartiesRows.value.length > 0) {
          pushTenantRowMessages(rowErrors, 0, 'is_primary', issue.messages)
          focusTarget ??= { rowIndex: 0, field: 'is_primary' }
        }
        continue
      }

      const wildcardField = wildcardLeaseField(issue.path)
      if (!wildcardField) {
        summary.push(...issue.messages)
        continue
      }

      if (wildcardField === 'party_id') {
        const duplicates = duplicateTenantRowIndexes()
        if (duplicates.length > 0) {
          for (const rowIndex of duplicates) pushTenantRowMessages(rowErrors, rowIndex, 'party_id', issue.messages)
          focusTarget ??= { rowIndex: duplicates[0], field: 'party_id' }
          continue
        }
      }

      if (wildcardField === 'ordinal') {
        const duplicates = duplicateOrdinalRowIndexes()
        if (duplicates.length > 0) {
          for (const rowIndex of duplicates) pushTenantRowMessages(rowErrors, rowIndex, 'ordinal', issue.messages)
          focusTarget ??= { rowIndex: duplicates[0], field: 'ordinal' }
          continue
        }
      }

      if (wildcardField === 'role') {
        const mismatches = roleMismatchRowIndexes()
        if (mismatches.length > 0) {
          for (const rowIndex of mismatches) pushTenantRowMessages(rowErrors, rowIndex, 'role', issue.messages)
          focusTarget ??= { rowIndex: mismatches[0], field: 'role' }
          continue
        }
      }

      if (wildcardField === 'is_primary') {
        const mismatches = primaryFlagMismatchRowIndexes()
        if (mismatches.length > 0) {
          for (const rowIndex of mismatches) pushTenantRowMessages(rowErrors, rowIndex, 'is_primary', issue.messages)
          focusTarget ??= { rowIndex: mismatches[0], field: 'is_primary' }
          continue
        }
      }

      summary.push(...issue.messages)
    }

    const normalizedSummary = dedupeMessages(summary)
    const hasRowErrors = Object.keys(rowErrors).length > 0
    if (!normalizedSummary.length && !hasRowErrors) return null

    return {
      summary: normalizedSummary,
      rowErrors,
      focusTarget,
    }
  })

  const bannerIssues = computed<EditorErrorIssue[]>(() => {
    return (displayedError.value?.issues ?? []).filter((issue) => {
      if (isLeasePartIssuePath(issue.path)) return false
      if (isFormIssuePath(issue.path)) return true
      if (issue.scope === 'field' && issue.path in inlineFieldErrors.value) return false
      return true
    })
  })

  function dismissIssues(predicate: (issue: EditorErrorIssue) => boolean) {
    if (!error.value) return
    const next = { ...dismissedIssueKeys.value }
    let changed = false
    for (const issue of error.value.issues) {
      if (!predicate(issue)) continue
      const dismissKey = issueDismissKey(issue)
      if (next[dismissKey]) continue
      next[dismissKey] = true
      changed = true
    }
    if (changed) dismissedIssueKeys.value = next
  }

  function dismissFieldIssues(fieldKey: string) {
    dismissIssues((issue) => issue.path === fieldKey)
  }

  function dismissLeaseIssues() {
    dismissIssues((issue) => isLeasePartIssuePath(issue.path))
  }

  return {
    error,
    displayedError,
    inlineFieldErrors,
    leaseTenantValidation,
    bannerIssues,
    normalizeEditorError,
    setEditorError,
    dismissFieldIssues,
    dismissLeaseIssues,
    focusFirstValidationError,
  }
}
