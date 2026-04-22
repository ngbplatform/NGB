import type {
  PayablesOpenItemsDetailsResponseDto,
  ReceivablesOpenItemsDetailsResponseDto,
} from '../../../ngb-property-management-web/src/api/types/pmContracts'
import type { CatalogItemDto, DocumentDto } from '../../../ngb-ui-framework/src/ngb/api/contracts'
import { PM_TEST_IDS } from '../support/routes'

export const mainMenuFixture = {
  groups: [
    {
      label: 'Home',
      ordinal: 0,
      icon: 'home',
      items: [
        { kind: 'page', code: 'home', label: 'Home', route: '/home', icon: 'home', ordinal: 0 },
      ],
    },
    {
      label: 'Receivables',
      ordinal: 10,
      icon: 'coins',
      items: [
        { kind: 'page', code: 'receivables-open-items', label: 'Receivables', route: '/receivables/open-items', icon: 'coins', ordinal: 0 },
      ],
    },
    {
      label: 'Payables',
      ordinal: 20,
      icon: 'wallet',
      items: [
        { kind: 'page', code: 'payables-open-items', label: 'Payables', route: '/payables/open-items', icon: 'wallet', ordinal: 0 },
      ],
    },
  ],
} as const

export const receivablesLeaseFixture: DocumentDto = {
  id: PM_TEST_IDS.receivablesLeaseId,
  number: 'LEASE-100',
  display: 'Lease 100 - Riverfront',
  payload: { fields: {} },
  status: 'posted',
  isMarkedForDeletion: false,
}

export const payablesVendorFixture: CatalogItemDto = {
  id: PM_TEST_IDS.payableVendorId,
  display: 'Northwind Services LLC',
  payload: { fields: {} },
  isMarkedForDeletion: false,
  isDeleted: false,
}

export const payablesPropertyFixture: CatalogItemDto = {
  id: PM_TEST_IDS.payablePropertyId,
  display: 'Maple Court',
  payload: { fields: {} },
  isMarkedForDeletion: false,
  isDeleted: false,
}

export const receivablesOpenItemsFixture: ReceivablesOpenItemsDetailsResponseDto = {
  registerId: '77777777-7777-4777-8777-777777777777',
  partyId: PM_TEST_IDS.receivablesPartyId,
  partyDisplay: 'Alex Tenant',
  propertyId: PM_TEST_IDS.receivablesPropertyId,
  propertyDisplay: 'Riverfront Flats',
  leaseId: PM_TEST_IDS.receivablesLeaseId,
  leaseDisplay: 'Lease 100 - Riverfront',
  charges: [
    {
      chargeDocumentId: '88888888-8888-4888-8888-888888888881',
      documentType: 'pm.rent_charge',
      number: 'RC-1001',
      chargeDisplay: 'April Rent',
      dueOnUtc: '2026-04-01',
      chargeTypeDisplay: 'Rent',
      memo: 'April base rent',
      originalAmount: 2400,
      outstandingAmount: 1200,
    },
    {
      chargeDocumentId: '88888888-8888-4888-8888-888888888882',
      documentType: 'pm.late_fee_charge',
      number: 'LF-1001',
      chargeDisplay: 'Late Fee',
      dueOnUtc: '2026-04-05',
      chargeTypeDisplay: 'Late Fee',
      memo: 'Late fee for April',
      originalAmount: 75,
      outstandingAmount: 75,
    },
  ],
  credits: [
    {
      creditDocumentId: '99999999-9999-4999-8999-999999999991',
      documentType: 'pm.receivable_payment',
      number: 'PMT-2001',
      creditDocumentDisplay: 'ACH Payment',
      receivedOnUtc: '2026-04-06',
      memo: 'ACH from tenant',
      originalAmount: 1300,
      availableCredit: 625,
    },
  ],
  allocations: [
    {
      applyId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
      applyDisplay: 'Apply 2001',
      applyNumber: 'AP-2001',
      creditDocumentId: '99999999-9999-4999-8999-999999999991',
      creditDocumentType: 'pm.receivable_payment',
      creditDocumentDisplay: 'ACH Payment',
      creditDocumentNumber: 'PMT-2001',
      chargeDocumentId: '88888888-8888-4888-8888-888888888881',
      chargeDocumentType: 'pm.rent_charge',
      chargeDisplay: 'April Rent',
      chargeNumber: 'RC-1001',
      appliedOnUtc: '2026-04-06',
      amount: 675,
      isPosted: true,
    },
  ],
  totalOutstanding: 1275,
  totalCredit: 625,
}

export const payablesOpenItemsFixture: PayablesOpenItemsDetailsResponseDto = {
  registerId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
  vendorId: PM_TEST_IDS.payableVendorId,
  vendorDisplay: 'Northwind Services LLC',
  propertyId: PM_TEST_IDS.payablePropertyId,
  propertyDisplay: 'Maple Court',
  charges: [
    {
      chargeDocumentId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc1',
      documentType: 'pm.payable_charge',
      number: 'BILL-4100',
      chargeDisplay: 'Roof repair',
      dueOnUtc: '2026-04-10',
      chargeTypeDisplay: 'Vendor Bill',
      vendorInvoiceNo: 'INV-4100',
      memo: 'Roof patch and labor',
      originalAmount: 3200,
      outstandingAmount: 1800,
    },
  ],
  credits: [
    {
      creditDocumentId: 'dddddddd-dddd-4ddd-8ddd-ddddddddddd1',
      documentType: 'pm.payable_payment',
      number: 'CHK-7100',
      creditDocumentDisplay: 'Check 7100',
      creditDocumentDateUtc: '2026-04-07',
      memo: 'Partial payment',
      originalAmount: 2000,
      availableCredit: 200,
    },
  ],
  allocations: [
    {
      applyId: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee1',
      applyDisplay: 'Apply 7100',
      applyNumber: 'AP-7100',
      creditDocumentId: 'dddddddd-dddd-4ddd-8ddd-ddddddddddd1',
      creditDocumentType: 'pm.payable_payment',
      creditDocumentDisplay: 'Check 7100',
      creditDocumentNumber: 'CHK-7100',
      chargeDocumentId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc1',
      chargeDocumentType: 'pm.payable_charge',
      chargeDisplay: 'Roof repair',
      chargeNumber: 'BILL-4100',
      appliedOnUtc: '2026-04-07',
      amount: 1800,
      isPosted: true,
    },
  ],
  totalOutstanding: 1800,
  totalCredit: 200,
}
