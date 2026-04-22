import type { LookupItem, LookupSource } from '../metadata/types';

export type GeneralJournalEntryDimensionValueDto = {
  dimensionId: string;
  valueId: string;
  display?: string | null;
};

export type GeneralJournalEntryLineDto = {
  lineNo: number;
  side: number;
  accountId: string;
  accountDisplay?: string | null;
  amount: number;
  memo?: string | null;
  dimensionSetId: string;
  dimensions: GeneralJournalEntryDimensionValueDto[];
};

export type GeneralJournalEntryAllocationDto = {
  entryNo: number;
  debitLineNo: number;
  creditLineNo: number;
  amount: number;
};

export type GeneralJournalEntryHeaderDto = {
  journalType: number;
  source: number;
  approvalState: number;
  reasonCode?: string | null;
  memo?: string | null;
  externalReference?: string | null;
  autoReverse: boolean;
  autoReverseOnUtc?: string | null;
  reversalOfDocumentId?: string | null;
  reversalOfDocumentDisplay?: string | null;
  initiatedBy?: string | null;
  initiatedAtUtc?: string | null;
  submittedBy?: string | null;
  submittedAtUtc?: string | null;
  approvedBy?: string | null;
  approvedAtUtc?: string | null;
  rejectedBy?: string | null;
  rejectedAtUtc?: string | null;
  rejectReason?: string | null;
  postedBy?: string | null;
  postedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type GeneralJournalEntryListItemDto = {
  id: string;
  dateUtc: string;
  number?: string | null;
  display?: string | null;
  documentStatus: number | string;
  isMarkedForDeletion: boolean;
  journalType: number;
  source: number;
  approvalState: number;
  reasonCode?: string | null;
  memo?: string | null;
  externalReference?: string | null;
  autoReverse: boolean;
  autoReverseOnUtc?: string | null;
  reversalOfDocumentId?: string | null;
  postedBy?: string | null;
  postedAtUtc?: string | null;
};

export type GeneralJournalEntryPageDto = {
  items: GeneralJournalEntryListItemDto[];
  offset: number;
  limit: number;
  total?: number | null;
};

export type GeneralJournalEntryDocumentDto = {
  id: string;
  display?: string | null;
  status: number | string;
  isMarkedForDeletion: boolean;
  number?: string | null;
};

export type GeneralJournalEntryDetailsDto = {
  document: GeneralJournalEntryDocumentDto;
  dateUtc: string;
  header: GeneralJournalEntryHeaderDto;
  lines: GeneralJournalEntryLineDto[];
  allocations: GeneralJournalEntryAllocationDto[];
  accountContexts?: GeneralJournalEntryAccountContextDto[];
};

export type GeneralJournalEntryDimensionRuleDto = {
  dimensionId: string;
  dimensionCode: string;
  ordinal: number;
  isRequired: boolean;
  lookup?: LookupSource | null;
};

export type GeneralJournalEntryAccountContextDto = {
  accountId: string;
  code: string;
  name: string;
  dimensionRules: GeneralJournalEntryDimensionRuleDto[];
};

export type CreateGeneralJournalEntryDraftRequestDto = {
  dateUtc: string;
};

export type UpdateGeneralJournalEntryHeaderRequestDto = {
  updatedBy: string;
  journalType?: number | null;
  reasonCode?: string | null;
  memo?: string | null;
  externalReference?: string | null;
  autoReverse?: boolean | null;
  autoReverseOnUtc?: string | null;
};

export type GeneralJournalEntryLineInputDto = {
  side: number;
  accountId: string;
  amount: number;
  memo?: string | null;
  dimensions?: GeneralJournalEntryDimensionValueDto[] | null;
};

export type ReplaceGeneralJournalEntryLinesRequestDto = {
  updatedBy: string;
  lines: GeneralJournalEntryLineInputDto[];
};

export type GeneralJournalEntrySubmitRequestDto = Record<string, never>;
export type GeneralJournalEntryApproveRequestDto = Record<string, never>;
export type GeneralJournalEntryPostRequestDto = Record<string, never>;
export type GeneralJournalEntryRejectRequestDto = { rejectReason: string };
export type GeneralJournalEntryReverseRequestDto = {
  reversalDateUtc: string;
  postImmediately?: boolean;
};

export type GeneralJournalEntryEditorLineModel = {
  clientKey: string;
  side: number;
  account: LookupItem | null;
  amount: string;
  memo: string;
  dimensions: Record<string, LookupItem | null>;
};
