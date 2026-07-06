import type { EditorEntityProfile, EntityEditorContext, EntityFormModel } from 'ngb-ui-framework'
import { asTrimmedString } from 'ngb-ui-framework'

const dateOnlyRe = /^\d{4}-\d{2}-\d{2}$/

const CRM_DOCUMENT_DISPLAY_CONFIG: Record<string, { title: string; dateField: string }> = {
  'crm.lead_intake': { title: 'Lead Intake', dateField: 'document_date_utc' },
  'crm.lead_qualification': { title: 'Lead Qualification', dateField: 'document_date_utc' },
  'crm.lead_conversion': { title: 'Lead Conversion', dateField: 'document_date_utc' },
  'crm.opportunity_update': { title: 'Opportunity Update', dateField: 'document_date_utc' },
  'crm.quote': { title: 'Quote', dateField: 'document_date_utc' },
  'crm.activity_log': { title: 'Activity Log', dateField: 'document_date_utc' },
}

function formatDateOnlyMdYyyy(value: unknown): string | null {
  if (typeof value !== 'string' || !dateOnlyRe.test(value)) return null
  const [yyyyRaw, mmRaw, ddRaw] = value.split('-')
  const yyyy = Number(yyyyRaw)
  const mm = Number(mmRaw)
  const dd = Number(ddRaw)
  if (!Number.isFinite(yyyy) || !Number.isFinite(mm) || !Number.isFinite(dd)) return null
  return `${mm}/${dd}/${yyyy}`
}

function computeContactDisplay(model: EntityFormModel): string | null {
  const first = asTrimmedString(model.first_name)
  const last = asTrimmedString(model.last_name)
  return [first, last].filter(Boolean).join(' ') || null
}

function computeProductDisplay(model: EntityFormModel): string | null {
  const sku = asTrimmedString(model.sku)
  const name = asTrimmedString(model.name)
  return name || sku || null
}

function computeStageDisplay(model: EntityFormModel): string | null {
  const ordinal = asTrimmedString(model.ordinal)
  const name = asTrimmedString(model.name)
  return name || ordinal || null
}

function computeCRMDocumentDisplay(typeCode: string, model: EntityFormModel): string | null {
  const config = CRM_DOCUMENT_DISPLAY_CONFIG[typeCode]
  if (!config) return null

  const number = asTrimmedString(model.number)
  const date = formatDateOnlyMdYyyy(model[config.dateField])
  return [config.title, number, date]
    .filter((part): part is string => typeof part === 'string' && part.trim().length > 0)
    .join(' ') || null
}

export function resolveCRMEditorEntityProfile(context: EntityEditorContext): EditorEntityProfile | null {
  if (context.kind === 'catalog' && context.typeCode === 'crm.account') {
    return {
      computedDisplayWatchFields: ['name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = asTrimmedString(model.name) || null
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'crm.contact') {
    return {
      computedDisplayWatchFields: ['first_name', 'last_name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = computeContactDisplay(model)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'crm.product') {
    return {
      computedDisplayWatchFields: ['sku', 'name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = computeProductDisplay(model)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'crm.opportunity_stage') {
    return {
      computedDisplayWatchFields: ['ordinal', 'name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = computeStageDisplay(model)
      },
    }
  }

  if (context.kind === 'document' && CRM_DOCUMENT_DISPLAY_CONFIG[context.typeCode]) {
    const { dateField } = CRM_DOCUMENT_DISPLAY_CONFIG[context.typeCode]
    return {
      computedDisplayWatchFields: ['number', dateField],
      computedDisplayMode: 'new_or_draft',
      syncComputedDisplay: ({ model }) => {
        model.display = computeCRMDocumentDisplay(context.typeCode, model)
      },
    }
  }

  return null
}
