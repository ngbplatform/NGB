import { httpGet, httpPost, httpPut } from '../api/http';
import type {
  CreateGeneralJournalEntryDraftRequestDto,
  GeneralJournalEntryAccountContextDto,
  GeneralJournalEntryApproveRequestDto,
  GeneralJournalEntryDetailsDto,
  GeneralJournalEntryPageDto,
  GeneralJournalEntryPostRequestDto,
  GeneralJournalEntryRejectRequestDto,
  GeneralJournalEntryReverseRequestDto,
  GeneralJournalEntrySubmitRequestDto,
  ReplaceGeneralJournalEntryLinesRequestDto,
  UpdateGeneralJournalEntryHeaderRequestDto,
} from './generalJournalEntryTypes';

const base = '/api/accounting/general-journal-entries';

export type GetGeneralJournalEntryPageArgs = {
  offset?: number;
  limit?: number;
  search?: string | null;
  dateFrom?: string | null;
  dateTo?: string | null;
  trash?: 'active' | 'deleted' | 'all' | null;
};

export async function getGeneralJournalEntryPage(
  args: GetGeneralJournalEntryPageArgs,
): Promise<GeneralJournalEntryPageDto> {
  const q = new URLSearchParams();
  q.set('offset', String(args.offset ?? 0));
  q.set('limit', String(args.limit ?? 50));
  if (args.search) q.set('search', args.search);
  if (args.dateFrom) q.set('dateFrom', args.dateFrom);
  if (args.dateTo) q.set('dateTo', args.dateTo);
  if (args.trash) q.set('trash', args.trash);
  const qs = q.toString();
  return await httpGet<GeneralJournalEntryPageDto>(`${base}${qs ? `?${qs}` : ''}`);
}

export async function getGeneralJournalEntry(id: string): Promise<GeneralJournalEntryDetailsDto> {
  return await httpGet<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}`);
}

export async function createGeneralJournalEntryDraft(
  request: CreateGeneralJournalEntryDraftRequestDto,
): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPost<GeneralJournalEntryDetailsDto>(base, request);
}

export async function updateGeneralJournalEntryHeader(
  id: string,
  request: UpdateGeneralJournalEntryHeaderRequestDto,
): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPut<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/header`, request);
}

export async function replaceGeneralJournalEntryLines(
  id: string,
  request: ReplaceGeneralJournalEntryLinesRequestDto,
): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPut<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/lines`, request);
}

export async function submitGeneralJournalEntry(
  id: string,
  request: GeneralJournalEntrySubmitRequestDto,
): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPost<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/submit`, request);
}

export async function approveGeneralJournalEntry(
  id: string,
  request: GeneralJournalEntryApproveRequestDto,
): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPost<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/approve`, request);
}

export async function rejectGeneralJournalEntry(
  id: string,
  request: GeneralJournalEntryRejectRequestDto,
): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPost<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/reject`, request);
}

export async function postGeneralJournalEntry(
  id: string,
  request: GeneralJournalEntryPostRequestDto,
): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPost<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/post`, request);
}

export async function reverseGeneralJournalEntry(
  id: string,
  request: GeneralJournalEntryReverseRequestDto,
): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPost<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/reverse`, request);
}

export async function getGeneralJournalEntryAccountContext(
  accountId: string,
): Promise<GeneralJournalEntryAccountContextDto> {
  return await httpGet<GeneralJournalEntryAccountContextDto>(`${base}/accounts/${encodeURIComponent(accountId)}`);
}

export async function markGeneralJournalEntryForDeletion(id: string): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPost<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/mark-for-deletion`);
}

export async function unmarkGeneralJournalEntryForDeletion(id: string): Promise<GeneralJournalEntryDetailsDto> {
  return await httpPost<GeneralJournalEntryDetailsDto>(`${base}/${encodeURIComponent(id)}/unmark-for-deletion`);
}
