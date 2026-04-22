export type RegisterColumn = {
  key: string;
  title: string;
  width?: number;
  minWidth?: number;
  align?: 'left' | 'right' | 'center';
  pinned?: 'left';
  sortable?: boolean;
  wrap?: boolean;
  format?: (value: unknown, row: RegisterDataRow) => string;
};

export type RegisterDataRow = Record<string, unknown> & {
  key: string;
  debit?: number;
  credit?: number;
  __status?: 'active' | 'saved' | 'posted' | 'marked';
  isMarkedForDeletion?: boolean;
  isDeleted?: boolean;
  isActive?: boolean;
};

export type RegisterSortSpec = {
  key: string;
  dir: 'asc' | 'desc';
};

export type DisplayGroupRow = {
  type: 'group';
  key: string;
  groupId: string;
  label: string;
  count: number;
  totalDebit: number;
  totalCredit: number;
};

export type DisplayDataRow = RegisterDataRow & {
  type: 'row';
  __index: number;
};

export type DisplayRow = DisplayGroupRow | DisplayDataRow;

export type RowStatus = 'active' | 'saved' | 'posted' | 'marked';

export function isDisplayDataRow(row: DisplayRow): row is DisplayDataRow {
  return row.type === 'row';
}
