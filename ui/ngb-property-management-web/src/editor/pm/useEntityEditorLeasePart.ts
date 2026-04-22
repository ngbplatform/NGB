import { ref, type ComputedRef } from 'vue'
import { clonePlainData, type RecordPayload } from 'ngb-ui-framework'
import type { LeasePartyRow } from './leasePartyTypes'

type UseEntityEditorLeasePartArgs = {
  isLeaseDocument: ComputedRef<boolean>
}

function extractLeaseRows(parts: RecordPayload['parts'] | null | undefined): LeasePartyRow[] {
  const rows = parts?.parties?.rows
  return Array.isArray(rows) ? clonePlainData(rows) as LeasePartyRow[] : []
}

export function useEntityEditorLeasePart(args: UseEntityEditorLeasePartArgs) {
  const leasePartiesRows = ref<LeasePartyRow[]>([])

  function defaultLeasePartyRow(): LeasePartyRow {
    return {
      party_id: null,
      role: 'PrimaryTenant',
      is_primary: true,
      ordinal: 1,
    }
  }

  function normalizeLeasePartiesRowsInPlace() {
    for (let index = 0; index < leasePartiesRows.value.length; index++) {
      leasePartiesRows.value[index].ordinal = index + 1
    }
  }

  function ensureLeasePartiesInitialized() {
    if (!args.isLeaseDocument.value) {
      leasePartiesRows.value = []
      return
    }

    if (leasePartiesRows.value.length === 0) {
      leasePartiesRows.value = [defaultLeasePartyRow()]
    }

    normalizeLeasePartiesRowsInPlace()
  }

  function setLeaseRows(parts: RecordPayload['parts'] | null | undefined) {
    if (!args.isLeaseDocument.value) {
      leasePartiesRows.value = []
      return
    }

    const rows = extractLeaseRows(parts)
    leasePartiesRows.value = rows.length > 0 ? rows : [defaultLeasePartyRow()]
    ensureLeasePartiesInitialized()
  }

  function applyInitialParts(parts: RecordPayload['parts'] | null | undefined) {
    setLeaseRows(parts)
  }

  function applyPersistedParts(parts: RecordPayload['parts'] | null | undefined) {
    setLeaseRows(parts)
  }

  function validateLeasePartiesBeforeSave(): string | null {
    const rows = leasePartiesRows.value
    if (rows.length === 0) return 'At least one tenant is required.'

    let primaryCount = 0
    let primaryRoleOk = true

    for (let index = 0; index < rows.length; index++) {
      const row = rows[index]
      const hasParty = !!(
        row?.party_id
        && (
          (typeof row.party_id === 'string' && row.party_id)
          || (
            typeof row.party_id === 'object'
            && row.party_id
            && 'id' in row.party_id
            && typeof row.party_id.id === 'string'
            && row.party_id.id
          )
        )
      )

      if (!hasParty) return `Tenant row #${index + 1}: Party is required.`

      if (row?.is_primary) {
        primaryCount++
        if (String(row?.role ?? '') !== 'PrimaryTenant') primaryRoleOk = false
      }
    }

    if (primaryCount !== 1) return 'Exactly one tenant must be Primary.'
    if (!primaryRoleOk) return "Primary tenant row must have role 'PrimaryTenant'."
    return null
  }

  function buildSaveParts(): RecordPayload['parts'] | undefined {
    if (!args.isLeaseDocument.value) return undefined
    ensureLeasePartiesInitialized()
    return {
      parties: {
        rows: leasePartiesRows.value as unknown as NonNullable<RecordPayload['parts']>[string]['rows'],
      },
    }
  }

  function buildCopyParts(): RecordPayload['parts'] | null {
    if (!args.isLeaseDocument.value) return null

    return {
      parties: {
        rows: clonePlainData(leasePartiesRows.value) as unknown as NonNullable<RecordPayload['parts']>[string]['rows'],
      },
    }
  }

  return {
    isLeaseDocument: args.isLeaseDocument,
    leasePartiesRows,
    defaultLeasePartyRow,
    ensureLeasePartiesInitialized,
    applyInitialParts,
    applyPersistedParts,
    validateLeasePartiesBeforeSave,
    buildSaveParts,
    buildCopyParts,
  }
}
