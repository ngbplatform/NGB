import { check, fail } from 'k6';
import { Counter } from 'k6/metrics';
import exec from 'k6/execution';

import { documentListFlow } from '../../../ngb-performance-tests-framework/src/flows/documentListFlow.ts';
import { accountingEffectsFlow } from '../../../ngb-performance-tests-framework/src/flows/accountingEffectsFlow.ts';
import { documentFlowReadFlow } from '../../../ngb-performance-tests-framework/src/flows/documentFlowReadFlow.ts';
import type { NgbHttpResponse } from '../../../ngb-performance-tests-framework/src/core/httpClient.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';
import { postingEnabled, responseDocumentId } from './pmFlowSupport.ts';

const posts = new Counter('ngb_pm_posting_succeeded');
const branches = new Counter('ngb_pm_posting_branch');
const documentType = PM_DOCUMENT_TYPES.rentCharge;

interface PostingCommand {
  readonly documentId: string;
  readonly expectedVersion: number;
  readonly key: string;
  executionId?: string;
  resultVersion?: number;
}

// k6 module state is private to each VU. Replay reuses the exact request body and key.
let templateFields: Record<string, unknown> | undefined;
let pending: PostingCommand | undefined;

export function pmRentChargePostingFlow(context: NgbScenarioContext): boolean {
  const fixtureId = __ENV.NGB_PM_FIXTURE_RENT_CHARGE_ID?.trim() || null;
  documentListFlow(context, documentType);
  if (!postingEnabled(context)) {
    branches.add(1, { branch: 'read_only' });
    accountingEffectsFlow(context, documentType, fixtureId);
    documentFlowReadFlow(context, documentType, fixtureId);
    return false;
  }

  const mode = __ENV.NGB_PM_POSTING_MODE?.trim() || 'fresh';
  if (mode !== 'fresh' && mode !== 'idempotent-replay') fail('NGB_PM_POSTING_MODE must be fresh or idempotent-replay.');
  if (!fixtureId) fail('Posting requires NGB_PM_FIXTURE_RENT_CHARGE_ID as a payload template.');

  if (!pending) pending = createCommand(context, fixtureId);
  if (!pending) return false;
  const command = pending;
  const postingMode = command.executionId ? 'replay' : 'fresh';
  branches.add(1, { branch: postingMode });
  const response = context.documents.executeAction(
    documentType, command.documentId, 'post', command.expectedVersion, command.key, { postingMode },
  );
  const result = readObject(response);
  const document = result.document as Record<string, unknown> | undefined;
  const succeeded = check(response, {
    'rent charge action posted the intended document': () => response.status === 200
      && document?.id === command.documentId && document?.status === 'Posted'
      && typeof result.executionId === 'string' && typeof result.documentVersion === 'number',
    'rent charge replay preserves execution and version': () => !command.executionId
      || (result.executionId === command.executionId && result.documentVersion === command.resultVersion),
  }, { area: 'documents', operation: 'pm.rent_charge.post', postingMode });

  if (!succeeded) return false; // Preserve the key after ambiguous failures to avoid duplicate posting.
  command.executionId = result.executionId as string;
  command.resultVersion = result.documentVersion as number;
  posts.add(1, { postingMode });
  accountingEffectsFlow(context, documentType, command.documentId);
  documentFlowReadFlow(context, documentType, command.documentId);
  if (mode === 'fresh') pending = undefined;
  return true;
}

function createCommand(context: NgbScenarioContext, fixtureId: string): PostingCommand | undefined {
  if (!templateFields) {
    const opened = context.documents.openDocument(documentType, fixtureId);
    if (opened.status !== 200) return undefined;
    const payload = readObject(opened).payload as { fields?: Record<string, unknown> } | undefined;
    const source = payload?.fields;
    if (!source) fail('Rent-charge template has no payload.fields.');
    const fields: Record<string, unknown> = {};
    for (const field of ['lease_id', 'period_from_utc', 'period_to_utc', 'due_on_utc', 'amount']) {
      if (source[field] === undefined) fail(`Rent-charge template is missing ${field}.`);
      fields[field] = source[field];
    }
    for (const [field, variable] of [
      ['period_from_utc', 'NGB_PM_POSTING_FROM_UTC'],
      ['period_to_utc', 'NGB_PM_POSTING_TO_UTC'],
      ['due_on_utc', 'NGB_PM_POSTING_DUE_ON_UTC'],
    ] as const) {
      const date = __ENV[variable]?.trim();
      if (date && !/^\d{4}-\d{2}-\d{2}$/.test(date)) fail(`${variable} must be YYYY-MM-DD.`);
      if (date) fields[field] = date;
    }
    const from = String(fields.period_from_utc);
    const period = `${from.slice(0, 7)}-01`;
    const status = context.periodClosing.getMonthStatus(period);
    if (status.status !== 200 || readObject(status).isClosed !== false) {
      exec.test.abort(`Posting template period ${period} is closed or unavailable. Set posting dates to an open period; do not reopen periods for the test.`);
      return undefined;
    }
    templateFields = fields;
  }
  const created = context.documents.createDocument(documentType, {
    fields: { ...templateFields, memo: `Performance posting VU ${__VU}` },
  });
  const documentId = responseDocumentId(created);
  if (![200, 201].includes(created.status) || !documentId) return undefined;
  const editorState = context.documents.getEditorState(documentType, documentId);
  const version = readObject(editorState).documentVersion;
  if (editorState.status !== 200 || typeof version !== 'number') return undefined;
  return { documentId, expectedVersion: version, key: `perf:post:${documentId}` };
}

function readObject(response: NgbHttpResponse): Record<string, unknown> {
  try {
    const value = response.json();
    return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
  } catch { return {}; }
}
