import { httpDelete, httpGet, httpPost, httpPostFile, httpPostNativeDownload, httpPut, type HttpRequestOptions, type HttpFileRequestOptions } from '../api/http'
import type { ReportDefinitionDto, ReportExecutionRequestDto, ReportExecutionResponseDto, ReportExportRequestDto, ReportVariantDto } from './types'

export async function getReportDefinitions(): Promise<ReportDefinitionDto[]> {
  return await httpGet<ReportDefinitionDto[]>('/api/report-definitions')
}

export async function getReportDefinition(reportCode: string): Promise<ReportDefinitionDto> {
  return await httpGet<ReportDefinitionDto>(`/api/report-definitions/${encodeURIComponent(reportCode)}`)
}

export async function executeReport(
  reportCode: string,
  request: ReportExecutionRequestDto,
  options?: HttpRequestOptions,
): Promise<ReportExecutionResponseDto> {
  const url = `/api/reports/${encodeURIComponent(reportCode)}/execute`
  return options
    ? await httpPost<ReportExecutionResponseDto>(url, request, options)
    : await httpPost<ReportExecutionResponseDto>(url, request)
}

export async function exportReportXlsx(reportCode: string, request: ReportExportRequestDto, options?: HttpFileRequestOptions): Promise<{ blob: Blob; fileName: string | null }> {
  const url = `/api/reports/${encodeURIComponent(reportCode)}/export/xlsx`
  const response = options ? await httpPostFile(url, request, options) : await httpPostFile(url, request)
  return { blob: response.blob, fileName: response.fileName }
}

export async function getReportVariants(reportCode: string): Promise<ReportVariantDto[]> {
  return await httpGet<ReportVariantDto[]>(`/api/reports/${encodeURIComponent(reportCode)}/variants`)
}

export async function getReportVariant(reportCode: string, variantCode: string): Promise<ReportVariantDto> {
  return await httpGet<ReportVariantDto>(`/api/reports/${encodeURIComponent(reportCode)}/variants/${encodeURIComponent(variantCode)}`)
}

export async function saveReportVariant(reportCode: string, variantCode: string, variant: ReportVariantDto): Promise<ReportVariantDto> {
  return await httpPut<ReportVariantDto>(`/api/reports/${encodeURIComponent(reportCode)}/variants/${encodeURIComponent(variantCode)}`, variant)
}

export async function deleteReportVariant(reportCode: string, variantCode: string): Promise<void> {
  await httpDelete<void>(`/api/reports/${encodeURIComponent(reportCode)}/variants/${encodeURIComponent(variantCode)}`)
}

export function exportReportXlsxInBrowser(reportCode: string, request: ReportExportRequestDto, options?: Parameters<typeof httpPostNativeDownload>[2]): Promise<void> {
  return httpPostNativeDownload(`/api/reports/${encodeURIComponent(reportCode)}/export/xlsx/form`, request, options)
}
