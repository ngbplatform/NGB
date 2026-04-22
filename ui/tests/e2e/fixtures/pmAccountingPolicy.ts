import type {
  CatalogItemDto,
  CatalogTypeMetadataDto,
  PageResponseDto,
} from '../../../ngb-ui-framework/src/ngb/api/contracts'
import { PM_TEST_IDS } from '../support/routes'

export const accountingPolicyMetadataFixture: CatalogTypeMetadataDto = {
  catalogType: 'pm.accounting_policy',
  displayName: 'Accounting Policy',
  kind: 2,
  form: {
    sections: [
      {
        title: 'Main',
        rows: [
          {
            fields: [
              {
                key: 'display',
                label: 'Display',
                dataType: 'String',
                uiControl: 1,
                isRequired: true,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'cash_account_id',
                label: 'Cash account',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'ar_tenants_account_id',
                label: 'AR account',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'ap_vendors_account_id',
                label: 'AP account',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'rent_income_account_id',
                label: 'Rent income',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'late_fee_income_account_id',
                label: 'Late fee income',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'tenant_balances_register_id',
                label: 'Tenant balances register',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'receivables_open_items_register_id',
                label: 'Receivables open items register',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'payables_open_items_register_id',
                label: 'Payables open items register',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
        ],
      },
    ],
  },
}

export const accountingPolicyPageFixture: PageResponseDto<CatalogItemDto> = {
  items: [
    {
      id: PM_TEST_IDS.accountingPolicyId,
      display: 'Accounting Policy',
      payload: {
        fields: {
          display: 'Accounting Policy',
          cash_account_id: '1110 Cash Control',
          ar_tenants_account_id: '1200 Tenant A/R',
          ap_vendors_account_id: '2100 Vendor A/P',
          rent_income_account_id: '4100 Rental Income',
          late_fee_income_account_id: '4205 Late Fee Income',
          tenant_balances_register_id: 'tenant-balances',
          receivables_open_items_register_id: 'receivables-open-items',
          payables_open_items_register_id: 'payables-open-items',
        },
      },
      isMarkedForDeletion: false,
      isDeleted: false,
    },
  ],
  offset: 0,
  limit: 1,
  total: 1,
}
