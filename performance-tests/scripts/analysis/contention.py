#!/usr/bin/env python3
"""Offline acceptance and per-second report diagnostics for a controlled Hangfire overlap."""
from collections import Counter, defaultdict
import json
from pathlib import Path
import sys

from analyze import points, stats, timestamp


def lines(path):
    with path.open() as stream:
        for line in stream:
            yield json.loads(line)


def analyze(directory):
    config = json.loads((directory / 'contention-config.json').read_text())
    events = list(lines(directory / 'contention.jsonl'))
    failures = []
    def require(condition, message):
        if not condition:
            failures.append(message)
    by_kind = defaultdict(list)
    for event in events:
        by_kind[event['kind']].append(event)
    require(len(by_kind['origin']) == 1, 'Missing or ambiguous k6 scenario origin.')
    if not by_kind['origin']:
        return {'passed': False, 'failures': failures}
    origin = timestamp(by_kind['origin'][0]['at'])
    hold_start = origin + config['rampSeconds']
    hold_end = hold_start + config['holdSeconds']
    windows = {'hold': (hold_start, hold_end)}
    job = None
    if config['mode'] == 'overlap':
        starts, ends = by_kind['finalization-start'], by_kind['finalization-finished']
        require(len(by_kind['triggered']) == 1, 'Expected exactly one confirmed Hangfire enqueue.')
        require(len(starts) == len(ends) == 1, 'Expected exactly one finalization start and finish.')
        summaries = by_kind['finalization-summary']
        require(len(summaries) == 1 and summaries[0].get('outcome') == 'Succeeded', 'Missing successful worker summary.')
        if len(starts) == len(ends) == 1 and len(by_kind['triggered']) == 1:
            begin, end = timestamp(starts[0]['at']), timestamp(ends[0]['at'])
            job_id = by_kind['triggered'][0]['id']
            snapshots = [event['job'] for event in by_kind['snapshot'] if (event.get('job') or {}).get('id') == job_id]
            require(bool(snapshots) and snapshots[-1]['state'] == 'Succeeded', 'Triggered job did not reach Succeeded in MCP evidence.')
            require(ends[0]['finalized'] > 0, 'Finalization completed without actual work.')
            require(hold_start < begin < end < hold_end - 5, 'Finalization must complete inside the hold, leaving a recovery window.')
            require(end - begin > 4, 'Finalization was too brief to demonstrate overlap with the clock guard.')
            attempts = by_kind['trigger-attempt']
            require(len(attempts) == 1 and timestamp(attempts[0]['at']) <= begin + 1, 'Finalization started before the controlled trigger.')
            windows.update(before=(hold_start, begin - 2), during=(begin + 2, end - 2), after=(end + 2, hold_end))
            job = {'id': job_id, 'startedAt': starts[0]['at'], 'finishedAt': ends[0]['at'], 'finalizedMonths': ends[0]['finalized']}
    else:
        require(not by_kind['triggered'] and not by_kind['finalization-start'], 'Control/report run was contaminated by finalization.')

    # An unrelated scheduler/manual trigger invalidates attribution even when it succeeds.
    expected = by_kind['snapshot'][0].get('last_job_id') if by_kind['snapshot'] else None
    for event in events:
        if event['kind'] == 'triggered':
            expected = event['id']
        elif event['kind'] == 'snapshot':
            require(event.get('last_job_id') == expected, 'Uncontrolled finalization job detected.')
    require(len(by_kind['snapshot']) >= 2, 'Insufficient PostgreSQL MCP snapshots.')

    reports = defaultdict(lambda: {'requests': 0, 'statuses': Counter(), 'durationsMs': [], 'byReport': Counter()})
    buckets = {name: {'postDurations': [], 'postErrors': 0, 'successfulRentPosts': 0,
                      'reportDurations': [], 'reportStatuses': Counter()} for name in windows}
    total_posts = total_post_errors = 0
    last_point = origin
    for metric, data, at in points(directory / 'samples.json.gz', {'http_req_duration', 'ngb_pm_posting_succeeded'}):
        last_point = max(last_point, at)
        tags, value = data.get('tags', {}), data['value']
        posting = metric == 'http_req_duration' and tags.get('operation') == 'platform.documents.post'
        report = metric == 'http_req_duration' and tags.get('operation') == 'platform.reports.execute'
        if posting:
            total_posts += 1
            total_post_errors += tags.get('status') != '200'
        if report:
            second = int(at - origin)
            row = reports[second]
            row['requests'] += 1
            row['statuses'][tags.get('status', 'missing')] += 1
            row['durationsMs'].append(value)
            row['byReport'][f"{tags.get('reportId', 'missing')}|{tags.get('status', 'missing')}"] += 1
        for name, (begin, end) in windows.items():
            # HTTP requests must fit wholly in the window; boundary requests remain in raw samples.
            if begin <= at <= end and (metric != 'http_req_duration' or at - value / 1000 >= begin):
                row = buckets[name]
                if posting:
                    row['postDurations'].append(value)
                    row['postErrors'] += tags.get('status') != '200'
                elif report:
                    row['reportDurations'].append(value)
                    row['reportStatuses'][tags.get('status', 'missing')] += 1
                elif metric == 'ngb_pm_posting_succeeded' and tags.get('postingMode') == 'fresh':
                    row['successfulRentPosts'] += value
    require(total_posts > 0 and total_post_errors == 0, 'Posting is missing or has HTTP failures.')
    require(last_point >= origin + config['durationSeconds'] - 5, 'Workload ended before the configured duration.')
    require(bool(reports), 'No report execution requests were captured.')
    for name, row in buckets.items():
        require(row['successfulRentPosts'] > 0 and bool(row['postDurations']), f'No verified fresh posting in {name} window.')
        row['postLatencyMs'] = stats(row.pop('postDurations'))
        row['reportLatencyMs'] = stats(row.pop('reportDurations'))
        require(row['postLatencyMs']['p95'] is not None and row['postLatencyMs']['p95'] < 2500, f'Posting p95 exceeded 2500 ms in {name}.')
    require(config['mode'] != 'overlap' or 'during' in buckets, 'Actual overlap could not be established.')

    gauges = defaultdict(list)
    admissions = defaultdict(list)
    counters = Counter()
    histograms = 0
    stopped = False
    for event in lines(directory / 'report-admission.jsonl'):
        require(event['kind'] != 'error', 'Metric collector reported an error or exceeded a series limit.')
        stopped |= event['kind'] == 'collector-stopped'
        if event['kind'] != 'meter':
            continue
        payload = event.get('payload', {})
        instrument = payload.get('instrumentName', '')
        at = timestamp(event['at'])
        if at < origin or at > last_point + 3:
            continue
        second = int(at - origin)
        name = event.get('eventName')
        if name == 'GaugeValuePublished':
            gauges[instrument].append(at)
        elif name == 'CounterRateValuePublished' and instrument == 'ngb.report.admission.requests':
            if payload.get('rate') not in ('', None):
                counters[payload.get('tags', '')] += float(payload['rate'])
        elif name == 'HistogramValuePublished' and instrument == 'ngb.report.admission.duration':
            histograms += int(payload['count'])
        else:
            continue
        admissions[second].append({'instrument': instrument, **payload})
    require(stopped, 'Metric stream did not finish cleanly.')
    require(bool(counters) and histograms > 0, 'Admission counter or wait histogram is missing.')
    for name in ('active', 'queued'):
        observed = sorted(set(gauges[f'ngb.report.admission.{name}']))
        coverage = [origin, *observed, last_point]
        require(bool(observed) and max(b - a for a, b in zip(coverage, coverage[1:])) <= 5,
                f'Admission {name} telemetry has gaps exceeding 5 seconds.')
    require(sum(row['successfulRentPosts'] for name, row in buckets.items() if name == 'hold') > 0,
            'No successful fresh posting in the sustained load.')
    timeline = []
    for second in sorted(set(reports) | set(admissions)):
        row = reports.get(second)
        timeline.append({'second': second, 'http': None if row is None else {
            **row, 'durationsMs': stats(row['durationsMs'])}, 'admission': admissions.get(second, [])})
    with (directory / 'report-admission-timeline.jsonl').open('w') as output:
        for row in timeline:
            output.write(json.dumps(row) + '\n')
    return {'passed': not failures, 'failures': sorted(set(failures)), 'mode': config['mode'], 'job': job,
            'windows': buckets, 'totalPostingRequests': total_posts, 'totalPostingErrors': total_post_errors,
            'admissionOutcomeDeltas': counters,
            'report429': sum(row['statuses']['429'] for row in reports.values()),
            'reportRequests': sum(row['requests'] for row in reports.values()),
            'limitations': ['An overlap pass demonstrates coexistence in this run, not complete ledger/projection correctness.',
                            'Report metrics cover the shared accounting report workload; not all PM/Trade/AB/CRM reports.',
                            'Admission histogram quantiles are per collection interval; do not average them into global percentiles.',
                            'Resource samples are in resources.jsonl; short waits may occur between samples.']}


if __name__ == '__main__':
    directory = Path(sys.argv[1])
    try:
        result = analyze(directory)
    except (ValueError, KeyError, TypeError, OSError) as error:
        result = {'passed': False, 'failures': [f'Incomplete or invalid diagnostic evidence ({type(error).__name__}).']}
    (directory / 'contention-analysis.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({'passed': result['passed'], 'failures': result['failures']}))
    sys.exit(0 if result['passed'] else 2)
