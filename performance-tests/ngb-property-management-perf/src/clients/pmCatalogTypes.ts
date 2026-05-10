export const PM_CATALOG_TYPES = {
  party: 'pm.party',
  property: 'pm.property',
  bankAccount: 'pm.bank_account',
  maintenanceCategory: 'pm.maintenance_category',
  receivableChargeType: 'pm.receivable_charge_type',
  payableChargeType: 'pm.payable_charge_type',
} as const;

export type PmCatalogType = typeof PM_CATALOG_TYPES[keyof typeof PM_CATALOG_TYPES];
