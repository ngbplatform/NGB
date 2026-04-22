import type {
  EditorConfiguredDocumentAction,
  ResolveEditorDocumentActionsArgs,
} from 'ngb-ui-framework'
import { tryExtractReferenceId } from 'ngb-ui-framework'

import { buildPmOpenItemsPath } from '../router/pmRoutePaths'

const RECEIVABLE_APPLY_DOCUMENT_TYPES = new Set([
  'pm.receivable_charge',
  'pm.rent_charge',
  'pm.late_fee_charge',
  'pm.receivable_payment',
  'pm.receivable_credit_memo',
])

const PAYABLE_APPLY_DOCUMENT_TYPES = new Set([
  'pm.payable_charge',
  'pm.payable_payment',
  'pm.payable_credit_memo',
])

function applyActionMessages(args: ResolveEditorDocumentActionsArgs): string[] {
  return (args.uiEffects?.disabledReasons?.apply ?? [])
    .map((item) => String(item?.message ?? '').trim())
    .filter((item) => item.length > 0)
}

function resolveReceivablesApplyAction(
  args: ResolveEditorDocumentActionsArgs,
): EditorConfiguredDocumentAction | null {
  if (!RECEIVABLE_APPLY_DOCUMENT_TYPES.has(args.context.typeCode)) return null
  if (!args.uiEffects) return null

  const leaseId = tryExtractReferenceId(args.model.lease_id)
  const disabledMessages = applyActionMessages(args)
  const title = disabledMessages.length > 0
    ? disabledMessages.join(' ')
    : !leaseId
      ? 'Apply is unavailable: lease is missing.'
      : 'Apply'

  const disabled = args.loading || args.saving || !args.uiEffects.canApply || !leaseId

  return {
    item: {
      key: 'openApply',
      title,
      icon: 'file-apply',
      disabled,
    },
    run: () => {
      if (disabled || !leaseId) return
      args.navigate(`${buildPmOpenItemsPath('receivables')}?${new URLSearchParams({
        leaseId,
        focusItemId: args.documentId,
        openApply: '1',
        refresh: '1',
        source: args.context.typeCode,
      }).toString()}`)
    },
  }
}

function resolvePayablesApplyAction(
  args: ResolveEditorDocumentActionsArgs,
): EditorConfiguredDocumentAction | null {
  if (!PAYABLE_APPLY_DOCUMENT_TYPES.has(args.context.typeCode)) return null
  if (!args.uiEffects) return null

  const partyId = tryExtractReferenceId(args.model.party_id)
  const propertyId = tryExtractReferenceId(args.model.property_id)
  const disabledMessages = applyActionMessages(args)
  const title = disabledMessages.length > 0
    ? disabledMessages.join(' ')
    : !partyId
      ? 'Apply is unavailable: vendor is missing.'
      : !propertyId
        ? 'Apply is unavailable: property is missing.'
        : 'Apply'

  const disabled = args.loading || args.saving || !args.uiEffects.canApply || !partyId || !propertyId

  return {
    item: {
      key: 'openApply',
      title,
      icon: 'file-apply',
      disabled,
    },
    run: () => {
      if (disabled || !partyId || !propertyId) return
      args.navigate(`${buildPmOpenItemsPath('payables')}?${new URLSearchParams({
        partyId,
        propertyId,
        focusItemId: args.documentId,
        openApply: '1',
        refresh: '1',
        source: args.context.typeCode,
      }).toString()}`)
    },
  }
}

export function resolvePmEditorDocumentActions(
  args: ResolveEditorDocumentActionsArgs,
): EditorConfiguredDocumentAction[] {
  const action = resolveReceivablesApplyAction(args) ?? resolvePayablesApplyAction(args)
  return action ? [action] : []
}
