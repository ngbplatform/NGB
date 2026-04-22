import type { EditorEntityProfile, EntityEditorContext } from 'ngb-ui-framework'
import { asTrimmedString } from 'ngb-ui-framework'

function computeDisplayFrom(...values: unknown[]): string | null {
  const parts = values
    .map((value) => asTrimmedString(value))
    .filter((value): value is string => !!value)

  return parts.length > 0 ? parts.join(' · ') : null
}

export function resolveAgencyBillingEditorEntityProfile(context: EntityEditorContext): EditorEntityProfile | null {
  if (context.kind === 'catalog' && context.typeCode === 'ab.client') {
    return {
      computedDisplayWatchFields: ['name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = asTrimmedString(model.name)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'ab.team_member') {
    return {
      computedDisplayWatchFields: ['full_name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = asTrimmedString(model.full_name)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'ab.project') {
    return {
      computedDisplayWatchFields: ['name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = asTrimmedString(model.name)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'ab.rate_card') {
    return {
      computedDisplayWatchFields: ['name', 'service_title'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = computeDisplayFrom(model.name, model.service_title) ?? asTrimmedString(model.name)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'ab.service_item') {
    return {
      computedDisplayWatchFields: ['name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = asTrimmedString(model.name)
      },
    }
  }

  if (context.kind === 'catalog' && context.typeCode === 'ab.payment_terms') {
    return {
      computedDisplayWatchFields: ['name'],
      computedDisplayMode: 'always',
      syncComputedDisplay: ({ model }) => {
        model.display = asTrimmedString(model.name)
      },
    }
  }

  return null
}
