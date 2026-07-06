export function catalogCollectionTitle(catalogType: string, displayName: string): string {
  switch (catalogType) {
    case 'crm.account': return 'Accounts'
    case 'crm.contact': return 'Contacts'
    case 'crm.product': return 'Products'
    case 'crm.opportunity_stage': return 'Opportunity Stages'
    default: return displayName
  }
}

export function documentCollectionTitle(documentType: string, displayName: string): string {
  switch (documentType) {
    case 'crm.lead_intake': return 'Lead Intakes'
    case 'crm.lead_qualification': return 'Lead Qualifications'
    case 'crm.lead_conversion': return 'Lead Conversions'
    case 'crm.opportunity_update': return 'Opportunity Updates'
    case 'crm.quote': return 'Quotes'
    case 'crm.activity_log': return 'Activity Logs'
    default: return displayName
  }
}
