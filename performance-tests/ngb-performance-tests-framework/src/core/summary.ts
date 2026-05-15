import type { Options } from 'k6/options';

import { readK6HostAliases, readK6InsecureSkipTlsVerify, readNgbPerfEnv } from './env.ts';

export type SummaryOutput = Record<string, string>;

interface K6SummaryData {
  readonly state?: {
    readonly testRunDurationMs?: number;
  };
  readonly metrics?: Record<string, K6Metric>;
  readonly root_group?: K6Group;
}

interface K6Metric {
  readonly type?: string;
  readonly contains?: string;
  readonly values?: Record<string, number>;
  readonly thresholds?: Record<string, { readonly ok?: boolean }>;
}

interface K6Group {
  readonly name?: string;
  readonly path?: string;
  readonly checks?: readonly K6Check[];
  readonly groups?: readonly K6Group[];
}

interface K6Check {
  readonly name?: string;
  readonly passes?: number;
  readonly fails?: number;
}

interface LatencyRow {
  readonly label: string;
  readonly metricName: string;
}

interface ParsedTaggedMetric {
  readonly root: string;
  readonly tags: Record<string, string>;
}

interface OperationBreakdownRow {
  readonly label: string;
  readonly metricName: string;
  readonly tags: Record<string, string>;
}

const primaryLatencyRows: readonly LatencyRow[] = [
  { label: 'HTTP overall', metricName: 'http_req_duration' },
  { label: 'Business operation', metricName: 'ngb_business_operation_duration' },
  { label: 'Iteration duration', metricName: 'iteration_duration' },
  { label: 'Auth', metricName: 'ngb_auth_duration{area:auth}' },
];

const areaLatencyRows: readonly LatencyRow[] = [
  { label: 'Health HTTP', metricName: 'http_req_duration{area:health}' },
  { label: 'Metadata HTTP', metricName: 'http_req_duration{area:metadata}' },
  { label: 'Catalogs HTTP', metricName: 'http_req_duration{area:catalogs}' },
  { label: 'Documents HTTP', metricName: 'http_req_duration{area:documents}' },
  { label: 'Admin HTTP', metricName: 'http_req_duration{area:admin}' },
  { label: 'Chart of accounts HTTP', metricName: 'http_req_duration{area:chart-of-accounts}' },
  { label: 'Reports', metricName: 'ngb_report_execution_duration{area:reports}' },
  { label: 'Report export HTTP', metricName: 'http_req_duration{area:report-export}' },
  { label: 'Accounting effects', metricName: 'ngb_accounting_effects_duration{area:accounting}' },
  { label: 'Document post', metricName: 'ngb_document_post_duration{area:documents}' },
  { label: 'Document flow', metricName: 'ngb_document_flow_duration{area:document-flow}' },
  { label: 'Audit HTTP', metricName: 'http_req_duration{area:audit}' },
  { label: 'Period closing HTTP', metricName: 'http_req_duration{area:period-closing}' },
];

export function defaultHandleSummary(data: unknown): SummaryOutput {
  const env = readNgbPerfEnv();
  const summary = asK6SummaryData(data);
  const text = buildTextSummary(summary);
  const markdown = buildMarkdownSummary(summary);
  const output: SummaryOutput = {
    stdout: text,
  };

  if (env.summaryExportPath) {
    output[env.summaryExportPath] = JSON.stringify(data, null, 2);
    output[deriveMarkdownPath(env.summaryExportPath)] = markdown;
  }

  return output;
}

export function withSummaryTrendStats(options: Options): Options {
  const hosts = mergeHosts(options.hosts, readK6HostAliases());

  return {
    ...options,
    ...(Object.keys(hosts).length > 0 ? { hosts } : {}),
    insecureSkipTLSVerify: readK6InsecureSkipTlsVerify(),
    summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  };
}

function mergeHosts(
  configuredHosts: Record<string, string> | undefined,
  envHosts: Record<string, string>,
): Record<string, string> {
  return {
    ...(configuredHosts ?? {}),
    ...envHosts,
  };
}

function asK6SummaryData(data: unknown): K6SummaryData {
  return typeof data === 'object' && data !== null ? data as K6SummaryData : {};
}

function buildTextSummary(data: K6SummaryData): string {
  const metrics = data.metrics ?? {};
  const runDurationSeconds = (data.state?.testRunDurationMs ?? 0) / 1000;
  const completedIterations = metricValue(metrics, 'iterations', 'count');
  const droppedIterations = metricValue(metrics, 'dropped_iterations', 'count');
  const scheduledIterations = completedIterations + droppedIterations;
  const checksPasses = metricValue(metrics, 'checks', 'passes');
  const checksFails = metricValue(metrics, 'checks', 'fails');
  const checksTotal = checksPasses + checksFails;

  const lines = [
    '',
    'NGB k6 performance summary',
    'Run',
    `  duration: ${formatDuration(runDurationSeconds)}`,
    `  iterations: completed=${formatInteger(completedIterations)} dropped=${formatInteger(droppedIterations)} scheduled=${formatInteger(scheduledIterations)} completion=${formatPercent(ratio(completedIterations, scheduledIterations))}`,
    `  throughput: iterations=${formatRate(metricValue(metrics, 'iterations', 'rate'))}/s http=${formatRate(metricValue(metrics, 'http_reqs', 'rate'))}/s business_ops=${formatRate(metricValue(metrics, 'ngb_business_operation_count', 'rate'))}/s`,
    `  volume: http_reqs=${formatInteger(metricValue(metrics, 'http_reqs', 'count'))} business_ops=${formatInteger(metricValue(metrics, 'ngb_business_operation_count', 'count'))} data_received=${formatBytes(metricValue(metrics, 'data_received', 'count'))} data_sent=${formatBytes(metricValue(metrics, 'data_sent', 'count'))}`,
    `  VUs: peak_active=${formatInteger(metricValue(metrics, 'vus', 'max'))} peak_allocated=${formatInteger(metricValue(metrics, 'vus_max', 'max'))}`,
    '',
    'Reliability',
    `  HTTP failures: rate=${formatPercent(metricValue(metrics, 'http_req_failed', 'rate'))} failed=${formatInteger(metricValue(metrics, 'http_req_failed', 'passes'))}`,
    `  business failures: rate=${formatPercent(metricValue(metrics, 'ngb_business_operation_failed', 'rate'))} failed=${formatInteger(metricValue(metrics, 'ngb_business_operation_failed', 'passes'))}`,
    `  checks: rate=${formatPercent(metricValue(metrics, 'checks', 'rate'))} passed=${formatInteger(checksPasses)} failed=${formatInteger(checksFails)} total=${formatInteger(checksTotal)}`,
    `  dropped iterations: ${formatInteger(droppedIterations)}${droppedIterations > 0 ? ' (load generator could not start every scheduled iteration)' : ''}`,
    '',
    'Latency',
    ...latencyLines(metrics, primaryLatencyRows),
    '',
    'Latency By Area',
    ...nonEmptyOrFallback(latencyLines(metrics, areaLatencyRows), '  no area-specific samples'),
    '',
    'HTTP By Operation',
    ...nonEmptyOrFallback(operationBreakdownLines(metrics), '  no operation breakdown samples (configure diagnosticBreakdowns in the test profile)'),
    '',
    'Failures By Status',
    ...nonEmptyOrFallback(failureStatusLines(metrics), '  no status breakdown samples'),
    '',
    'Report Execution By Id',
    ...nonEmptyOrFallback(
      latencyLines(metrics, reportExecutionLatencyRows(metrics)),
      '  no report-id samples (configure reportBreakdownIds in the vertical test profile)',
    ),
    '',
    'Thresholds',
    ...nonEmptyOrFallback(thresholdLines(metrics), '  no thresholds configured'),
    '',
    'Checks',
    ...nonEmptyOrFallback(checkLines(data.root_group), '  no checks recorded'),
    '',
  ];

  return `${lines.join('\n')}\n`;
}

function buildMarkdownSummary(data: K6SummaryData): string {
  const metrics = data.metrics ?? {};
  const runDurationSeconds = (data.state?.testRunDurationMs ?? 0) / 1000;
  const completedIterations = metricValue(metrics, 'iterations', 'count');
  const droppedIterations = metricValue(metrics, 'dropped_iterations', 'count');
  const scheduledIterations = completedIterations + droppedIterations;

  return [
    '# NGB k6 Performance Summary',
    '',
    '## Run',
    '',
    '| Metric | Value |',
    '| --- | ---: |',
    `| Duration | ${formatDuration(runDurationSeconds)} |`,
    `| Completed iterations | ${formatInteger(completedIterations)} |`,
    `| Dropped iterations | ${formatInteger(droppedIterations)} |`,
    `| Scheduled iterations | ${formatInteger(scheduledIterations)} |`,
    `| Completion | ${formatPercent(ratio(completedIterations, scheduledIterations))} |`,
    `| Iterations/sec | ${formatRate(metricValue(metrics, 'iterations', 'rate'))} |`,
    `| HTTP req/sec | ${formatRate(metricValue(metrics, 'http_reqs', 'rate'))} |`,
    `| Business ops/sec | ${formatRate(metricValue(metrics, 'ngb_business_operation_count', 'rate'))} |`,
    `| HTTP requests | ${formatInteger(metricValue(metrics, 'http_reqs', 'count'))} |`,
    `| Business operations | ${formatInteger(metricValue(metrics, 'ngb_business_operation_count', 'count'))} |`,
    `| Peak active VUs | ${formatInteger(metricValue(metrics, 'vus', 'max'))} |`,
    `| Peak allocated VUs | ${formatInteger(metricValue(metrics, 'vus_max', 'max'))} |`,
    '',
    '## Reliability',
    '',
    '| Metric | Value |',
    '| --- | ---: |',
    `| HTTP failure rate | ${formatPercent(metricValue(metrics, 'http_req_failed', 'rate'))} |`,
    `| HTTP failed requests | ${formatInteger(metricValue(metrics, 'http_req_failed', 'passes'))} |`,
    `| Business failure rate | ${formatPercent(metricValue(metrics, 'ngb_business_operation_failed', 'rate'))} |`,
    `| Business failed operations | ${formatInteger(metricValue(metrics, 'ngb_business_operation_failed', 'passes'))} |`,
    `| Check pass rate | ${formatPercent(metricValue(metrics, 'checks', 'rate'))} |`,
    `| Check failures | ${formatInteger(metricValue(metrics, 'checks', 'fails'))} |`,
    '',
    '## Latency',
    '',
    markdownLatencyTable(metrics, primaryLatencyRows),
    '',
    '## Latency By Area',
    '',
    markdownLatencyTable(metrics, areaLatencyRows),
    '',
    '## HTTP By Operation',
    '',
    markdownOperationBreakdownTable(metrics),
    '',
    '## Failures By Status',
    '',
    markdownFailureStatusTable(metrics),
    '',
    '## Report Execution By Id',
    '',
    markdownLatencyTable(metrics, reportExecutionLatencyRows(metrics), 'Report ID'),
    '',
    '## Thresholds',
    '',
    markdownThresholdTable(metrics),
    '',
    '## Checks',
    '',
    markdownChecksTable(data.root_group),
    '',
  ].join('\n');
}

function latencyLines(metrics: Record<string, K6Metric>, rows: readonly LatencyRow[]): string[] {
  return rows
    .filter((row) => trendHasSamples(metrics[row.metricName]))
    .map((row) => {
      const values = metrics[row.metricName]?.values ?? {};
      return `  ${row.label}: avg=${formatMs(values.avg)} med=${formatMs(values.med)} p90=${formatMs(values['p(90)'])} p95=${formatMs(values['p(95)'])} p99=${formatMs(values['p(99)'])} max=${formatMs(values.max)}`;
    });
}

function reportExecutionLatencyRows(metrics: Record<string, K6Metric>): LatencyRow[] {
  return Object.keys(metrics)
    .map((metricName) => {
      const tags = parseMetricTags(metricName);
      const reportId = tags?.reportId;
      if (
        !metricName.startsWith('ngb_report_execution_duration{')
        || tags?.area !== 'reports'
        || tags?.operation !== 'platform.reports.execute'
        || !reportId
      ) {
        return null;
      }

      const periodProfile = tags?.periodProfile;
      return { label: periodProfile ? `${reportId} [${periodProfile}]` : reportId, metricName };
    })
    .filter((row): row is LatencyRow => row !== null && trendHasSamples(metrics[row.metricName]))
    .sort((left, right) => {
      const leftValues = metrics[left.metricName]?.values ?? {};
      const rightValues = metrics[right.metricName]?.values ?? {};
      return (rightValues['p(95)'] ?? 0) - (leftValues['p(95)'] ?? 0)
        || left.label.localeCompare(right.label);
    });
}

function operationBreakdownRows(metrics: Record<string, K6Metric>): OperationBreakdownRow[] {
  return Object.keys(metrics)
    .map((metricName) => {
      const parsed = parseTaggedMetricName(metricName);
      if (
        parsed?.root !== 'http_req_duration'
        || !parsed.tags.operation
        || parsed.tags.status
        || !trendHasSamples(metrics[metricName])
      ) {
        return null;
      }

      return {
        label: operationBreakdownLabel(parsed.tags),
        metricName,
        tags: parsed.tags,
      };
    })
    .filter((row): row is OperationBreakdownRow => row !== null)
    .sort((left, right) => {
      const leftFailed = rateCount(findMatchingTaggedMetric(metrics, 'http_req_failed', left.tags));
      const rightFailed = rateCount(findMatchingTaggedMetric(metrics, 'http_req_failed', right.tags));
      const leftValues = metrics[left.metricName]?.values ?? {};
      const rightValues = metrics[right.metricName]?.values ?? {};
      return rightFailed - leftFailed
        || (rightValues['p(95)'] ?? 0) - (leftValues['p(95)'] ?? 0)
        || left.label.localeCompare(right.label);
    });
}

function operationBreakdownLines(metrics: Record<string, K6Metric>): string[] {
  return operationBreakdownRows(metrics)
    .slice(0, 25)
    .map((row) => {
      const values = metrics[row.metricName]?.values ?? {};
      const httpFailed = findMatchingTaggedMetric(metrics, 'http_req_failed', row.tags);
      return `  ${row.label}: fail_rate=${formatPercent(rateValue(httpFailed))} failed=${formatInteger(rateCount(httpFailed))} p95=${formatMs(values['p(95)'])} p99=${formatMs(values['p(99)'])} max=${formatMs(values.max)}`;
    });
}

function failureStatusRows(metrics: Record<string, K6Metric>): OperationBreakdownRow[] {
  return Object.keys(metrics)
    .map((metricName) => {
      const parsed = parseTaggedMetricName(metricName);
      if (
        parsed?.root !== 'ngb_business_operation_failed'
        || !parsed.tags.status
        || !rateHasSamples(metrics[metricName])
      ) {
        return null;
      }

      return {
        label: operationBreakdownLabel(parsed.tags),
        metricName,
        tags: parsed.tags,
      };
    })
    .filter((row): row is OperationBreakdownRow => row !== null)
    .sort((left, right) => {
      const leftMetric = metrics[left.metricName];
      const rightMetric = metrics[right.metricName];
      return rateCount(rightMetric) - rateCount(leftMetric)
        || rateValue(rightMetric) - rateValue(leftMetric)
        || left.label.localeCompare(right.label);
    });
}

function failureStatusLines(metrics: Record<string, K6Metric>): string[] {
  return failureStatusRows(metrics)
    .filter((row) => rateCount(metrics[row.metricName]) > 0)
    .slice(0, 25)
    .map((row) => {
      const metric = metrics[row.metricName];
      return `  ${row.label}: fail_rate=${formatPercent(rateValue(metric))} failed=${formatInteger(rateCount(metric))} total=${formatInteger(rateTotal(metric))}`;
    });
}

function operationBreakdownLabel(tags: Record<string, string>): string {
  const qualifiers = [
    tags.documentType ? `doc=${tags.documentType}` : '',
    tags.catalogType ? `catalog=${tags.catalogType}` : '',
    tags.reportId ? `report=${tags.reportId}` : '',
    tags.entityKind ? `entity=${tags.entityKind}` : '',
    tags.periodProfile ? `period=${tags.periodProfile}` : '',
    tags.status ? `status=${tags.status}` : '',
  ].filter(Boolean);

  return qualifiers.length > 0
    ? `${tags.operation ?? tags.area ?? 'operation'} [${qualifiers.join(', ')}]`
    : (tags.operation ?? tags.area ?? 'operation');
}

function parseMetricTags(metricName: string): Record<string, string> | null {
  return parseTaggedMetricName(metricName)?.tags ?? null;
}

function parseTaggedMetricName(metricName: string): ParsedTaggedMetric | null {
  const match = metricName.match(/^([^{]+){(.+)}$/);
  if (!match) {
    return null;
  }

  const root = match[1];
  const tagList = match[2];
  if (!root || !tagList) {
    return null;
  }

  const tags: Record<string, string> = {};
  for (const pair of tagList.split(',')) {
    const separator = pair.indexOf(':');
    if (separator <= 0) {
      continue;
    }

    const key = pair.slice(0, separator).trim();
    const value = pair.slice(separator + 1).trim();
    if (key && value) {
      tags[key] = value;
    }
  }

  return { root, tags };
}

function findMatchingTaggedMetric(
  metrics: Record<string, K6Metric>,
  root: string,
  tags: Record<string, string>,
): K6Metric | undefined {
  for (const [metricName, metric] of Object.entries(metrics)) {
    const parsed = parseTaggedMetricName(metricName);
    if (parsed?.root === root && sameTags(parsed.tags, tags)) {
      return metric;
    }
  }

  return undefined;
}

function sameTags(left: Record<string, string>, right: Record<string, string>): boolean {
  const leftEntries = Object.entries(left).sort(([leftKey], [rightKey]) => leftKey.localeCompare(rightKey));
  const rightEntries = Object.entries(right).sort(([leftKey], [rightKey]) => leftKey.localeCompare(rightKey));
  return leftEntries.length === rightEntries.length
    && leftEntries.every(([key, value], index) => {
      const [rightKey, rightValue] = rightEntries[index] ?? [];
      return key === rightKey && value === rightValue;
    });
}

function thresholdLines(metrics: Record<string, K6Metric>): string[] {
  const lines: string[] = [];

  for (const [metricName, metric] of Object.entries(metrics)) {
    for (const [expression, result] of Object.entries(metric.thresholds ?? {})) {
      if (isDiagnosticMaterializationThreshold(expression)) {
        continue;
      }

      lines.push(`  ${result.ok ? 'PASS' : 'FAIL'} ${metricName} ${expression}`);
    }
  }

  return lines.sort((left, right) => left.localeCompare(right));
}

function isDiagnosticMaterializationThreshold(expression: string): boolean {
  return expression === 'max<600000'
    || expression === 'rate<1.01'
    || expression === 'rate<1';
}

function checkLines(rootGroup: K6Group | undefined): string[] {
  return collectChecks(rootGroup)
    .sort((left, right) => (right.fails ?? 0) - (left.fails ?? 0) || (right.passes ?? 0) - (left.passes ?? 0))
    .slice(0, 12)
    .map((check) => `  ${check.name ?? 'unnamed check'}: passed=${formatInteger(check.passes ?? 0)} failed=${formatInteger(check.fails ?? 0)}`);
}

function collectChecks(group: K6Group | undefined): K6Check[] {
  if (!group) {
    return [];
  }

  const checks: K6Check[] = [...(group.checks ?? [])];
  for (const child of group.groups ?? []) {
    checks.push(...collectChecks(child));
  }

  return checks;
}

function markdownLatencyTable(
  metrics: Record<string, K6Metric>,
  rows: readonly LatencyRow[],
  labelHeader = 'Area',
): string {
  const sampledRows = rows.filter((row) => trendHasSamples(metrics[row.metricName]));
  if (sampledRows.length === 0) {
    return '_No samples._';
  }

  return [
    `| ${labelHeader} | Avg | Med | P90 | P95 | P99 | Max |`,
    '| --- | ---: | ---: | ---: | ---: | ---: | ---: |',
    ...sampledRows.map((row) => {
      const values = metrics[row.metricName]?.values ?? {};
      return `| ${row.label} | ${formatMs(values.avg)} | ${formatMs(values.med)} | ${formatMs(values['p(90)'])} | ${formatMs(values['p(95)'])} | ${formatMs(values['p(99)'])} | ${formatMs(values.max)} |`;
    }),
  ].join('\n');
}

function markdownOperationBreakdownTable(metrics: Record<string, K6Metric>): string {
  const rows = operationBreakdownRows(metrics).slice(0, 50);
  if (rows.length === 0) {
    return '_No operation breakdown samples. Configure `diagnosticBreakdowns` in the test profile._';
  }

  return [
    '| Operation | Failed | Failure Rate | P95 | P99 | Max |',
    '| --- | ---: | ---: | ---: | ---: | ---: |',
    ...rows.map((row) => {
      const values = metrics[row.metricName]?.values ?? {};
      const httpFailed = findMatchingTaggedMetric(metrics, 'http_req_failed', row.tags);
      return `| ${escapeMarkdownCell(row.label)} | ${formatInteger(rateCount(httpFailed))} | ${formatPercent(rateValue(httpFailed))} | ${formatMs(values['p(95)'])} | ${formatMs(values['p(99)'])} | ${formatMs(values.max)} |`;
    }),
  ].join('\n');
}

function markdownFailureStatusTable(metrics: Record<string, K6Metric>): string {
  const rows = failureStatusRows(metrics).filter((row) => rateCount(metrics[row.metricName]) > 0).slice(0, 50);
  if (rows.length === 0) {
    return '_No failed status samples._';
  }

  return [
    '| Operation | Status | Failed | Total | Failure Rate |',
    '| --- | ---: | ---: | ---: | ---: |',
    ...rows.map((row) => {
      const metric = metrics[row.metricName];
      return `| ${escapeMarkdownCell(operationBreakdownLabel({ ...row.tags, status: '' }))} | ${row.tags.status ?? 'n/a'} | ${formatInteger(rateCount(metric))} | ${formatInteger(rateTotal(metric))} | ${formatPercent(rateValue(metric))} |`;
    }),
  ].join('\n');
}

function markdownThresholdTable(metrics: Record<string, K6Metric>): string {
  const lines = thresholdLines(metrics);
  if (lines.length === 0) {
    return '_No thresholds configured._';
  }

  return [
    '| Result | Threshold |',
    '| --- | --- |',
    ...lines.map((line) => {
      const trimmed = line.trim();
      const firstSpace = trimmed.indexOf(' ');
      return `| ${trimmed.slice(0, firstSpace)} | ${trimmed.slice(firstSpace + 1)} |`;
    }),
  ].join('\n');
}

function markdownChecksTable(rootGroup: K6Group | undefined): string {
  const checks = collectChecks(rootGroup);
  if (checks.length === 0) {
    return '_No checks recorded._';
  }

  return [
    '| Check | Passed | Failed |',
    '| --- | ---: | ---: |',
    ...checks
      .sort((left, right) => (right.fails ?? 0) - (left.fails ?? 0) || (right.passes ?? 0) - (left.passes ?? 0))
      .slice(0, 20)
      .map((check) => `| ${check.name ?? 'unnamed check'} | ${formatInteger(check.passes ?? 0)} | ${formatInteger(check.fails ?? 0)} |`),
  ].join('\n');
}

function nonEmptyOrFallback(lines: string[], fallback: string): string[] {
  return lines.length > 0 ? lines : [fallback];
}

function metricValue(metrics: Record<string, K6Metric>, metricName: string, valueName: string): number {
  return metrics[metricName]?.values?.[valueName] ?? 0;
}

function trendHasSamples(metric: K6Metric | undefined): boolean {
  const values = metric?.values;
  if (!values) {
    return false;
  }

  return ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'].some((key) => (values[key] ?? 0) > 0);
}

function rateHasSamples(metric: K6Metric | undefined): boolean {
  return rateTotal(metric) > 0;
}

function rateValue(metric: K6Metric | undefined): number {
  return metric?.values?.rate ?? 0;
}

function rateCount(metric: K6Metric | undefined): number {
  return metric?.values?.passes ?? 0;
}

function rateTotal(metric: K6Metric | undefined): number {
  const values = metric?.values;
  return (values?.passes ?? 0) + (values?.fails ?? 0);
}

function ratio(value: number, total: number): number {
  return total > 0 ? value / total : 0;
}

function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return 'n/a';
  }

  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds - minutes * 60;
  return minutes > 0 ? `${minutes}m ${remainingSeconds.toFixed(1)}s` : `${remainingSeconds.toFixed(1)}s`;
}

function formatInteger(value: number): string {
  if (!Number.isFinite(value)) {
    return 'n/a';
  }

  return addThousandsSeparators(String(Math.round(value)));
}

function formatRate(value: number): string {
  return Number.isFinite(value) ? value.toFixed(2) : 'n/a';
}

function formatPercent(value: number): string {
  return Number.isFinite(value) ? `${(value * 100).toFixed(2)}%` : 'n/a';
}

function formatMs(value: number | undefined): string {
  return typeof value === 'number' && Number.isFinite(value) ? `${value.toFixed(2)}ms` : 'n/a';
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value)) {
    return 'n/a';
  }

  const units = ['B', 'KiB', 'MiB', 'GiB'];
  let current = value;
  let unitIndex = 0;
  while (current >= 1024 && unitIndex < units.length - 1) {
    current /= 1024;
    unitIndex += 1;
  }

  return `${current.toFixed(unitIndex === 0 ? 0 : 2)} ${units[unitIndex]}`;
}

function escapeMarkdownCell(value: string): string {
  return value.replace(/\|/g, '\\|');
}

function deriveMarkdownPath(summaryExportPath: string): string {
  return summaryExportPath.endsWith('.json')
    ? summaryExportPath.replace(/\.json$/, '.md')
    : `${summaryExportPath}.md`;
}

function addThousandsSeparators(value: string): string {
  return value.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}
