import { describe, expect, it } from 'vitest'

import { catalogCollectionTitle, documentCollectionTitle } from '../../../src/utils/entityCollectionTitles'

describe('CRM collection titles', () => {
  it('uses CRM plural labels for catalogs and documents', () => {
    const catalogs = [
      ['crm.account', 'Accounts'], ['crm.contact', 'Contacts'], ['crm.product', 'Products'],
      ['crm.opportunity_stage', 'Opportunity Stages'], ['unknown', 'Fallback'],
    ]
    const documents = [
      ['crm.lead_intake', 'Lead Intakes'], ['crm.lead_qualification', 'Lead Qualifications'],
      ['crm.lead_conversion', 'Lead Conversions'], ['crm.opportunity_update', 'Opportunity Updates'],
      ['crm.quote', 'Quotes'], ['crm.activity_log', 'Activity Logs'], ['unknown', 'Fallback'],
    ]
    for (const [typeCode, expected] of catalogs) expect(catalogCollectionTitle(typeCode!, 'Fallback')).toBe(expected)
    for (const [typeCode, expected] of documents) expect(documentCollectionTitle(typeCode!, 'Fallback')).toBe(expected)
  })
})
