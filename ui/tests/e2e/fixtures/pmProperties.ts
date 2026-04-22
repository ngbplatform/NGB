import type { CatalogItemDto, PageResponseDto } from '../../../ngb-ui-framework/src/ngb/api/contracts'
import { ReportRowKind, type ReportExecutionResponseDto } from '../../../ngb-ui-framework/src/ngb/reporting/types'
import { PM_TEST_IDS } from '../support/routes'

export const propertyBuildingsFixture: PageResponseDto<CatalogItemDto> = {
  items: [
    {
      id: PM_TEST_IDS.propertyBuildingId,
      display: 'Riverfront Tower',
      payload: {
        fields: {
          kind: 'Building',
          address_line1: '185 Grand Ave',
          city: 'Jersey City',
          state: 'NJ',
          zip: '07302',
        },
      },
      isMarkedForDeletion: false,
      isDeleted: false,
    },
    {
      id: '12121212-3434-4545-8686-909090909090',
      display: 'Harbor View Plaza',
      payload: {
        fields: {
          kind: 'Building',
          address_line1: '12 Harbor View Dr',
          city: 'Hoboken',
          state: 'NJ',
          zip: '07030',
        },
      },
      isMarkedForDeletion: false,
      isDeleted: false,
    },
  ],
  offset: 0,
  limit: 50,
  total: 2,
}

export const propertyUnitsFixture: PageResponseDto<CatalogItemDto> = {
  items: [
    {
      id: PM_TEST_IDS.propertyUnitAId,
      display: 'Unit 4A',
      payload: {
        fields: {
          kind: 'Unit',
          unit_no: '4A',
          parent_property_id: PM_TEST_IDS.propertyBuildingId,
        },
      },
      isMarkedForDeletion: false,
      isDeleted: false,
    },
    {
      id: PM_TEST_IDS.propertyUnitBId,
      display: 'Unit 4B',
      payload: {
        fields: {
          kind: 'Unit',
          unit_no: '4B',
          parent_property_id: PM_TEST_IDS.propertyBuildingId,
        },
      },
      isMarkedForDeletion: false,
      isDeleted: false,
    },
  ],
  offset: 0,
  limit: 50,
  total: 2,
}

export const propertyBuildingSummaryFixture: ReportExecutionResponseDto = {
  sheet: {
    columns: [
      { code: 'building', title: 'Building', dataType: 'string' },
      { code: 'as_of', title: 'As of', dataType: 'string' },
      { code: 'total_units', title: 'Total Units', dataType: 'number' },
      { code: 'occupied_units', title: 'Occupied Units', dataType: 'number' },
      { code: 'vacant_units', title: 'Vacant Units', dataType: 'number' },
      { code: 'vacancy_percent', title: 'Vacancy %', dataType: 'number' },
    ],
    rows: [
      {
        rowKind: ReportRowKind.Detail,
        cells: [
          { value: 'Riverfront Tower', display: 'Riverfront Tower', valueType: 'string' },
          { value: '2026-04-07', display: '2026-04-07', valueType: 'string' },
          { value: 24, display: '24', valueType: 'number' },
          { value: 19, display: '19', valueType: 'number' },
          { value: 5, display: '5', valueType: 'number' },
          { value: 20.83, display: '20.83', valueType: 'number' },
        ],
      },
    ],
    meta: {
      title: 'Building Summary',
    },
  },
  offset: 0,
  limit: 2,
  total: 1,
  hasMore: false,
}
