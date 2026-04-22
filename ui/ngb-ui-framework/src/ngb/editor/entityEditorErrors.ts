import { ApiError, type ApiValidationIssue } from '../api/http';

export const ENTITY_EDITOR_FORM_ISSUE_PATH = '_form';

export type EditorErrorIssue = {
  path: string;
  label: string;
  scope: string;
  messages: string[];
  code?: string | null;
};

export type EditorErrorState = {
  summary: string;
  issues: EditorErrorIssue[];
  errorCode?: string | null;
  status?: number | null;
  context?: Record<string, unknown> | null;
};

export type NormalizeEntityEditorErrorOptions = {
  resolveIssueLabel?: (path: string) => string;
};

export function dedupeEntityEditorMessages(messages: string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];

  for (const message of messages) {
    const value = String(message ?? '').trim();
    if (!value) continue;

    const key = value.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(value);
  }

  return result;
}

export function humanizeEntityEditorFieldKey(key: string): string {
  const base = String(key ?? '').trim();
  if (!base) return 'Field';

  const normalized = base
    .replace(/_utc$/i, '')
    .replace(/_id$/i, '')
    .replace(/[._]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();

  const words = normalized.split(' ').filter(Boolean).map((word) => {
    const lower = word.toLowerCase();
    if (lower === 'no') return 'No';
    if (lower === 'id') return 'ID';
    if (/^line\d+$/i.test(word)) return `Line ${word.slice(4)}`;
    return lower.charAt(0).toUpperCase() + lower.slice(1);
  });

  return words.join(' ') || 'Field';
}

export function isEntityEditorFormIssuePath(path: string): boolean {
  const raw = String(path ?? '').trim();
  return raw.length === 0 || raw === ENTITY_EDITOR_FORM_ISSUE_PATH;
}

function defaultEditorIssueLabel(path: string): string {
  if (isEntityEditorFormIssuePath(path)) return 'Validation';
  return humanizeEntityEditorFieldKey(path);
}

function buildEditorIssuesFromApiIssues(
  issues: ApiValidationIssue[],
  resolveIssueLabel: (path: string) => string,
): EditorErrorIssue[] {
  const buckets = new Map<string, EditorErrorIssue>();

  for (const issue of issues) {
    const path = String(issue.path ?? '').trim() || ENTITY_EDITOR_FORM_ISSUE_PATH;
    const scope = String(issue.scope ?? '').trim() || (isEntityEditorFormIssuePath(path) ? 'form' : 'field');
    const message = String(issue.message ?? '').trim();
    if (!message) continue;

    const bucketKey = `${scope}:${path}`;
    const current = buckets.get(bucketKey) ?? {
      path,
      label: resolveIssueLabel(path),
      scope,
      messages: [],
      code: issue.code ?? null,
    };

    current.messages = dedupeEntityEditorMessages([...current.messages, message]);
    if (!current.code && issue.code) current.code = issue.code;
    buckets.set(bucketKey, current);
  }

  return Array.from(buckets.values());
}

function buildEditorIssuesFromLegacyErrors(
  rawErrors: Record<string, string[] | string>,
  resolveIssueLabel: (path: string) => string,
): EditorErrorIssue[] {
  const issues: EditorErrorIssue[] = [];

  for (const [path, values] of Object.entries(rawErrors)) {
    const messages = dedupeEntityEditorMessages(Array.isArray(values) ? values : [String(values)]);
    if (messages.length === 0) continue;

    issues.push({
      path,
      label: resolveIssueLabel(path),
      scope: isEntityEditorFormIssuePath(path) ? 'form' : 'field',
      messages,
      code: null,
    });
  }

  return issues;
}

export function resolveEntityEditorErrorSummary(summary: string, issues: EditorErrorIssue[]): string {
  const fallback = summary.trim() || 'Request failed.';
  const hasHighlightableIssues = issues.some((issue) => !isEntityEditorFormIssuePath(issue.path));
  if (hasHighlightableIssues) return 'Please fix the highlighted fields.';
  return fallback;
}

export function normalizeEntityEditorError(
  cause: unknown,
  options: NormalizeEntityEditorErrorOptions = {},
): EditorErrorState {
  const resolveIssueLabel = options.resolveIssueLabel ?? defaultEditorIssueLabel;

  if (cause instanceof ApiError) {
    const issues = cause.issues && cause.issues.length > 0
      ? buildEditorIssuesFromApiIssues(cause.issues, resolveIssueLabel)
      : cause.errors
        ? buildEditorIssuesFromLegacyErrors(cause.errors, resolveIssueLabel)
        : [];

    return {
      summary: resolveEntityEditorErrorSummary(String(cause.message ?? '').trim(), issues),
      issues,
      errorCode: cause.errorCode ?? null,
      status: cause.status,
      context: cause.context ?? null,
    };
  }

  const summary = cause instanceof Error ? cause.message : String(cause ?? 'Request failed.');
  return {
    summary: summary.trim() || 'Request failed.',
    issues: [],
    errorCode: null,
    status: null,
    context: null,
  };
}
