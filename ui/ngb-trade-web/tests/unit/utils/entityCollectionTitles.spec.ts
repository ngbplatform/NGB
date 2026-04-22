import { describe, expect, it } from 'vitest'

import { catalogCollectionTitle, documentCollectionTitle } from '../../../src/utils/entityCollectionTitles'

describe('trade collection titles', () => {
  it.each([
    ['trd.party', 'Party', 'Parties'],
    ['trd.item', 'Item', 'Items'],
    ['trd.warehouse', 'Warehouse', 'Warehouses'],
    ['trd.unit_of_measure', 'Unit of Measure', 'Units of Measure'],
    ['trd.payment_terms', 'Payment Term', 'Payment Terms'],
    ['trd.inventory_adjustment_reason', 'Inventory Adjustment Reason', 'Inventory Adjustment Reasons'],
    ['trd.price_type', 'Price Type', 'Price Types'],
    ['trd.accounting_policy', 'Accounting Policy', 'Accounting Policy'],
  ])('maps catalog collection title for %s', (catalogType, displayName, expectedTitle) => {
    expect(catalogCollectionTitle(String(catalogType), String(displayName))).toBe(expectedTitle)
  })

  it('falls back to metadata display name for unknown catalog types', () => {
    expect(catalogCollectionTitle('trd.unknown', 'Custom Catalog')).toBe('Custom Catalog')
  })

  it.each([
    ['trd.purchase_receipt', 'Purchase Receipt', 'Purchase Receipts'],
    ['trd.sales_invoice', 'Sales Invoice', 'Sales Invoices'],
    ['trd.customer_payment', 'Customer Payment', 'Customer Payments'],
    ['trd.vendor_payment', 'Vendor Payment', 'Vendor Payments'],
    ['trd.inventory_transfer', 'Inventory Transfer', 'Inventory Transfers'],
    ['trd.inventory_adjustment', 'Inventory Adjustment', 'Inventory Adjustments'],
    ['trd.customer_return', 'Customer Return', 'Customer Returns'],
    ['trd.vendor_return', 'Vendor Return', 'Vendor Returns'],
    ['trd.item_price_update', 'Item Price Update', 'Item Price Updates'],
  ])('maps document collection title for %s', (documentType, displayName, expectedTitle) => {
    expect(documentCollectionTitle(String(documentType), String(displayName))).toBe(expectedTitle)
  })

  it('falls back to metadata display name for unknown document types', () => {
    expect(documentCollectionTitle('trd.unknown_document', 'Custom Document')).toBe('Custom Document')
  })
})
