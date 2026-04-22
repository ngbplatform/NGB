import { executeReport, ReportRowKind, type ReportExecutionResponseDto } from 'ngb-ui-framework'

export type PmBuildingSummaryDto = {
  buildingDisplay: string
  asOfUtc: string
  totalUnits: number
  occupiedUnits: number
  vacantUnits: number
  vacancyPercent: number
}

function cellDisplay(response: ReportExecutionResponseDto, rowIndex: number, cellIndex: number): string {
  return String(response.sheet.rows[rowIndex]?.cells[cellIndex]?.display ?? '')
}

function cellNumber(response: ReportExecutionResponseDto, rowIndex: number, cellIndex: number): number {
  const raw = response.sheet.rows[rowIndex]?.cells[cellIndex]?.value
  return Number(raw ?? 0) || 0
}

function isDetailLikeRowKind(rowKind: unknown): boolean {
  return rowKind === ReportRowKind.Detail
    || rowKind === 'Detail'
    || rowKind === 'detail'
}

function isTotalLikeRowKind(rowKind: unknown): boolean {
  return rowKind === ReportRowKind.Total
    || rowKind === 'Total'
    || rowKind === 'total'
    || rowKind === ReportRowKind.Subtotal
    || rowKind === 'Subtotal'
    || rowKind === 'subtotal'
}

export async function getPmBuildingSummary(buildingId: string, opts?: { asOfUtc?: string }): Promise<PmBuildingSummaryDto> {
  const response = await executeReport('pm.building.summary', {
    filters: {
      building_id: {
        value: buildingId,
      },
    },
    parameters: opts?.asOfUtc ? { as_of_utc: opts.asOfUtc } : undefined,
    limit: 2,
    offset: 0,
  })

  const rows = response.sheet.rows ?? []
  const detailRowIndex = rows.findIndex((row) => isDetailLikeRowKind(row.rowKind))
  const firstDataRowIndex = detailRowIndex >= 0
    ? detailRowIndex
    : rows.findIndex((row) => !isTotalLikeRowKind(row.rowKind))

  if (firstDataRowIndex < 0 || rows[firstDataRowIndex].cells.length < 6) {
    throw new Error('pm.building.summary: unexpected response')
  }

  return {
    buildingDisplay: cellDisplay(response, firstDataRowIndex, 0),
    asOfUtc: cellDisplay(response, firstDataRowIndex, 1),
    totalUnits: cellNumber(response, firstDataRowIndex, 2),
    occupiedUnits: cellNumber(response, firstDataRowIndex, 3),
    vacantUnits: cellNumber(response, firstDataRowIndex, 4),
    vacancyPercent: cellNumber(response, firstDataRowIndex, 5),
  }
}
