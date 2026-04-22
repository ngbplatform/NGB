export type ChartOfAccountsAccountDto = {
  accountId: string;
  code: string;
  name: string;
  accountType: string;
  cashFlowRole?: string | null;
  cashFlowLineCode?: string | null;
  isActive: boolean;
  isDeleted: boolean;
  isMarkedForDeletion: boolean;
};

export type ChartOfAccountsOptionDto = {
  value: string;
  label: string;
};

export type ChartOfAccountsCashFlowRoleOptionDto = {
  value: string;
  label: string;
  supportsLineCode: boolean;
  requiresLineCode: boolean;
};

export type ChartOfAccountsCashFlowLineOptionDto = {
  value: string;
  label: string;
  section: string;
  allowedRoles: string[];
};

export type ChartOfAccountsMetadataDto = {
  accountTypeOptions: ChartOfAccountsOptionDto[];
  cashFlowRoleOptions: ChartOfAccountsCashFlowRoleOptionDto[];
  cashFlowLineOptions: ChartOfAccountsCashFlowLineOptionDto[];
};

export type ChartOfAccountsPageDto = {
  items: ChartOfAccountsAccountDto[];
  offset: number;
  limit: number;
  total?: number | null;
};

export type ChartOfAccountsUpsertRequestDto = {
  code: string;
  name: string;
  accountType: string;
  isActive: boolean;
  cashFlowRole?: string | null;
  cashFlowLineCode?: string | null;
};

export type ChartOfAccountEditorShellState = {
  hideHeader: boolean;
  flushBody: boolean;
};
