import { describe, expect, it } from 'vitest'

import { getLookupHint } from '../../../src/lookup/hints'

describe('property-management lookup hints', () => {
  it('preserves every explicit lookup kind and applies PM catalog filters', () => {
    expect(getLookupHint('pm.payable_charge', 'PARTY_ID', { kind: 'catalog', catalogType: 'pm.party' }))
      .toEqual({ kind: 'catalog', catalogType: 'pm.party', filters: { is_vendor: 'true' } })
    expect(getLookupHint('pm.rent_charge', 'party_id', { kind: 'catalog', catalogType: 'pm.party' }))
      .toEqual({ kind: 'catalog', catalogType: 'pm.party', filters: { is_tenant: 'true' } })
    expect(getLookupHint('pm.lease', 'property_id', { kind: 'catalog', catalogType: 'pm.property' }))
      .toEqual({ kind: 'catalog', catalogType: 'pm.property', filters: { kind: 'Unit' } })
    expect(getLookupHint('other.entity', 'party_id', { kind: 'catalog', catalogType: 'other.party' }))
      .toEqual({ kind: 'catalog', catalogType: 'other.party' })
    expect(getLookupHint('pm.lease', 'source_id', { kind: 'document', documentTypes: ['pm.lease'] }))
      .toEqual({ kind: 'document', documentTypes: ['pm.lease'] })
    expect(getLookupHint('pm.lease', 'account_id', { kind: 'coa' })).toEqual({ kind: 'coa' })
    expect(getLookupHint('pm.lease', 'unknown', { kind: 'future' } as never)).toBeNull()
  })

  it.each([
    ['pm.payable_charge', 'party_id', { kind: 'catalog', catalogType: 'pm.party', filters: { is_vendor: 'true' } }],
    ['pm.payable_payment', 'party_id', { kind: 'catalog', catalogType: 'pm.party', filters: { is_vendor: 'true' } }],
    ['pm.payable_credit_memo', 'party_id', { kind: 'catalog', catalogType: 'pm.party', filters: { is_vendor: 'true' } }],
    ['pm.rent_charge', 'party_id', { kind: 'catalog', catalogType: 'pm.party', filters: { is_tenant: 'true' } }],
    ['pm.receivable_charge', 'party_id', { kind: 'catalog', catalogType: 'pm.party', filters: { is_tenant: 'true' } }],
    ['pm.lease', 'party_id', { kind: 'catalog', catalogType: 'pm.party' }],
    ['pm.lease', 'property_id', { kind: 'catalog', catalogType: 'pm.property', filters: { kind: 'Unit' } }],
    ['pm.work_order', 'property_id', { kind: 'catalog', catalogType: 'pm.property' }],
    ['pm.property', 'parent_property_id', { kind: 'catalog', catalogType: 'pm.property' }],
    ['pm.payable_charge', 'charge_type_id', { kind: 'catalog', catalogType: 'pm.payable_charge_type' }],
    ['pm.payable_credit_memo', 'charge_type_id', { kind: 'catalog', catalogType: 'pm.payable_charge_type' }],
    ['pm.receivable_charge', 'charge_type_id', { kind: 'catalog', catalogType: 'pm.receivable_charge_type' }],
  ])('infers direct hint for %s.%s', (entityType, key, expected) => {
    expect(getLookupHint(entityType, key)).toEqual(expected)
  })

  it.each([
    ['unit_id', { kind: 'catalog', catalogType: 'pm.unit' }],
    ['building_id', { kind: 'catalog', catalogType: 'pm.building' }],
    ['vendor_id', { kind: 'catalog', catalogType: 'pm.vendor' }],
    ['bank_account_id', { kind: 'catalog', catalogType: 'pm.bank_account' }],
    ['payable_charge_type_id', { kind: 'catalog', catalogType: 'pm.payable_charge_type' }],
    ['lease_id', { kind: 'document', documentTypes: ['pm.lease'] }],
  ])('infers suffix hint for %s', (key, expected) => {
    expect(getLookupHint('pm.work_order', key)).toEqual(expected)
  })

  it('infers apply and returned-payment document references', () => {
    expect(getLookupHint('pm.receivable_apply', 'credit_document_id')).toEqual({
      kind: 'document', documentTypes: ['pm.receivable_payment', 'pm.receivable_credit_memo'],
    })
    expect(getLookupHint('pm.receivable_apply', 'charge_document_id')).toEqual({
      kind: 'document', documentTypes: ['pm.receivable_charge', 'pm.late_fee_charge', 'pm.rent_charge'],
    })
    expect(getLookupHint('pm.payable_apply', 'credit_document_id')).toEqual({
      kind: 'document', documentTypes: ['pm.payable_payment', 'pm.payable_credit_memo'],
    })
    expect(getLookupHint('pm.payable_apply', 'charge_document_id')).toEqual({
      kind: 'document', documentTypes: ['pm.payable_charge'],
    })
    expect(getLookupHint('pm.receivable_returned_payment', 'original_payment_id')).toEqual({
      kind: 'document', documentTypes: ['pm.receivable_payment'],
    })
    expect(getLookupHint('pm.receivable_apply', 'unknown_id')).toBeNull()
    expect(getLookupHint('pm.payable_apply', 'unknown_id')).toBeNull()
  })

  it('covers compatibility accounts, non-PM entities, malformed suffixes, and missing hints', () => {
    expect(getLookupHint('other.entity', 'bank_account_id')).toEqual({ kind: 'catalog', catalogType: 'pm.bank_account' })
    expect(getLookupHint('other.entity', 'cash_account_id')).toEqual({ kind: 'coa' })
    expect(getLookupHint('other.entity', 'account_id')).toEqual({ kind: 'coa' })
    expect(getLookupHint('other.entity', 'party_id')).toBeNull()
    expect(getLookupHint('pm.lease', 'notes')).toBeNull()
    expect(getLookupHint(' PM.LEASE ', 'PARTY_ID')).toEqual({ kind: 'catalog', catalogType: 'pm.party' })
  })
})
