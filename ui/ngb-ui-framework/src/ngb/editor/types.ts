import type { NgbIconName } from '../primitives/iconNames';
import type {
  DocumentStatusValue,
  LookupHint,
  LookupSource,
  LookupStoreApi,
  RecordPayload,
} from '../metadata/types';

export type EditorKind = 'catalog' | 'document';
export type EditorMode = 'page' | 'drawer';
export type EditorChangeReason = 'markForDeletion' | 'unmarkForDeletion' | 'post' | 'unpost';

export type EntityEditorContext = {
  kind: EditorKind;
  typeCode: string;
};

export type EntityEditorFlags = {
  canSave: boolean;
  isDirty: boolean;
  loading: boolean;
  saving: boolean;
  canExpand: boolean;
  canDelete: boolean;
  canMarkForDeletion: boolean;
  canUnmarkForDeletion: boolean;
  canPost: boolean;
  canUnpost: boolean;
  canShowAudit: boolean;
  canShareLink: boolean;
  extras?: Record<string, boolean | undefined>;
};

export type EntityHeaderIconAction = {
  key: string;
  title: string;
  icon: NgbIconName;
  disabled?: boolean;
};

export type DocumentHeaderCoreActionKey =
  | 'openCompactPage'
  | 'openFullPage'
  | 'copyDocument'
  | 'printDocument'
  | 'toggleMarkForDeletion'
  | 'save'
  | 'togglePost'
  | 'openEffectsPage'
  | 'openDocumentFlowPage'
  | 'openAuditLog'
  | 'copyShareLink';

export type DocumentHeaderActionKey = DocumentHeaderCoreActionKey | (string & {});

export type DocumentHeaderActionItem = {
  key: DocumentHeaderActionKey;
  title: string;
  icon: NgbIconName;
  disabled?: boolean;
};

export type DocumentHeaderActionGroup = {
  key: string;
  label: string;
  items: DocumentHeaderActionItem[];
};

export type DocumentRecord = {
  id: string;
  number?: string | null;
  display?: string | null;
  payload: RecordPayload;
  status: DocumentStatusValue;
  isMarkedForDeletion?: boolean;
};

export type RelationshipGraphNode = {
  nodeId: string;
  kind: number;
  typeCode: string;
  entityId: string;
  title: string;
  subtitle?: string | null;
  documentStatus?: DocumentStatusValue | null;
  depth?: number | null;
  amount?: number | null;
};

export type RelationshipGraphEdge = {
  fromNodeId: string;
  toNodeId: string;
  relationshipType: string;
  label?: string | null;
};

export type RelationshipGraph = {
  nodes: RelationshipGraphNode[];
  edges: RelationshipGraphEdge[];
};

export type EffectAccount = {
  accountId: string;
  code: string;
  name: string;
};

export type EffectDimensionValue = {
  dimensionId: string;
  valueId: string;
  display: string;
};

export type EffectResourceValue = {
  code: string;
  value: number;
};

export type AccountingEntryEffect = {
  entryId: string | number;
  documentId?: string | null;
  occurredAtUtc: string;
  debitAccount?: EffectAccount | null;
  creditAccount?: EffectAccount | null;
  debitAccountId?: string | null;
  creditAccountId?: string | null;
  amount: number;
  isStorno?: boolean;
  debitDimensionSetId?: string | null;
  creditDimensionSetId?: string | null;
  debitDimensions?: EffectDimensionValue[] | null;
  creditDimensions?: EffectDimensionValue[] | null;
};

export type OperationalRegisterMovementEffect = {
  registerId?: string | null;
  registerCode: string;
  registerName?: string | null;
  movementId: string | number;
  documentId?: string | null;
  occurredAtUtc: string;
  periodMonth?: string | null;
  isStorno?: boolean;
  dimensionSetId?: string | null;
  dimensions?: EffectDimensionValue[] | null;
  resources: EffectResourceValue[] | Record<string, unknown>;
};

export type ReferenceRegisterWriteEffect = {
  registerId?: string | null;
  registerCode: string;
  registerName?: string | null;
  recordId: string | number;
  documentId?: string | null;
  periodUtc?: string | null;
  periodBucketUtc?: string | null;
  recordedAtUtc: string;
  dimensionSetId?: string | null;
  dimensions?: EffectDimensionValue[] | null;
  fields: Record<string, unknown>;
  isTombstone: boolean;
};

export type DocumentUiActionReason = {
  errorCode: string;
  message: string;
};

export type DocumentUiEffects = {
  isPosted: boolean;
  canEdit: boolean;
  canPost: boolean;
  canUnpost: boolean;
  canRepost: boolean;
  canApply: boolean;
  disabledReasons?: Record<string, DocumentUiActionReason[]> | null;
};

export type DocumentEffects = {
  accountingEntries: AccountingEntryEffect[];
  operationalRegisterMovements: OperationalRegisterMovementEffect[];
  referenceRegisterWrites: ReferenceRegisterWriteEffect[];
  ui?: DocumentUiEffects | null;
};

export type EntityEditorHandle<TDocumentEffects = DocumentEffects | null> = {
  save: () => Promise<void>;
  load: () => Promise<void>;
  openFullPage: () => void;
  openCompactPage: () => void;
  closePage: () => void;
  toggleMarkForDeletion: () => void;
  togglePost: () => void;
  markForDeletion: () => Promise<void>;
  unmarkForDeletion: () => Promise<void>;
  deleteEntity: () => Promise<void>;
  post: () => Promise<void>;
  unpost: () => Promise<void>;
  getDocumentEffects: () => TDocumentEffects;
  reloadDocumentEffects: () => Promise<TDocumentEffects>;
  copyShareLink: () => Promise<void>;
  copyDocument: () => void;
  printDocument: () => void;
  openAuditLog: () => void;
  openAudit: () => void;
  closeAuditLog: () => void;
  getIsDirty: () => boolean;
  getCanSave: () => boolean;
  getFlags: () => EntityEditorFlags;
};

export type AuditFieldChange = {
  fieldPath: string;
  oldValueJson?: string | null;
  newValueJson?: string | null;
};

export type AuditActor = {
  userId?: string | null;
  displayName?: string | null;
  email?: string | null;
};

export type AuditEvent = {
  auditEventId: string;
  entityKind: number;
  entityId: string;
  actionCode: string;
  actor?: AuditActor | null;
  occurredAtUtc: string;
  correlationId?: string | null;
  metadataJson?: string | null;
  changes: AuditFieldChange[];
};

export type AuditCursor = {
  occurredAtUtc: string;
  auditEventId: string;
};

export type AuditLogPage = {
  items: AuditEvent[];
  nextCursor?: AuditCursor | null;
  limit: number;
};

export type EditorAuditLoadOptions = {
  afterOccurredAtUtc?: string | null;
  afterAuditEventId?: string | null;
  limit?: number;
};

export type EditorAuditBehavior = {
  hiddenFieldNames?: string[];
  explicitFieldLabels?: Record<string, string>;
};

export type EditorDocumentEffectsBehavior = {
  prefetchRelatedLabels?: (args: {
    documentType: string;
    documentId: string;
    effects: DocumentEffects;
    lookupStore: LookupStoreApi | null;
  }) => Promise<void> | void;
  resolveAccountLabel?: (args: {
    account: EffectAccount | null | undefined;
    accountId: string | null | undefined;
    lookupStore: LookupStoreApi | null;
  }) => string;
  resolveDimensionDisplay?: (args: {
    item: EffectDimensionValue | null | undefined;
    lookupStore: LookupStoreApi | null;
  }) => string;
  resolveFieldValue?: (args: {
    documentType: string;
    documentId: string;
    document: DocumentRecord | null;
    fieldKey: string;
    value: unknown;
    fields: Record<string, unknown>;
    lookupStore: LookupStoreApi | null;
  }) => string | null | undefined;
};

export type EditorPrintLookupHintArgs = {
  documentType: string;
  fieldKey: string;
  lookup?: LookupSource | null;
};

export type EditorPrintBehavior = {
  resolveLookupHint?: (args: EditorPrintLookupHintArgs) => LookupHint | null;
};
