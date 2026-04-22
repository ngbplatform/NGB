import type { EditorEntityProfile, EntityEditorContext, EntityFormModel } from 'ngb-ui-framework'
import { asTrimmedString } from 'ngb-ui-framework'

const dateOnlyRe = /^\d{4}-\d{2}-\d{2}$/

const TRADE_DOCUMENT_DISPLAY_CONFIG: Record<string, { title: string; dateField: string }> = {
  'trd.purchase_receipt': { title: 'Purchase Receipt', dateField: 'document_date_utc' },
  'trd.sales_invoice': { title: 'Sales Invoice', dateField: 'document_date_utc' },
  'trd.customer_payment': { title: 'Customer Payment', dateField: 'document_date_utc' },
  'trd.vendor_payment': { title: 'Vendor Payment', dateField: 'document_date_utc' },
  'trd.inventory_transfer': { title: 'Inventory Transfer', dateField: 'document_date_utc' },
  'trd.inventory_adjustment': { title: 'Inventory Adjustment', dateField: 'document_date_utc' },
  'trd.customer_return': { title: 'Customer Return', dateField: 'document_date_utc' },
  'trd.vendor_return': { title: 'Vendor Return', dateField: 'document_date_utc' },
  'trd.item_price_update': { title: 'Item Price Update', dateField: 'effective_date' },
}

function formatDateOnlyMdYyyy(value: unknown): string | null {
  if (typeof value !== 'string' || !dateOnlyRe.test(value)) return null

  const [yyyyRaw, mmRaw, ddRaw] = value.split('-')
  const yyyy = Number(yyyyRaw)
  const mm = Number(mmRaw)
  const dd = Number(ddRaw)
  if (!Number.isFinite(yyyy) || !Number.isFinite(mm) || !Number.isFinite(dd)) return null
  if (mm < 1 || mm > 12 || dd < 1 || dd > 31) return null

  return `${mm}/${dd}/${yyyy}`
}

function syncNameFromDisplay(model: EntityFormModel): void {
  const display = asTrimmedString(model.display)
  if (!display) return
  model.name = display
}

function computeWarehouseDisplay(model: EntityFormModel): string | null {
  const name = asTrimmedString(model.name)
  const address = asTrimmedString(model.address)

  if (name && address) return `${name} — ${address}`
  if (name) return name
  if (address) return address
  return null
}

function computeTradeDocumentDisplay(typeCode: string, model: EntityFormModel): string | null {
  const config = TRADE_DOCUMENT_DISPLAY_CONFIG[typeCode]
  if (!config) return null

  const number = asTrimmedString(model.number)
  const date = formatDateOnlyMdYyyy(model[config.dateField])
  if (!number && !date) return null

  return [config.title, number, date]
    .filter((part): part is string => typeof part === 'string' && part.trim().length > 0)
    .join(' ')
}

export function resolveTradeEditorEntityProfile(context: EntityEditorContext): EditorEntityProfile | null {
  if (context.kind === 'catalog' && context.typeCode === 'trd.item') {
    return {
      computedDisplayWatchFields: ['display'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        syncNameFromDisplay(model)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'trd.unit_of_measure') {
    return {
      computedDisplayWatchFields: ['display'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        syncNameFromDisplay(model)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'trd.party') {
    return {
      computedDisplayWatchFields: ['display'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        syncNameFromDisplay(model)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'trd.warehouse') {
    return {
      computedDisplayWatchFields: ['name', 'address'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = computeWarehouseDisplay(model)
      },
    }
  }

  if (context.kind === 'document' && TRADE_DOCUMENT_DISPLAY_CONFIG[context.typeCode]) {
    const { dateField } = TRADE_DOCUMENT_DISPLAY_CONFIG[context.typeCode]
    return {
      computedDisplayWatchFields: ['number', dateField],
      computedDisplayMode: 'new_or_draft',
      syncComputedDisplay: ({ model }) => {
        model.display = computeTradeDocumentDisplay(context.typeCode, model)
      },
    }
  }

  return null
}
