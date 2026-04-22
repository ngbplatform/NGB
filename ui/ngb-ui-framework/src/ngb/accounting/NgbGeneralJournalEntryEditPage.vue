<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '../auth'
import { ApiError } from '../api/http'
import NgbDrawer from '../components/NgbDrawer.vue'
import NgbFormLayout from '../components/forms/NgbFormLayout.vue'
import NgbFormRow from '../components/forms/NgbFormRow.vue'
import NgbFormSection from '../components/forms/NgbFormSection.vue'
import NgbValidationSummary from '../components/forms/NgbValidationSummary.vue'
import { documentStatusLabel, documentStatusTone, normalizeDocumentStatusValue } from '../editor/documentStatus'
import NgbEntityAuditSidebar from '../editor/NgbEntityAuditSidebar.vue'
import NgbBadge from '../primitives/NgbBadge.vue'
import NgbCheckbox from '../primitives/NgbCheckbox.vue'
import NgbDatePicker from '../primitives/NgbDatePicker.vue'
import NgbIcon from '../primitives/NgbIcon.vue'
import NgbInput from '../primitives/NgbInput.vue'
import NgbTabs from '../primitives/NgbTabs.vue'
import NgbSelect from '../primitives/NgbSelect.vue'
import { useToasts } from '../primitives/toast'
import NgbPageHeader from '../site/NgbPageHeader.vue'
import { navigateBack } from '../router/backNavigation'
import { copyAppLink } from '../router/shareLink'
import {
  approveGeneralJournalEntry,
  createGeneralJournalEntryDraft,
  getGeneralJournalEntry,
  markGeneralJournalEntryForDeletion,
  postGeneralJournalEntry,
  rejectGeneralJournalEntry,
  replaceGeneralJournalEntryLines,
  reverseGeneralJournalEntry,
  submitGeneralJournalEntry,
  unmarkGeneralJournalEntryForDeletion,
  updateGeneralJournalEntryHeader,
} from './generalJournalEntryApi'
import {
  createGeneralJournalEntryLine,
  formatGeneralJournalEntryMoney,
  generalJournalEntryApprovalStateLabel,
  generalJournalEntrySourceLabel,
  normalizeDateOnly,
  normalizeGeneralJournalEntryApprovalState,
  normalizeGeneralJournalEntrySource,
  parseGeneralJournalEntryAmount,
  todayDateOnly,
  toUtcMidday,
} from './generalJournalEntry'
import NgbGeneralJournalEntryLinesEditor from './NgbGeneralJournalEntryLinesEditor.vue'
import { buildGeneralJournalEntriesPath, buildGeneralJournalEntriesListPath } from './navigation'
import type {
  GeneralJournalEntryAccountContextDto,
  GeneralJournalEntryDetailsDto,
  GeneralJournalEntryEditorLineModel,
  GeneralJournalEntryLineDto,
} from './generalJournalEntryTypes'

const props = withDefaults(defineProps<{
  listPath?: string | null
}>(), {
  listPath: null,
})

const route = useRoute()
const router = useRouter()
const toasts = useToasts()
const auth = useAuthStore()

const loading = ref(false)
const saving = ref(false)
const details = ref<GeneralJournalEntryDetailsDto | null>(null)
const errorMessages = ref<string[]>([])
const activeTab = ref('lines')
const auditOpen = ref(false)
const suppressRouteLoad = ref(false)

const draftDate = ref<string | null>(todayDateOnly())
const journalType = ref(1)
const reasonCode = ref('')
const memo = ref('')
const externalReference = ref('')
const autoReverse = ref(false)
const autoReverseOnUtc = ref<string | null>(null)
const lines = ref<GeneralJournalEntryEditorLineModel[]>([createGeneralJournalEntryLine('new-1')])

const rejectReason = ref('')
const reversalDate = ref<string | null>(todayDateOnly())
const reversePostImmediately = ref(true)

const journalTypeOptions = [
  { value: 1, label: 'Standard' },
  { value: 2, label: 'Reversing' },
  { value: 3, label: 'Adjusting' },
  { value: 4, label: 'Opening' },
  { value: 5, label: 'Closing' },
]

const tabs = [
  { key: 'lines', label: 'Lines' },
  { key: 'workflow', label: 'Workflow' },
]

const currentId = computed(() => {
  const raw = route.params.id
  return typeof raw === 'string' && raw.trim().length > 0 ? raw.trim() : null
})

const resolvedListPath = computed(() => {
  const explicit = String(props.listPath ?? '').trim()
  if (explicit) return explicit

  const currentPath = String(route.path ?? '').trim()
  if (!currentPath) return buildGeneralJournalEntriesListPath()
  if (currentPath.endsWith('/new')) return currentPath.slice(0, -4)

  const lastSlash = currentPath.lastIndexOf('/')
  if (lastSlash <= 0) return buildGeneralJournalEntriesListPath()
  return currentPath.slice(0, lastSlash)
})

const isNew = computed(() => !currentId.value)
const documentStatus = computed(() => normalizeDocumentStatusValue(details.value?.document?.status))
const approvalState = computed(() => normalizeGeneralJournalEntryApprovalState(details.value?.header?.approvalState))
const source = computed(() => normalizeGeneralJournalEntrySource(details.value?.header?.source))
const isManualSource = computed(() => source.value !== 2)
const isMarkedForDeletion = computed(() => !!details.value?.document?.isMarkedForDeletion || documentStatus.value === 3)

const canEditDraft = computed(() => !saving.value && (isNew.value || (isManualSource.value && documentStatus.value === 1 && approvalState.value === 1)))
const canSave = computed(() => canEditDraft.value)
const canSubmit = computed(() => !isNew.value && isManualSource.value && documentStatus.value === 1 && approvalState.value === 1 && !isMarkedForDeletion.value && !saving.value)
const canApprove = computed(() => !isNew.value && documentStatus.value === 1 && approvalState.value === 2 && !saving.value)
const canReject = computed(() => canApprove.value)
const canPost = computed(() => !isNew.value && documentStatus.value === 1 && approvalState.value === 3 && !saving.value)
const canReverse = computed(() => !isNew.value && documentStatus.value === 2 && !saving.value)
const canMarkForDeletion = computed(() => !isNew.value && isManualSource.value && documentStatus.value === 1 && !isMarkedForDeletion.value && !saving.value)
const canUnmarkForDeletion = computed(() => !isNew.value && isManualSource.value && isMarkedForDeletion.value && !saving.value)

const showSubmit = computed(() => canSubmit.value)
const showApprove = computed(() => !isNew.value && documentStatus.value === 1 && approvalState.value === 2)
const showReject = computed(() => showApprove.value)
const showPost = computed(() => !isNew.value && documentStatus.value === 1 && approvalState.value === 3)
const showReverse = computed(() => !isNew.value && documentStatus.value === 2)
const canShareLink = computed(() => !isNew.value && !!currentId.value)
const canOpenAudit = computed(() => !isNew.value && !!currentId.value)
const currentActorDisplay = computed(() => {
  const value = String(auth.userName ?? '').trim()
  return value || 'Current user'
})
const initiatedByDisplay = computed(() => resolveActorFieldDisplay(details.value?.header?.initiatedBy))
const submittedByDisplay = computed(() => resolveActorFieldDisplay(details.value?.header?.submittedBy))
const approvedByDisplay = computed(() => resolveActorFieldDisplay(details.value?.header?.approvedBy))
const rejectedByDisplay = computed(() => resolveActorFieldDisplay(details.value?.header?.rejectedBy))
const postedByDisplay = computed(() => resolveActorFieldDisplay(details.value?.header?.postedBy))
const reverseInitiatedByDisplay = computed(() =>
  resolveActorFieldDisplay(source.value === 2 ? details.value?.header?.initiatedBy : null),
)

const pageTitle = computed(() => {
  const display = String(details.value?.document?.display ?? '').trim()
  if (display) return display
  const number = String(details.value?.document?.number ?? '').trim()
  if (number) return `General Journal Entry ${number}`
  return isNew.value ? 'New General Journal Entry' : 'General Journal Entry'
})

const sourceDocumentLabel = computed(() => {
  const value = String(details.value?.header?.reversalOfDocumentDisplay ?? '').trim()
  return value || null
})
const preloadedAccountContexts = computed<Record<string, GeneralJournalEntryAccountContextDto | null>>(() =>
  Object.fromEntries(
    (details.value?.accountContexts ?? []).map((context) => [context.accountId, context]),
  ),
)

const auditEntityKind = 1
const auditEntityId = computed(() => currentId.value)
const auditEntityTitle = computed(() => pageTitle.value)
const documentStatusBadgeTone = computed(() => documentStatusTone(documentStatus.value))

watch(
  () => currentId.value,
  () => {
    if (suppressRouteLoad.value) return
    void load()
  },
  { immediate: true },
)

function resetForNew() {
  details.value = null
  errorMessages.value = []
  activeTab.value = 'lines'
  draftDate.value = todayDateOnly()
  journalType.value = 1
  reasonCode.value = ''
  memo.value = ''
  externalReference.value = ''
  autoReverse.value = false
  autoReverseOnUtc.value = null
  lines.value = [createGeneralJournalEntryLine('new-1')]
  rejectReason.value = ''
  reversalDate.value = todayDateOnly()
  reversePostImmediately.value = true
}

async function applyDetails(dto: GeneralJournalEntryDetailsDto) {
  details.value = dto
  draftDate.value = dateOnlyFromIso(dto.dateUtc)
  journalType.value = Number(dto.header.journalType ?? 1)
  reasonCode.value = dto.header.reasonCode ?? ''
  memo.value = dto.header.memo ?? ''
  externalReference.value = dto.header.externalReference ?? ''
  autoReverse.value = !!dto.header.autoReverse
  autoReverseOnUtc.value = normalizeDateOnly(dto.header.autoReverseOnUtc)
  rejectReason.value = dto.header.rejectReason ?? ''
  reversalDate.value = todayDateOnly()
  reversePostImmediately.value = true
  lines.value = hydrateLines(dto.lines, dto.accountContexts ?? [])
}

async function load() {
  errorMessages.value = []

  if (!currentId.value) {
    resetForNew()
    return
  }

  loading.value = true
  try {
    const dto = await getGeneralJournalEntry(currentId.value)
    await applyDetails(dto)
  } catch (cause) {
    errorMessages.value = extractErrorMessages(cause)
  } finally {
    loading.value = false
  }
}

function dateOnlyFromIso(value: string | null | undefined): string | null {
  return normalizeDateOnly(value)
}

function badgeToneForApproval(): 'neutral' | 'success' | 'warn' {
  if (approvalState.value === 3) return 'success'
  if (approvalState.value === 4) return 'warn'
  return 'neutral'
}

function resolveActorFieldDisplay(value: string | null | undefined): string {
  const raw = String(value ?? '').trim()
  return raw || currentActorDisplay.value
}

function hydrateLines(
  sourceLines: GeneralJournalEntryLineDto[],
  accountContexts: GeneralJournalEntryAccountContextDto[],
): GeneralJournalEntryEditorLineModel[] {
  if (!sourceLines.length) return [createGeneralJournalEntryLine('existing-1')]
  const contextsByAccount = Object.fromEntries(
    accountContexts.map((context) => [context.accountId, context]),
  ) as Record<string, GeneralJournalEntryAccountContextDto | null>

  return sourceLines.map((line) => {
    const context = contextsByAccount[line.accountId] ?? null
    const dimensions: Record<string, { id: string; label: string }> = {}

    for (const dim of line.dimensions ?? []) {
      dimensions[dim.dimensionId] = {
        id: dim.valueId,
        label: String(dim.display ?? dim.valueId),
      }
    }

    return {
      clientKey: `line-${line.lineNo}`,
      side: Number(line.side ?? 1),
      account: {
        id: line.accountId,
        label: String(line.accountDisplay ?? (context ? `${context.code} — ${context.name}` : line.accountId)),
      },
      amount: String(line.amount ?? ''),
      memo: line.memo ?? '',
      dimensions,
    }
  })
}

function buildHeaderRequest() {
  return {
    updatedBy: currentActorDisplay.value,
    journalType: Number(journalType.value),
    reasonCode: reasonCode.value.trim() || null,
    memo: memo.value.trim() || null,
    externalReference: externalReference.value.trim() || null,
    autoReverse: !!autoReverse.value,
    autoReverseOnUtc: autoReverse.value ? normalizeDateOnly(autoReverseOnUtc.value) : null,
  }
}

function buildLinesRequest() {
  return {
    updatedBy: currentActorDisplay.value,
    lines: lines.value
      .filter((line) => !!line.account?.id)
      .map((line) => ({
        side: Number(line.side || 1),
        accountId: line.account!.id,
        amount: parseGeneralJournalEntryAmount(line.amount),
        memo: line.memo.trim() || null,
        dimensions: Object.entries(line.dimensions ?? {})
          .filter(([, item]) => !!item?.id)
          .map(([dimensionId, item]) => ({ dimensionId, valueId: item!.id })),
      })),
  }
}

async function saveDraft(): Promise<boolean> {
  if (!canSave.value) return false

  saving.value = true
  errorMessages.value = []
  try {
    let id = currentId.value
    const headerRequest = buildHeaderRequest()
    const linesRequest = buildLinesRequest()

    if (!id) {
      suppressRouteLoad.value = true
      const created = await createGeneralJournalEntryDraft({
        dateUtc: toUtcMidday(draftDate.value),
      })
      id = created.document.id
      await router.replace(buildGeneralJournalEntriesPath(id, { basePath: resolvedListPath.value }))
    }

    await updateGeneralJournalEntryHeader(id!, headerRequest)
    const finalDto = await replaceGeneralJournalEntryLines(id!, linesRequest)
    await applyDetails(finalDto)

    toasts.push({ title: 'Saved', message: 'Draft was saved.', tone: 'success' })
    return true
  } catch (cause) {
    errorMessages.value = extractErrorMessages(cause)
    return false
  } finally {
    suppressRouteLoad.value = false
    saving.value = false
  }
}

async function submit() {
  const ok = await saveDraft()
  if (!ok || !currentId.value) return

  saving.value = true
  errorMessages.value = []
  try {
    const dto = await submitGeneralJournalEntry(currentId.value, {})
    await applyDetails(dto)
    toasts.push({ title: 'Submitted', message: 'Journal entry was submitted.', tone: 'success' })
  } catch (cause) {
    errorMessages.value = extractErrorMessages(cause)
  } finally {
    saving.value = false
  }
}

async function approve() {
  if (!currentId.value) return
  saving.value = true
  errorMessages.value = []
  try {
    const dto = await approveGeneralJournalEntry(currentId.value, {})
    await applyDetails(dto)
    toasts.push({ title: 'Approved', message: 'Journal entry was approved.', tone: 'success' })
  } catch (cause) {
    errorMessages.value = extractErrorMessages(cause)
  } finally {
    saving.value = false
  }
}

async function reject() {
  if (!currentId.value) return
  saving.value = true
  errorMessages.value = []
  try {
    const dto = await rejectGeneralJournalEntry(currentId.value, {
      rejectReason: rejectReason.value.trim(),
    })
    await applyDetails(dto)
    toasts.push({ title: 'Rejected', message: 'Journal entry was rejected.', tone: 'warn' })
  } catch (cause) {
    errorMessages.value = extractErrorMessages(cause)
  } finally {
    saving.value = false
  }
}

async function postApproved() {
  if (!currentId.value) return
  saving.value = true
  errorMessages.value = []
  try {
    const dto = await postGeneralJournalEntry(currentId.value, {})
    await applyDetails(dto)
    toasts.push({ title: 'Posted', message: 'Journal entry was posted.', tone: 'success' })
  } catch (cause) {
    errorMessages.value = extractErrorMessages(cause)
  } finally {
    saving.value = false
  }
}

async function reversePosted() {
  if (!currentId.value) return
  saving.value = true
  errorMessages.value = []
  try {
    const reversed = await reverseGeneralJournalEntry(currentId.value, {
      reversalDateUtc: toUtcMidday(reversalDate.value),
      postImmediately: !!reversePostImmediately.value,
    })

    suppressRouteLoad.value = true
    await router.replace(buildGeneralJournalEntriesPath(reversed.document.id, { basePath: resolvedListPath.value }))
    await applyDetails(reversed)
    toasts.push({ title: 'Reversed', message: 'Reversal journal entry was created.', tone: 'success' })
  } catch (cause) {
    errorMessages.value = extractErrorMessages(cause)
  } finally {
    suppressRouteLoad.value = false
    saving.value = false
  }
}

async function toggleMarkForDeletion() {
  if (!currentId.value) return
  const isRestore = isMarkedForDeletion.value
  saving.value = true
  errorMessages.value = []
  try {
    const dto = isRestore
      ? await unmarkGeneralJournalEntryForDeletion(currentId.value)
      : await markGeneralJournalEntryForDeletion(currentId.value)

    await applyDetails(dto)
    toasts.push({
      title: isRestore ? 'Restored' : 'Marked for deletion',
      message: isRestore ? 'Draft was restored.' : 'Draft was marked for deletion.',
      tone: isRestore ? 'success' : 'warn',
    })
  } catch (cause) {
    errorMessages.value = extractErrorMessages(cause)
  } finally {
    saving.value = false
  }
}

async function copyShareLink(): Promise<void> {
  if (!currentId.value) return
  await copyAppLink(
    router,
    toasts,
    buildGeneralJournalEntriesPath(currentId.value, { basePath: resolvedListPath.value }),
  )
}

function openAuditLog() {
  if (!canOpenAudit.value) return
  auditOpen.value = true
}

function closeAuditLog() {
  auditOpen.value = false
}

function closePage() {
  void navigateBack(router, route, resolvedListPath.value)
}

function extractErrorMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    const messages: string[] = []

    if (error.errorCode === 'gje.lines.required') messages.push('Add at least one journal line.')
    else if (error.errorCode === 'gje.lines.debit_and_credit_required') messages.push('Add at least one debit line and one credit line.')
    else if (error.errorCode === 'gje.lines.unbalanced') {
      const debit = Number(error.context?.debit ?? 0)
      const credit = Number(error.context?.credit ?? 0)
      messages.push(`Journal is unbalanced. Debit ${formatGeneralJournalEntryMoney(debit)} vs Credit ${formatGeneralJournalEntryMoney(credit)}.`)
    } else if (error.errorCode === 'gje.business_field.required') {
      const field = String(error.context?.field ?? 'Field')
      messages.push(`${field} is required.`)
    } else if (error.errorCode === 'gje.line.dimensions.invalid') {
      const lineNo = Number(error.context?.lineNo ?? 0)
      const accountCode = String(error.context?.accountCode ?? '').trim()
      const reason = String(error.context?.reason ?? '').trim().replace(/_/g, ' ')
      const parts = [`Line ${lineNo || '?'} has invalid dimensions.`]
      if (accountCode) parts.push(`Account: ${accountCode}.`)
      if (reason) parts.push(`Reason: ${reason}.`)
      messages.push(parts.join(' '))
    }

    for (const issue of error.issues ?? []) {
      const message = String(issue.message ?? '').trim()
      if (message) messages.push(message)
    }

    if (messages.length === 0) {
      const fromErrors = Object.values(error.errors ?? {}).flat().map((x) => String(x ?? '').trim()).filter(Boolean)
      messages.push(...fromErrors)
    }

    if (messages.length === 0 && error.message) messages.push(error.message)
    return Array.from(new Set(messages.filter(Boolean)))
  }

  return [error instanceof Error ? error.message : String(error)]
}
</script>

<template>
  <div data-testid="journal-entry-edit-page" class="flex h-full min-h-0 flex-col bg-ngb-bg">
    <NgbPageHeader :title="pageTitle" can-back @back="navigateBack(router, route, resolvedListPath)">
      <template #secondary>
        <div class="min-w-0 flex flex-wrap items-center gap-2">
          <NgbBadge :tone="documentStatusBadgeTone">{{ documentStatusLabel(documentStatus) }}</NgbBadge>
          <NgbBadge :tone="badgeToneForApproval()">{{ generalJournalEntryApprovalStateLabel(approvalState) }}</NgbBadge>
          <NgbBadge tone="neutral">{{ generalJournalEntrySourceLabel(source) }}</NgbBadge>
          <NgbBadge v-if="sourceDocumentLabel" tone="neutral">Reversal Of: {{ sourceDocumentLabel }}</NgbBadge>
        </div>
      </template>

      <template #actions>
        <button
          v-if="canShareLink"
          class="ngb-iconbtn"
          :disabled="loading || saving"
          title="Share link"
          @click="copyShareLink"
        >
          <NgbIcon name="share" />
        </button>

        <button
          v-if="canOpenAudit"
          class="ngb-iconbtn"
          :disabled="loading || saving"
          title="Audit log"
          @click="openAuditLog"
        >
          <NgbIcon name="history" />
        </button>

        <button
          v-if="canMarkForDeletion || canUnmarkForDeletion"
          class="ngb-iconbtn"
          :disabled="loading || saving"
          :title="canUnmarkForDeletion ? 'Unmark for deletion' : 'Mark for deletion'"
          @click="toggleMarkForDeletion"
        >
          <NgbIcon :name="canUnmarkForDeletion ? 'trash-restore' : 'trash'" />
        </button>

        <button
          class="ngb-iconbtn"
          :disabled="loading || saving || !canSave"
          title="Save"
          @click="saveDraft"
        >
          <NgbIcon name="save" />
        </button>

        <button
          v-if="showSubmit"
          class="ngb-iconbtn"
          :disabled="loading || saving || !canSubmit"
          title="Submit"
          @click="submit"
        >
          <NgbIcon name="arrow-right" />
        </button>

        <button
          v-if="showApprove"
          class="ngb-iconbtn"
          :disabled="loading || saving || !canApprove"
          title="Approve"
          @click="approve"
        >
          <NgbIcon name="shield-check" />
        </button>

        <button
          v-if="showReject"
          class="ngb-iconbtn"
          :disabled="loading || saving || !canReject || !rejectReason.trim()"
          title="Reject"
          @click="reject"
        >
          <NgbIcon name="circle-x" />
        </button>

        <button
          v-if="showPost"
          class="ngb-iconbtn"
          :disabled="loading || saving || !canPost"
          title="Post"
          @click="postApproved"
        >
          <NgbIcon name="check" />
        </button>

        <button
          v-if="showReverse"
          class="ngb-iconbtn"
          :disabled="loading || saving || !canReverse"
          title="Reverse"
          @click="reversePosted"
        >
          <NgbIcon name="undo" />
        </button>

        <button class="ngb-iconbtn" :disabled="loading || saving" title="Close" @click="closePage">
          <NgbIcon name="x" />
        </button>
      </template>
    </NgbPageHeader>

    <div class="flex flex-1 min-h-0 overflow-auto overscroll-contain bg-ngb-bg p-6">
      <div class="min-h-full w-full space-y-4 bg-ngb-bg pb-6">
        <div
          v-if="!isNew && isMarkedForDeletion"
          class="flex items-start justify-between gap-3 rounded-[var(--ngb-radius)] border border-red-200 bg-red-50 p-3 dark:border-red-900/50 dark:bg-red-950/30"
        >
          <div class="min-w-0">
            <div class="text-sm font-semibold text-ngb-danger">Deleted</div>
            <div class="mt-1 text-sm text-ngb-muted">
              This document is marked for deletion. Restore it to edit or continue the workflow.
            </div>
          </div>
        </div>

        <NgbValidationSummary :messages="errorMessages" />

        <div
          v-if="!isManualSource && !isNew"
          class="rounded-[var(--ngb-radius)] border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900/50 dark:bg-amber-950/20 dark:text-amber-100"
        >
          This is a system-generated journal entry. Header and lines are read-only.
        </div>

        <div v-if="loading" class="text-sm text-ngb-muted">Loading…</div>

        <template v-else>
          <NgbFormLayout>
            <NgbFormSection title="Header" description="General journal entry business fields and workflow state.">
              <NgbFormRow label="Document Date" hint="Date is fixed when the draft is created.">
                <NgbDatePicker v-model="draftDate" :disabled="!isNew || saving" />
              </NgbFormRow>

              <NgbFormRow label="Initiated By">
                <NgbInput :model-value="initiatedByDisplay" readonly />
              </NgbFormRow>

              <NgbFormRow label="Journal Type">
                <NgbSelect v-model="journalType" :options="journalTypeOptions" :disabled="!canEditDraft" />
              </NgbFormRow>

              <NgbFormRow label="Reason Code">
                <NgbInput v-model="reasonCode" :disabled="!canEditDraft" placeholder="Optional business reason code" />
              </NgbFormRow>

              <NgbFormRow label="Memo">
                <NgbInput v-model="memo" :disabled="!canEditDraft" placeholder="Explain the journal entry" />
              </NgbFormRow>

              <NgbFormRow label="External Reference">
                <NgbInput v-model="externalReference" :disabled="!canEditDraft" placeholder="External ticket, import id, or source ref" />
              </NgbFormRow>

              <NgbFormRow label="Auto Reverse">
                <div class="flex h-9 items-center gap-3">
                  <NgbCheckbox v-model="autoReverse" :disabled="!canEditDraft" />
                  <div class="w-[12rem]">
                    <NgbDatePicker v-model="autoReverseOnUtc" :disabled="!canEditDraft || !autoReverse" />
                  </div>
                </div>
              </NgbFormRow>
            </NgbFormSection>
          </NgbFormLayout>

          <NgbTabs v-model="activeTab" :tabs="tabs" full-width-bar>
            <template #default="{ active }">
              <div v-if="active === 'lines'" class="space-y-4 pb-6">
                <NgbGeneralJournalEntryLinesEditor
                  v-model="lines"
                  :readonly="!canEditDraft"
                  :preloaded-account-contexts="preloadedAccountContexts"
                />

                <div
                  v-if="details?.allocations?.length"
                  class="overflow-hidden rounded-[var(--ngb-radius)] border border-ngb-border bg-ngb-card"
                >
                  <div class="border-b border-ngb-border px-4 py-3">
                    <div class="text-sm font-semibold text-ngb-text">Allocations</div>
                    <div class="mt-0.5 text-xs text-ngb-muted">Deterministic debit/credit pairings generated by posting.</div>
                  </div>

                  <table class="w-full table-fixed text-sm">
                    <thead class="bg-ngb-bg text-xs text-ngb-muted">
                      <tr>
                        <th class="border-r border-dotted border-ngb-border px-3 py-2 text-left font-semibold">Entry</th>
                        <th class="border-r border-dotted border-ngb-border px-3 py-2 text-left font-semibold">Debit Line</th>
                        <th class="border-r border-dotted border-ngb-border px-3 py-2 text-left font-semibold">Credit Line</th>
                        <th class="px-3 py-2 text-right font-semibold">Amount</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr
                        v-for="allocation in details?.allocations ?? []"
                        :key="allocation.entryNo"
                        class="border-t border-ngb-border transition-colors hover:bg-ngb-bg"
                      >
                        <td class="border-r border-dotted border-ngb-border px-3 py-2">{{ allocation.entryNo }}</td>
                        <td class="border-r border-dotted border-ngb-border px-3 py-2">{{ allocation.debitLineNo }}</td>
                        <td class="border-r border-dotted border-ngb-border px-3 py-2">{{ allocation.creditLineNo }}</td>
                        <td class="px-3 py-2 text-right">{{ formatGeneralJournalEntryMoney(allocation.amount) }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>

              <NgbFormLayout v-else class="pb-6">
                <NgbFormSection title="Workflow" description="Workflow actors are captured automatically from the authenticated current user.">
                  <NgbFormRow label="Submitted By">
                    <NgbInput :model-value="submittedByDisplay" readonly />
                  </NgbFormRow>

                  <NgbFormRow label="Approved By">
                    <NgbInput :model-value="approvedByDisplay" readonly />
                  </NgbFormRow>

                  <NgbFormRow label="Rejected By">
                    <NgbInput :model-value="rejectedByDisplay" readonly />
                  </NgbFormRow>

                  <NgbFormRow label="Reject Reason">
                    <NgbInput v-model="rejectReason" :disabled="!canReject" placeholder="Required when rejecting" />
                  </NgbFormRow>

                  <NgbFormRow label="Posted By">
                    <NgbInput :model-value="postedByDisplay" readonly />
                  </NgbFormRow>

                  <NgbFormRow label="Reverse Initiated By">
                    <NgbInput :model-value="reverseInitiatedByDisplay" readonly />
                  </NgbFormRow>

                  <NgbFormRow label="Reversal Date">
                    <div class="flex items-center gap-3">
                      <div class="w-[12rem]">
                        <NgbDatePicker v-model="reversalDate" :disabled="!canReverse" />
                      </div>
                      <div class="flex h-9 items-center gap-2">
                        <NgbCheckbox v-model="reversePostImmediately" :disabled="!canReverse" />
                        <span class="text-sm text-ngb-muted">Post immediately</span>
                      </div>
                    </div>
                  </NgbFormRow>
                </NgbFormSection>
              </NgbFormLayout>
            </template>
          </NgbTabs>
        </template>
      </div>
    </div>

    <NgbDrawer v-model:open="auditOpen" title="Audit Log" hide-header flush-body>
      <NgbEntityAuditSidebar
        :open="auditOpen"
        :entity-kind="auditEntityKind"
        :entity-id="auditEntityId"
        :entity-title="auditEntityTitle"
        @back="closeAuditLog"
        @close="closeAuditLog"
      />
    </NgbDrawer>
  </div>
</template>
