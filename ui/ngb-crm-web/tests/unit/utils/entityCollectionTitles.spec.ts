import { describe, expect, it } from 'vitest'

import { catalogCollectionTitle, documentCollectionTitle } from '../../../src/utils/entityCollectionTitles'

describe('CRM collection titles', () => {
  it('uses CRM plural labels for catalogs and documents', () => {
    expect(catalogCollectionTitle('crm.account', 'Account')).toBe('Accounts')
    expect(catalogCollectionTitle('crm.opportunity_stage', 'Opportunity Stage')).toBe('Opportunity Stages')
    expect(documentCollectionTitle('crm.lead_conversion', 'Lead Conversion')).toBe('Lead Conversions')
    expect(documentCollectionTitle('crm.quote', 'Quote')).toBe('Quotes')
  })
})
