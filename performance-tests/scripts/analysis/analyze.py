#!/usr/bin/env python3
"""Analyze saved NGB k6 summaries, JSON/gzip samples and MCP resource samples offline."""
import argparse
from array import array
from collections import Counter, defaultdict
from datetime import datetime, timezone
import gzip
import json
import math
from pathlib import Path
import re

COUNTS = {'http_req_failed': 'httpErrors', 'ngb_business_operation_failed': 'businessErrors',
          'iterations': 'iterations', 'dropped_iterations': 'drops'}
OPERATION_TAGS = ('scenario', 'operation', 'documentType', 'reportId', 'periodProfile', 'catalogType')


def timestamp(value):
    return datetime.fromisoformat(value.replace('Z', '+00:00')).timestamp()


def quantile(ordered, fraction):
    if not ordered:
        return None
    position = (len(ordered) - 1) * fraction
    lower = int(position)
    return ordered[lower] + (ordered[min(lower + 1, len(ordered) - 1)] - ordered[lower]) * (position - lower)


def stats(values):
    ordered = sorted(values)
    return {'count': len(ordered), 'p50': quantile(ordered, .5), 'p95': quantile(ordered, .95),
            'p99': quantile(ordered, .99), 'max': ordered[-1] if ordered else None,
            'over1second': sum(v > 1000 for v in ordered), 'over5seconds': sum(v > 5000 for v in ordered)}


def seconds(value):
    parts = re.findall(r'(\d+(?:\.\d+)?)(ms|s|m|h)', value)
    if not parts or ''.join(a + b for a, b in parts) != value:
        raise ValueError(f'Unsupported k6 duration: {value}')
    return sum(float(a) * {'ms': .001, 's': 1, 'm': 60, 'h': 3600}[b] for a, b in parts)


def points(path, wanted):
    markers = [f'"{key}"'.encode() for key in wanted]
    opener = gzip.open if path.suffix == '.gz' else open
    with opener(path, 'rb') as stream:
        for line in stream:
            if not any(marker in line for marker in markers):
                continue
            item = json.loads(line)
            if item.get('type') == 'Point' and item.get('metric') in wanted:
                data = item['data']
                if not math.isfinite(data['value']):
                    raise ValueError('Non-finite sample value.')
                yield item['metric'], data, timestamp(data['time'])


def hold_windows(summary):
    scenarios = summary.get('runConfiguration', {}).get('scenarios', {})
    if len(scenarios) != 1:
        return []
    scenario = next(iter(scenarios.values()))
    if scenario.get('executor') not in ('ramping-vus', 'ramping-arrival-rate'):
        return []
    start, previous, windows = 0, scenario.get('startVUs', scenario.get('startRate', 1)), []
    for index, stage in enumerate(scenario.get('stages', [])):
        end = start + seconds(stage['duration'])
        if stage['target'] == previous and previous > 0 and end - start > 4:
            windows.append({'name': f'hold-{index + 1}-{previous}', 'target': previous,
                            'startSeconds': start + 2, 'endSeconds': end - 2,
                            'executor': scenario['executor']})
        previous, start = stage['target'], end
    return windows


def new_bucket():
    return {'values': array('d'), 'operations': defaultdict(lambda: array('d')), 'counts': Counter()}


def add(bucket, metric, data):
    value, tags = data['value'], data.get('tags', {})
    if metric == 'http_req_duration':
        bucket['values'].append(value)
        key = '|'.join(str(tags.get(k, '')) for k in OPERATION_TAGS)
        bucket['operations'][key].append(value)
    elif metric in COUNTS:
        bucket['counts'][COUNTS[metric]] += value


def finish(bucket, seen):
    return {'http': stats(bucket['values']),
            'counts': {name: bucket['counts'][name] if metric in seen else None for metric, name in COUNTS.items()},
            'operations': {key: stats(values) for key, values in sorted(bucket['operations'].items())}}


def analyze_samples(path, summary):
    # Reconstruct scenario time from completed iteration durations, independent of auth jitter.
    starts = [at - data['value'] / 1000 for _, data, at in points(path, {'iteration_duration'})
              if data.get('tags', {}).get('scenario')]
    origin = min(starts) if starts else None
    del starts
    origin_method = 'earliest completed iteration end minus its duration'
    if origin is None:
        candidates = (at - data['value'] / 1000 for _, data, at in points(path, {'http_req_duration'})
                      if data.get('tags', {}).get('scenario'))
        origin = min(candidates, default=None)
        origin_method = 'first scenario request start; iteration samples unavailable'
    if origin is None:
        raise ValueError('No scenario samples; cannot establish a workload timeline.')
    windows = hold_windows(summary)
    total, minutes, holds = new_bucket(), defaultdict(new_bucket), defaultdict(new_bucket)
    series, names, series_by_operation = set(), set(), defaultdict(set)
    series_first_seen, mismatch_operations = Counter(), Counter()
    seen, vus, last = set(), [], origin
    wanted = {'http_req_duration', 'vus', *COUNTS}
    for metric, data, at in points(path, wanted):
        seen.add(metric)
        last = max(last, at)
        tags = data.get('tags', {})
        elapsed = at - origin
        minute = max(0, int(elapsed // 60))
        add(total, metric, data)
        if tags.get('scenario'):
            add(minutes[minute], metric, data)
            for window in windows:
                if window['startSeconds'] <= elapsed < window['endSeconds']:
                    add(holds[window['name']], metric, data)
        if metric == 'vus':
            vus.append((elapsed, data['value']))
        if metric == 'http_req_duration':
            key = tuple(sorted(tags.items()))
            if key not in series:
                series_first_seen[str(minute) if tags.get('scenario') else 'setup'] += 1
            series.add(key)
            names.add(tags.get('name'))
            operation = tags.get('operation', '(none)')
            series_by_operation[operation].add(key)
            if tags.get('operation') and (tags.get('name') != operation or tags.get('url') != operation):
                mismatch_operations[operation] += 1
    result = {'schemaVersion': 1, 'source': str(path),
              'startEstimateUtc': datetime.fromtimestamp(origin, timezone.utc).isoformat(),
              'originMethod': origin_method,
              'boundaryNote': 'Completion-time assignment; hold windows omit two seconds at each edge. Start time is estimated.',
              'operationKeyTags': OPERATION_TAGS, 'total': finish(total, seen),
              'peakSampledVU': max((value for _, value in vus), default=None),
              'cardinality': {'distinctHttpDurationSeries': len(series), 'distinctHttpNames': len(names),
                              'newSeriesByMinute': dict(series_first_seen),
                              'groupingMismatchOperations': dict(mismatch_operations),
                              'seriesByOperation': {key: len(value) for key, value in sorted(series_by_operation.items())}},
              'minutes': {str(key): finish(value, seen) for key, value in sorted(minutes.items())}, 'holds': {}}
    for window in windows:
        readings = [value for elapsed, value in vus if window['startSeconds'] <= elapsed < window['endSeconds']]
        value = finish(holds[window['name']], seen)
        duration = window['endSeconds'] - window['startSeconds']
        result['holds'][window['name']] = {**window, **value, 'windowSeconds': duration,
            'httpRps': value['http']['count'] / duration,
            'observedVUrange': [min(readings), max(readings)] if readings else None,
            'windowObservedThroughEnd': last - origin >= window['endSeconds'],
            'stageAlignmentValid': bool(readings) and all(v == window['target'] for v in readings)
                if window['executor'] == 'ramping-vus' else None}
    mismatches = []
    metrics = summary['metrics']
    def check(label, actual, expected):
        if expected is not None and (actual is None or not math.isclose(actual, expected, rel_tol=1e-10, abs_tol=1e-6)):
            mismatches.append({'metric': label, 'samples': actual, 'summary': expected})
    check('http_reqs', len(total['values']), metrics.get('http_reqs', {}).get('values', {}).get('count'))
    for metric, name in COUNTS.items():
        # k6 may export a zero counter in the summary without emitting any point for it.
        expected = metrics.get(metric, {}).get('values', {}).get('passes' if metric.endswith('_failed') else 'count')
        check(metric, total['counts'][name], expected)
    for key, field in [('p95', 'p(95)'), ('p99', 'p(99)'), ('max', 'max')]:
        check(field, result['total']['http'][key], metrics.get('http_req_duration', {}).get('values', {}).get(field))
    result['summaryMatchesSamples'] = not mismatches
    result['summaryMismatches'] = mismatches
    return result


def counter_delta(samples, section, key):
    values = [sample.get(section) for sample in samples]
    if len(values) < 2 or any(not value or value.get(key) is None for value in values):
        return None
    if len({value.get('stats_reset') for value in values}) != 1:
        return None
    counts = [float(value[key]) for value in values]
    if any(b < a for a, b in zip(counts, counts[1:])):
        return None
    return counts[-1] - counts[0]


def memory_gib(value):
    match = re.fullmatch(r'([\d.]+)\s*([A-Za-z]+)', value.split('/')[0].strip())
    units = {'B': 1, 'kB': 1000, 'MB': 10**6, 'GB': 10**9, 'TB': 10**12,
             'KiB': 1024, 'MiB': 1024**2, 'GiB': 1024**3, 'TiB': 1024**4}
    if not match or match[2] not in units:
        raise ValueError('Unrecognized Docker memory unit.')
    return float(match[1]) * units[match[2]] / 2**30


def analyze_resources(path, origin=None):
    samples = [json.loads(line) for line in path.read_text().splitlines() if line.strip()]
    if not samples:
        raise ValueError('Resource file is empty.')
    db = [sample['postgres_mcp']['rows'][0]['sample'] for sample in samples
          if sample.get('postgres_mcp', {}).get('rows')]
    errors = [{'at': sample['at'], 'component': key, 'error': value['error']}
              for sample in samples for key, value in sample.items() if isinstance(value, dict) and 'error' in value]
    deltas = {section: {key: counter_delta(db, section, key) for key in keys} for section, keys in {
        'database': ['deadlocks', 'temp_bytes', 'blocks_read', 'blocks_hit', 'commits', 'rollbacks'],
        'checkpoint': ['num_done', 'num_timed', 'num_requested', 'write_time', 'sync_time', 'buffers_written'],
        'wal': ['wal_bytes', 'wal_records', 'wal_fpi', 'wal_buffers_full']}.items()}
    def long_locks(sample):
        return sum((t.get('query_kind') == 'batch_advisory_lock' or t.get('query_prefix', '').startswith('WITH RECURSIVE requested'))
                   and (t.get('transaction_seconds') or 0) > 1 for t in sample.get('transactions') or [])
    containers = defaultdict(list)
    generators = []
    for sample in samples:
        if isinstance(sample.get('containers'), list):
            for reading in sample['containers']:
                containers[reading['Name']].append(reading)
        if isinstance(sample.get('load_generator'), list):
            generators.extend(sample['load_generator'])
    times = [timestamp(sample['at']) for sample in samples]
    result = {'schemaVersion': 1, 'resourceSamples': len(samples), 'databaseSamples': len(db), 'errors': errors,
              'firstAt': samples[0]['at'], 'lastAt': samples[-1]['at'],
              'maxSampleGapSeconds': max((b - a for a, b in zip(times, times[1:])), default=None),
              'counterDeltas': deltas, 'counterDeltaNote': 'Null means unavailable, reset or non-monotonic; never treated as zero.',
              'maxConnections': max((s['database']['connections'] for s in db), default=None),
              'maxConcurrentLongBatchLockTransactions': max(map(long_locks, db), default=None),
              'maxDatabaseLockWaiters': max((sum(g['count'] for g in s.get('sessions') or [] if g['wait_event_type'] == 'Lock') for s in db), default=None),
              'containers': {}, 'k6': {'samples': len(generators),
                  'peakRssGiB': max((p['rssKiB'] / 1048576 for p in generators), default=None),
                  'cpuP95Percent': quantile(sorted(p['cpuPercent'] for p in generators), .95)}, 'minuteResources': {}}
    for name, readings in containers.items():
        cpu = [float(c['CPUPerc'].rstrip('%')) for c in readings]
        valid = sorted(v for v in cpu if math.isfinite(v) and v >= 0)
        result['containers'][name] = {'samples': len(readings), 'cpuP95Percent': quantile(valid, .95),
            'cpuMaxPercent': max(valid, default=None), 'invalidCpuSamples': len(cpu) - len(valid),
            'peakMemoryGiB': max(memory_gib(c['MemUsage']) for c in readings)}
    if origin:
        groups = defaultdict(list)
        for sample in db:
            minute = int((timestamp(sample['at']) - timestamp(origin)) // 60)
            if minute >= 0:
                groups[minute].append(sample)
        result['minuteResources'] = {str(minute): {'samples': len(group),
            'maxConnections': max(s['database']['connections'] for s in group),
            'maxLongBatchLocks': max(map(long_locks, group))} for minute, group in sorted(groups.items())}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--summary', type=Path, required=True)
    parser.add_argument('--samples', type=Path, required=True)
    parser.add_argument('--resources', type=Path)
    parser.add_argument('--output-dir', type=Path, required=True)
    args = parser.parse_args()
    summary = json.loads(args.summary.read_text())
    if 'metrics' not in summary:
        parser.error('Expected an NGB handleSummary JSON file with a metrics object.')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    result = analyze_samples(args.samples, summary)
    (args.output_dir / 'analysis.json').write_text(json.dumps(result, indent=2) + '\n')
    if args.resources:
        resources = analyze_resources(args.resources, result['startEstimateUtc'])
        (args.output_dir / 'resources-analysis.json').write_text(json.dumps(resources, indent=2) + '\n')
    print(json.dumps({'outputDirectory': str(args.output_dir), 'summaryMatchesSamples': result['summaryMatchesSamples'],
                      'http': result['total']['http'], 'holds': list(result['holds'])}, indent=2))
    return 0 if result['summaryMatchesSamples'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
