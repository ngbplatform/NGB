import { optionLabelForFilter } from '../metadata/filtering'
import { normalizeDateOnlyValue } from '../utils/dateValues'
import { slugifyVariantCode } from './composer'
import type {
  ReportComposerDraft,
  ReportDefinitionDto,
  ReportFilterFieldDto,
  ReportVariantDto,
} from './types'

export type ReportPageBadge = {
  key: string
  text: string
}

export function hasFilterStateValue(field: ReportFilterFieldDto, composer: ReportComposerDraft): boolean {
  const state = composer.filters[field.fieldCode]
  if (!state) return false
  if (state.items.length > 0) return true
  return state.raw.trim().length > 0
}

export function canAutoRunReport(definition: ReportDefinitionDto, composer: ReportComposerDraft): boolean {
  const missingParameter = (definition.parameters ?? []).some((parameter) => parameter.isRequired && String(composer.parameters[parameter.code] ?? '').trim().length === 0)
  if (missingParameter) return false
  return !(definition.filters ?? []).some((field) => field.isRequired && !hasFilterStateValue(field, composer))
}

export function normalizeParameterDataType(dataType: string): string {
  return dataType.trim().toLowerCase().replace(/[^a-z0-9]+/g, '_')
}

export function isInlineDateParameterDataType(dataType: string): boolean {
  const normalized = normalizeParameterDataType(dataType)
  return normalized === 'date' || normalized === 'date_only' || normalized === 'date_time_utc'
}

export function parameterLabel(parameter: { code: string; label?: string | null }): string {
  return String(parameter.label ?? parameter.code).trim() || String(parameter.code ?? '').trim()
}

export function normalizeReportDateValue(value: unknown): string | null {
  return normalizeDateOnlyValue(String(value ?? '').trim())
}

export function normalizeCode(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '_')
}

export function tryResolveOptionLabel(field: ReportFilterFieldDto, rawValue: string): string {
  const normalized = rawValue.trim()
  if (normalized.length === 0) return normalized
  return optionLabelForFilter(field, normalized) || normalized
}

export function chooseAvailableVariantCode(
  name: string,
  variants: readonly ReportVariantDto[],
  exceptVariantCode?: string | null,
): string {
  const base = slugifyVariantCode(name)

  let candidate = base
  let suffix = 2
  const except = String(exceptVariantCode ?? '').trim()

  while (variants.some((variant) => variant.variantCode === candidate && variant.variantCode !== except)) {
    candidate = `${base}-${suffix}`
    suffix += 1
  }

  return candidate
}
