export interface NgbRequestTags {
  readonly app?: 'ngb';
  readonly vertical?: string;
  readonly profile?: string;
  readonly area?: string;
  readonly operation?: string;
  readonly scenario?: string;
  readonly documentType?: string;
  readonly reportId?: string;
  readonly catalogType?: string;
  readonly entityKind?: string;
  readonly periodProfile?: string;
  readonly status?: string;
}

export function buildTags(tags: NgbRequestTags): Record<string, string> {
  const normalized: Record<string, string> = {
    app: tags.app ?? 'ngb',
  };

  addTag(normalized, 'vertical', tags.vertical);
  addTag(normalized, 'profile', tags.profile);
  addTag(normalized, 'area', tags.area);
  addTag(normalized, 'operation', tags.operation);
  addTag(normalized, 'scenario', tags.scenario);
  addTag(normalized, 'documentType', tags.documentType);
  addTag(normalized, 'reportId', tags.reportId);
  addTag(normalized, 'catalogType', tags.catalogType);
  addTag(normalized, 'entityKind', tags.entityKind);
  addTag(normalized, 'periodProfile', tags.periodProfile);
  addTag(normalized, 'status', tags.status);

  return normalized;
}

export function mergeTags(...items: Array<NgbRequestTags | undefined>): NgbRequestTags {
  return items.reduce<NgbRequestTags>((merged, item) => ({ ...merged, ...(item ?? {}) }), {});
}

function addTag(target: Record<string, string>, name: string, value: string | undefined): void {
  const trimmed = value?.trim();
  if (trimmed) {
    target[name] = trimmed;
  }
}
