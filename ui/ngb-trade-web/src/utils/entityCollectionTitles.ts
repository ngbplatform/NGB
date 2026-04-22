export function catalogCollectionTitle(catalogType: string, displayName: string): string {
  switch (catalogType) {
    case 'trd.party': return 'Parties'
    case 'trd.item': return 'Items'
    case 'trd.warehouse': return 'Warehouses'
    case 'trd.unit_of_measure': return 'Units of Measure'
    case 'trd.payment_terms': return 'Payment Terms'
    case 'trd.inventory_adjustment_reason': return 'Inventory Adjustment Reasons'
    case 'trd.price_type': return 'Price Types'
    case 'trd.accounting_policy': return 'Accounting Policy'
    default: return displayName
  }
}

export function documentCollectionTitle(documentType: string, displayName: string): string {
  switch (documentType) {
    case 'trd.purchase_receipt': return 'Purchase Receipts'
    case 'trd.sales_invoice': return 'Sales Invoices'
    case 'trd.customer_payment': return 'Customer Payments'
    case 'trd.vendor_payment': return 'Vendor Payments'
    case 'trd.inventory_transfer': return 'Inventory Transfers'
    case 'trd.inventory_adjustment': return 'Inventory Adjustments'
    case 'trd.customer_return': return 'Customer Returns'
    case 'trd.vendor_return': return 'Vendor Returns'
    case 'trd.item_price_update': return 'Item Price Updates'
    default: return displayName
  }
}
