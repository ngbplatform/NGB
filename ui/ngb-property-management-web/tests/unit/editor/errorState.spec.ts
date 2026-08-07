import { computed, nextTick, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  normalizeEntityEditorError: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  dedupeEntityEditorMessages: (messages: string[]) => [...new Set(messages.filter(Boolean))],
  humanizeEntityEditorFieldKey: (key: string) => `Human ${key}`,
  isEntityEditorFormIssuePath: (path: string) => path === '_form' || path === 'form',
  normalizeEntityEditorError: mocks.normalizeEntityEditorError,
}))

import { useEntityEditorErrorState } from '../../../src/editor/pm/useEntityEditorErrorState'

type Issue = {
  path: string
  label?: string
  scope?: string
  messages?: string[]
  code?: string | null
}

function issue(value: Issue) {
  return {
    label: value.label ?? value.path,
    scope: value.scope ?? 'field',
    messages: value.messages ?? ['Invalid'],
    code: value.code ?? null,
    path: value.path,
  }
}

function error(issues: Issue[] = [], summary = 'Please fix errors') {
  return {
    summary,
    issues: issues.map(issue),
    errorCode: null,
    status: 400,
    context: null,
  }
}

function setup(options: {
  isLease?: boolean
  rows?: Record<string, unknown>[]
  labels?: Record<string, string>
  formRef?: Record<string, unknown> | null
  gridRef?: Record<string, unknown> | null
} = {}) {
  const isLease = ref(options.isLease ?? true)
  const rows = ref(options.rows ?? [])
  const formRef = ref(options.formRef ?? null)
  const leaseGridRef = ref(options.gridRef ?? null)
  const state = useEntityEditorErrorState({
    fieldLabels: computed(() => options.labels ?? { subject: 'Subject', name: 'Name' }),
    isLeaseDocument: computed(() => isLease.value),
    leasePartiesRows: rows,
    loading: ref(false),
    saving: ref(false),
    formRef,
    leaseGridRef,
  } as never)
  return { state, isLease, rows, formRef, leaseGridRef }
}

describe('property-management entity editor error state', () => {
  beforeEach(() => vi.clearAllMocks())

  it('normalizes errors with field, form, tenants, row, and fallback labels', () => {
    mocks.normalizeEntityEditorError.mockImplementation((_cause, options) => ({
      labels: [
        options.resolveIssueLabel(null),
        options.resolveIssueLabel(''),
        options.resolveIssueLabel('_form'),
        options.resolveIssueLabel('parties'),
        options.resolveIssueLabel('parties[0].party_id'),
        options.resolveIssueLabel('parties.1.role'),
        options.resolveIssueLabel('parties[x].unknown'),
        options.resolveIssueLabel('subject'),
        options.resolveIssueLabel('unknown_key'),
      ],
    }))
    const { state } = setup()
    expect(state.normalizeEditorError(new Error('boom'))).toEqual({
      labels: [
        'Validation',
        'Validation',
        'Validation',
        'Tenants',
        'Tenant #1 / Party',
        'Tenant #2 / Role',
        'Human parties[x].unknown',
        'Subject',
        'Human unknown_key',
      ],
    })
  })

  it('maps every lease party field label', () => {
    mocks.normalizeEntityEditorError.mockImplementation((_cause, options) => ({
      labels: [
        options.resolveIssueLabel('parties[0].is_primary'),
        options.resolveIssueLabel('parties[0].ordinal'),
        options.resolveIssueLabel('parties[0].custom'),
      ],
    }))
    expect(setup().state.normalizeEditorError(null)).toEqual({
      labels: ['Tenant #1 / Primary', 'Tenant #1 / Line No', 'Tenant #1 / Human custom'],
    })
  })

  it('labels a syntactically valid but non-finite tenant index without an ordinal', () => {
    const path = `parties[${'9'.repeat(400)}].role`
    mocks.normalizeEntityEditorError.mockImplementation((_cause, options) => ({
      label: options.resolveIssueLabel(path),
    }))
    expect(setup().state.normalizeEditorError(null)).toEqual({ label: 'Tenant / Role' })
  })

  it('returns no displayed error for null or fully dismissed issues', async () => {
    const { state } = setup()
    expect(state.displayedError.value).toBeNull()
    expect(state.inlineFieldErrors.value).toEqual({})
    expect(state.leaseTenantValidation.value).toBeNull()
    expect(state.bannerIssues.value).toEqual([])
    state.setEditorError(error([{ path: 'subject' }]) as never)
    await nextTick()
    state.dismissFieldIssues('subject')
    expect(state.displayedError.value).toBeNull()
    state.setEditorError(null)
    expect(state.displayedError.value).toBeNull()
  })

  it('exposes only the first valid inline message for known field issues', () => {
    const { state } = setup({ isLease: false })
    state.setEditorError(error([
      { path: 'subject', messages: ['', 'Required', 'Later'] },
      { path: 'subject', messages: ['Duplicate'] },
      { path: 'unknown', messages: ['Unknown'] },
      { path: 'name', scope: 'collection', messages: ['Collection'] },
      { path: '_form', messages: ['Form'] },
      { path: 'parties', messages: ['Lease'] },
    ]) as never)
    expect(state.inlineFieldErrors.value).toEqual({ subject: 'Required' })
    expect(state.bannerIssues.value.map((entry) => entry.path)).toEqual(['unknown', 'name', '_form'])
  })

  it('keeps an issue without a usable inline message in the banner', () => {
    const { state } = setup({ isLease: false })
    state.setEditorError(error([{ path: 'name', messages: ['', '   '] }]) as never)
    expect(state.inlineFieldErrors.value).toEqual({})
    expect(state.bannerIssues.value).toHaveLength(1)
  })

  it('focuses a lease issue first when the grid accepts focus', async () => {
    const focusFirstError = vi.fn(() => true)
    const focusField = vi.fn(() => true)
    const { state } = setup({ gridRef: { focusFirstError }, formRef: { focusField } })
    state.setEditorError(error([{ path: 'parties[0].party_id' }, { path: 'subject' }]) as never)
    await nextTick()
    expect(focusFirstError).toHaveBeenCalledOnce()
    expect(focusField).not.toHaveBeenCalled()
  })

  it('continues from rejected lease focus to a non-form field', async () => {
    const focusFirstError = vi.fn(() => false)
    const focusField = vi.fn(() => true)
    const { state } = setup({ gridRef: { focusFirstError }, formRef: { focusField } })
    state.setEditorError(error([
      { path: 'parties[0].party_id' },
      { path: '_form' },
      { path: 'subject' },
    ]) as never)
    await nextTick()
    expect(focusField).toHaveBeenCalledWith('subject')
  })

  it('uses fallback grid and form focusing when no issue accepts direct focus', () => {
    const gridFocus = vi.fn(() => false)
    const focusField = vi.fn(() => false)
    const focusFirstError = vi.fn(() => true)
    const { state } = setup({
      gridRef: { focusFirstError: gridFocus },
      formRef: { focusField, focusFirstError },
    })
    state.setEditorError(error([{ path: 'subject' }]) as never)
    state.focusFirstValidationError()
    expect(gridFocus).toHaveBeenCalled()
    expect(focusFirstError).toHaveBeenCalledWith(['subject'])
  })

  it('stops at the fallback grid when it accepts focus', () => {
    const gridFocus = vi.fn(() => true)
    const focusField = vi.fn(() => false)
    const focusFirstError = vi.fn()
    const { state } = setup({
      gridRef: { focusFirstError: gridFocus },
      formRef: { focusField, focusFirstError },
    })
    state.setEditorError(error([{ path: 'subject' }]) as never)
    state.focusFirstValidationError()
    expect(gridFocus).toHaveBeenCalled()
    expect(focusFirstError).not.toHaveBeenCalled()
  })

  it('handles present focus objects with absent optional focus methods', () => {
    const { state } = setup({ gridRef: {}, formRef: {} })
    state.setEditorError(error([{ path: 'parties[0].party_id' }, { path: 'subject' }]) as never)
    expect(() => state.focusFirstValidationError()).not.toThrow()
  })

  it('does nothing when there is no displayed error or focus handles', () => {
    const { state } = setup()
    expect(() => state.focusFirstValidationError()).not.toThrow()
    state.setEditorError(error([{ path: 'subject' }]) as never)
    expect(() => state.focusFirstValidationError()).not.toThrow()
  })

  it('maps exact bracket and dot tenant row issues and rejects invalid row paths', () => {
    const { state } = setup({ rows: [{}, {}] })
    const nonFiniteRow = `parties[${'9'.repeat(400)}].role`
    state.setEditorError(error([
      { path: 'parties[0].party_id', messages: ['Required', 'Required'] },
      { path: 'parties.1.role', messages: ['Role'] },
      { path: nonFiniteRow, messages: ['Bad'] },
      { path: 'parties[x].role', messages: ['Unknown'] },
    ]) as never)
    expect(state.leaseTenantValidation.value).toEqual({
      summary: ['Bad'],
      rowErrors: {
        0: { party_id: ['Required'] },
        1: { role: ['Role'] },
      },
      focusTarget: { rowIndex: 0, field: 'party_id' },
    })
    expect(state.bannerIssues.value.map((entry) => entry.path)).toContain('parties[x].role')
  })

  it('maps parties summary to multiple primary rows', () => {
    const { state } = setup({
      rows: [
        { is_primary: true },
        { is_primary: true },
      ],
    })
    state.setEditorError(error([{ path: 'parties', messages: ['One primary'] }]) as never)
    expect(state.leaseTenantValidation.value).toEqual({
      summary: ['One primary'],
      rowErrors: {
        0: { is_primary: ['One primary'] },
        1: { is_primary: ['One primary'] },
      },
      focusTarget: { rowIndex: 0, field: 'is_primary' },
    })
  })

  it('maps a missing primary summary to the first available row', () => {
    const { state } = setup({ rows: [{ is_primary: false }] })
    state.setEditorError(error([{ path: 'parties', messages: ['Primary required'] }]) as never)
    expect(state.leaseTenantValidation.value?.rowErrors[0]?.is_primary).toEqual(['Primary required'])
  })

  it('keeps an empty parties collection issue as summary only', () => {
    const { state } = setup({ rows: [] })
    state.setEditorError(error([{ path: 'parties', messages: ['Tenant required'] }]) as never)
    expect(state.leaseTenantValidation.value).toEqual({
      summary: ['Tenant required'],
      rowErrors: {},
      focusTarget: null,
    })
  })

  it('maps duplicate tenant references from strings and objects', () => {
    const { state } = setup({
      rows: [
        { party_id: 'same' },
        { party_id: { id: ' same ' } },
        { party_id: '' },
        { party_id: '   ' },
        { party_id: { id: '' } },
        { party_id: { id: 10 } },
        { party_id: 10 },
      ],
    })
    state.setEditorError(error([{ path: 'parties[].party_id', messages: ['Duplicate'] }]) as never)
    expect(state.leaseTenantValidation.value?.rowErrors).toEqual({
      0: { party_id: ['Duplicate'] },
      1: { party_id: ['Duplicate'] },
    })
  })

  it('handles sparse tenant row arrays while matching duplicate ids', () => {
    const rows = new Array(3)
    rows[1] = { party_id: 'same' }
    rows[2] = { party_id: 'same' }
    const { state } = setup({ rows })
    state.setEditorError(error([{ path: 'parties[].party_id', messages: ['Duplicate'] }]) as never)
    expect(state.leaseTenantValidation.value?.rowErrors).toEqual({
      1: { party_id: ['Duplicate'] },
      2: { party_id: ['Duplicate'] },
    })
  })

  it('falls back to summary when wildcard party ids are unique', () => {
    const { state } = setup({ rows: [{ party_id: 'one' }, { party_id: 'two' }] })
    state.setEditorError(error([{ path: 'parties[].party_id', messages: ['Invalid'] }]) as never)
    expect(state.leaseTenantValidation.value?.summary).toEqual(['Invalid'])
  })

  it('maps duplicate finite ordinals and ignores non-finite ordinals', () => {
    const { state } = setup({ rows: [{ ordinal: 1 }, { ordinal: '1' }, { ordinal: 'bad' }] })
    state.setEditorError(error([{ path: 'parties[].ordinal', messages: ['Duplicate ordinal'] }]) as never)
    expect(state.leaseTenantValidation.value?.rowErrors).toEqual({
      0: { ordinal: ['Duplicate ordinal'] },
      1: { ordinal: ['Duplicate ordinal'] },
    })
  })

  it('falls back to summary for unique wildcard ordinals', () => {
    const { state } = setup({ rows: [{ ordinal: 1 }, { ordinal: 2 }] })
    state.setEditorError(error([{ path: 'parties[].ordinal', messages: ['Invalid'] }]) as never)
    expect(state.leaseTenantValidation.value?.summary).toEqual(['Invalid'])
  })

  it('maps primary role and primary flag mismatches', () => {
    const { state } = setup({
      rows: [
        { role: 'Tenant', is_primary: true },
        { role: 'PrimaryTenant', is_primary: false },
        { is_primary: true },
      ],
    })
    state.setEditorError(error([
      { path: 'parties[].role', messages: ['Role mismatch'] },
      { path: 'parties[].is_primary', messages: ['Flag mismatch'] },
    ]) as never)
    expect(state.leaseTenantValidation.value?.rowErrors).toEqual({
      0: { role: ['Role mismatch'] },
      1: { is_primary: ['Flag mismatch'] },
      2: { role: ['Role mismatch'] },
    })
  })

  it('falls back to summary for wildcard role and primary fields without mismatches', () => {
    const { state } = setup({
      rows: [{ role: 'PrimaryTenant', is_primary: true }],
    })
    state.setEditorError(error([
      { path: 'parties[].role', messages: ['Role'] },
      { path: 'parties[].is_primary', messages: ['Flag'] },
    ]) as never)
    expect(state.leaseTenantValidation.value?.summary).toEqual(['Role', 'Flag'])
  })

  it('returns null validation for non-leases and for lease errors outside the tenant part', () => {
    const { state, isLease } = setup({ isLease: false })
    state.setEditorError(error([{ path: 'subject' }]) as never)
    expect(state.leaseTenantValidation.value).toBeNull()
    isLease.value = true
    expect(state.leaseTenantValidation.value).toBeNull()
  })

  it('dismisses field and lease issues without affecting unrelated issues', () => {
    const { state } = setup({ rows: [{}] })
    state.dismissFieldIssues('subject')
    state.dismissLeaseIssues()
    state.setEditorError(error([
      { path: 'subject' },
      { path: 'parties[0].party_id' },
      { path: '_form' },
    ]) as never)
    state.dismissFieldIssues('missing')
    state.dismissFieldIssues('subject')
    state.dismissFieldIssues('subject')
    expect(state.displayedError.value?.issues.map((entry) => entry.path)).toEqual([
      'parties[0].party_id', '_form',
    ])
    state.dismissLeaseIssues()
    expect(state.displayedError.value?.issues.map((entry) => entry.path)).toEqual(['_form'])
  })
})
