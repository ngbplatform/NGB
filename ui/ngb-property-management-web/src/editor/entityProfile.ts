import type { EditorEntityProfile, EntityEditorContext, EntityFormModel } from 'ngb-ui-framework'
import { asTrimmedString, tryExtractReferenceDisplay } from 'ngb-ui-framework'

export const PM_EDITOR_TAGS = {
  PROPERTY_CATALOG: 'pm:property-catalog',
  BANK_ACCOUNT_CATALOG: 'pm:bank-account-catalog',
  LEASE_DOCUMENT: 'pm:lease-document',
} as const

const dateOnlyRe = /^\d{4}-\d{2}-\d{2}$/

function formatDateOnlyMmDdYyyy(value: unknown): string | null {
  if (typeof value !== 'string' || !dateOnlyRe.test(value)) return null

  const [yyyyRaw, mmRaw, ddRaw] = value.split('-')
  const yyyy = Number(yyyyRaw)
  const mm = Number(mmRaw)
  const dd = Number(ddRaw)
  if (!Number.isFinite(yyyy) || !Number.isFinite(mm) || !Number.isFinite(dd)) return null
  if (mm < 1 || mm > 12 || dd < 1 || dd > 31) return null

  return `${String(mm).padStart(2, '0')}/${String(dd).padStart(2, '0')}/${yyyy}`
}

function computePmPropertyDisplay(model: EntityFormModel): string | null {
  const kind = asTrimmedString(model.kind)

  if (kind === 'Building') {
    const line1 = asTrimmedString(model.address_line1)
    if (!line1) return null

    const line2 = asTrimmedString(model.address_line2)
    const city = asTrimmedString(model.city)
    const state = asTrimmedString(model.state)
    const zip = asTrimmedString(model.zip)

    const first = line2 ? `${line1} ${line2}` : line1
    const parts: string[] = [first]
    if (city) parts.push(city)
    if (state && zip) parts.push(`${state} ${zip}`)
    else if (state) parts.push(state)
    else if (zip) parts.push(zip)

    return parts.join(', ')
  }

  if (kind === 'Unit') {
    const parentDisplay = tryExtractReferenceDisplay(model.parent_property_id)
    const unitNo = asTrimmedString(model.unit_no)
    if (!parentDisplay || !unitNo) return null
    return `${parentDisplay} #${unitNo}`
  }

  return null
}

function computePmBankAccountDisplay(model: EntityFormModel): string | null {
  const bankName = asTrimmedString(model.bank_name)
  const accountName = asTrimmedString(model.account_name)
  const last4 = asTrimmedString(model.last4)
  if (!bankName || !accountName || !last4) return null
  return `${bankName} ${accountName} **** ${last4}`
}

function computeLeaseDisplay(model: EntityFormModel): string | null {
  const propertyDisplay = tryExtractReferenceDisplay(model.property_id)
  const start = formatDateOnlyMmDdYyyy(model.start_on_utc)
  if (!propertyDisplay || !start) return null

  const end = model.end_on_utc ? formatDateOnlyMmDdYyyy(model.end_on_utc) : null
  return `${propertyDisplay} — ${start} → ${end ?? 'Open'}`
}

function sanitizePmPropertyModel(model: EntityFormModel): void {
  const kind = asTrimmedString(model.kind)
  if (kind === 'Building') {
    model.parent_property_id = null
    model.unit_no = null
    return
  }

  if (kind === 'Unit') {
    model.address_line1 = null
    model.address_line2 = null
    model.city = null
    model.state = null
    model.zip = null
  }
}

export function resolvePmEditorEntityProfile(context: EntityEditorContext): EditorEntityProfile | null {
  if (context.kind === 'catalog' && context.typeCode === 'pm.property') {
    return {
      tags: [PM_EDITOR_TAGS.PROPERTY_CATALOG],
      sanitizeWatchFields: ['kind'],
      computedDisplayWatchFields: [
        'kind',
        'address_line1',
        'address_line2',
        'city',
        'state',
        'zip',
        'parent_property_id',
        'unit_no',
      ],
      computedDisplayMode: 'always',
      sanitizeModelForEditing: ({ model }) => {
        sanitizePmPropertyModel(model)
      },
      syncComputedDisplay: ({ model }) => {
        const display = computePmPropertyDisplay(model)
        if (display) model.display = display
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'pm.bank_account') {
    return {
      tags: [PM_EDITOR_TAGS.BANK_ACCOUNT_CATALOG],
      computedDisplayWatchFields: ['bank_name', 'account_name', 'last4'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        const display = computePmBankAccountDisplay(model)
        if (display) model.display = display
      },
    }
  }

  if (context.kind === 'document' && context.typeCode === 'pm.lease') {
    return {
      tags: [PM_EDITOR_TAGS.LEASE_DOCUMENT],
      computedDisplayWatchFields: ['property_id', 'start_on_utc', 'end_on_utc'],
      computedDisplayMode: 'new_or_draft',
      syncComputedDisplay: ({ model }) => {
        const display = computeLeaseDisplay(model)
        if (display) model.display = display
      },
    }
  }
  return null
}
