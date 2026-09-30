import { httpGet, httpPost, httpPut, type HttpRequestOptions } from '../api/http';
import type { ByIdsRequestDto, LookupItemDto } from '../api/contracts';
import type {
  ChartOfAccountsAccountDto,
  ChartOfAccountsMetadataDto,
  ChartOfAccountsPageDto,
  ChartOfAccountsUpsertRequestDto,
} from './types';

export type GetChartOfAccountsPageArgs = {
  offset?: number;
  limit?: number;
  search?: string | null;
  onlyActive?: boolean | null;
  includeDeleted?: boolean;
  onlyDeleted?: boolean | null;
};

export async function getChartOfAccountsPage(
  args: GetChartOfAccountsPageArgs,
  options?: HttpRequestOptions,
): Promise<ChartOfAccountsPageDto> {
  const q = new URLSearchParams();
  q.set('offset', String(args.offset ?? 0));
  q.set('limit', String(args.limit ?? 20));
  if (args.search) q.set('search', args.search);

  if (args.includeDeleted != null) q.set('includeDeleted', String(args.includeDeleted));
  if (args.onlyActive != null) q.set('onlyActive', String(args.onlyActive));
  if (args.onlyDeleted != null) q.set('onlyDeleted', String(args.onlyDeleted));

  const url = `/api/chart-of-accounts?${q.toString()}`;
  return options
    ? await httpGet<ChartOfAccountsPageDto>(url, null, options)
    : await httpGet<ChartOfAccountsPageDto>(url);
}

export async function getChartOfAccountsMetadata(options?: HttpRequestOptions): Promise<ChartOfAccountsMetadataDto> {
  return options
    ? await httpGet<ChartOfAccountsMetadataDto>('/api/chart-of-accounts/metadata', undefined, options)
    : await httpGet<ChartOfAccountsMetadataDto>('/api/chart-of-accounts/metadata');
}

export async function getChartOfAccountById(accountId: string, options?: HttpRequestOptions): Promise<ChartOfAccountsAccountDto> {
  const url = `/api/chart-of-accounts/${encodeURIComponent(accountId)}`;
  return options
    ? await httpGet<ChartOfAccountsAccountDto>(url, undefined, options)
    : await httpGet<ChartOfAccountsAccountDto>(url);
}

export async function getChartOfAccountsByIds(ids: string[]): Promise<LookupItemDto[]> {
  const request: ByIdsRequestDto = { ids }
  return await httpPost<LookupItemDto[]>('/api/chart-of-accounts/by-ids', request);
}

export async function createChartOfAccount(
  request: ChartOfAccountsUpsertRequestDto,
): Promise<ChartOfAccountsAccountDto> {
  return await httpPost<ChartOfAccountsAccountDto>('/api/chart-of-accounts', request);
}

export async function updateChartOfAccount(
  accountId: string,
  request: ChartOfAccountsUpsertRequestDto,
): Promise<ChartOfAccountsAccountDto> {
  return await httpPut<ChartOfAccountsAccountDto>(
    `/api/chart-of-accounts/${encodeURIComponent(accountId)}`,
    request,
  );
}

export async function markChartOfAccountForDeletion(accountId: string): Promise<void> {
  await httpPost<void>(`/api/chart-of-accounts/${encodeURIComponent(accountId)}/mark-for-deletion`);
}

export async function unmarkChartOfAccountForDeletion(accountId: string): Promise<void> {
  await httpPost<void>(`/api/chart-of-accounts/${encodeURIComponent(accountId)}/unmark-for-deletion`);
}

export async function setChartOfAccountActive(
  accountId: string,
  isActive: boolean,
): Promise<void> {
  await httpPost<void>(
    `/api/chart-of-accounts/${encodeURIComponent(accountId)}/set-active?isActive=${encodeURIComponent(String(isActive))}`,
  );
}
