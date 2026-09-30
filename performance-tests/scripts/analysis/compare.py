#!/usr/bin/env python3
"""Compare two explicit NGB summaries without selecting baselines by date or filename."""
import argparse
import json
from pathlib import Path


def read_manifest(path):
    manifest = path.with_suffix('.manifest.json')
    return json.loads(manifest.read_text()) if manifest.exists() else None


def summarize(summary):
    metrics = summary['metrics']
    def value(metric, field):
        return metrics.get(metric, {}).get('values', {}).get(field)
    thresholds = [(name, expression, outcome.get('ok')) for name, metric in metrics.items()
                  for expression, outcome in metric.get('thresholds', {}).items()]
    return {'durationSeconds': summary.get('state', {}).get('testRunDurationMs', 0) / 1000,
            'httpRequests': value('http_reqs', 'count'), 'httpRps': value('http_reqs', 'rate'),
            'httpP95Ms': value('http_req_duration', 'p(95)'), 'httpP99Ms': value('http_req_duration', 'p(99)'),
            'httpErrors': value('http_req_failed', 'passes'),
            'businessErrors': value('ngb_business_operation_failed', 'passes'),
            'failedChecks': value('checks', 'fails'), 'drops': value('dropped_iterations', 'count'),
            'peakVU': value('vus', 'max'),
            'thresholdStatus': 'FAIL' if any(ok is False for _, _, ok in thresholds) else
                'PASS' if thresholds and all(ok is True for _, _, ok in thresholds) else 'UNKNOWN',
            'failedThresholds': [{'metric': name, 'expression': expression}
                                 for name, expression, ok in thresholds if ok is False]}


def normalized_settings(manifest):
    return {key: value for key, value in manifest.get('settings', {}).items()
            if key not in {'NGB_K6_SUMMARY_EXPORT', 'NGB_K6_TIME_SERIES_EXPORT', 'NGB_PERF_API_IMAGE_ID',
                           'NGB_PERF_DATASET_ID', 'NGB_PERF_RUN_LABEL'}}


def compare(old, new, old_manifest=None, new_manifest=None):
    old_compact, new_compact = summarize(old), summarize(new)
    warnings = []
    configs = [item.get('runConfiguration') for item in (old, new)]
    configuration_match = configs[0] == configs[1] if all(configs) else None
    if configuration_match is not True:
        warnings.append('Workload configurations differ or are missing; aggregate latency/RPS changes are descriptive only.')
    manifests = bool(old_manifest and new_manifest)
    settings_match = normalized_settings(old_manifest) == normalized_settings(new_manifest) if manifests else None
    if settings_match is not True:
        warnings.append('Manifest workload settings differ or are missing, including possible write/posting/fixture differences.')
    dataset_ids = [m.get('datasetId') if m else None for m in (old_manifest, new_manifest)]
    if not all(dataset_ids) or dataset_ids[0] != dataset_ids[1] or any(
            any(marker in str(v).lower() for marker in ['unrestored', 'not-restored', 'unknown']) for v in dataset_ids):
        warnings.append('Identical restored datasets are not established.')
    warnings.append('Verify container limits, cache state, competing activity and restoration records manually. Matching metadata is not proof of a controlled A/B.')
    common = old['metrics'].keys() & new['metrics'].keys()
    operations = []
    for name in sorted(common):
        if not name.startswith(('http_req_duration{', 'ngb_')) or 'operation:' not in name:
            continue
        a = old['metrics'][name].get('values', {}).get('p(95)')
        b = new['metrics'][name].get('values', {}).get('p(95)')
        # Empty diagnostic slices can contain zero trend values without observations.
        if a is not None and b is not None and a > 0 and b > 0:
            operations.append({'metric': name, 'oldP95Ms': a, 'newP95Ms': b, 'changePercent': (b / a - 1) * 100})
    return {'schemaVersion': 1, 'old': old_compact, 'new': new_compact,
            'runConfigurationMatch': configuration_match, 'manifestSettingsMatch': settings_match,
            'datasetIds': dataset_ids, 'warnings': warnings, 'operationP95Changes': operations}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('old', type=Path)
    parser.add_argument('new', type=Path)
    parser.add_argument('--output-dir', type=Path, required=True)
    args = parser.parse_args()
    result = compare(json.loads(args.old.read_text()), json.loads(args.new.read_text()),
                     read_manifest(args.old), read_manifest(args.new))
    result['sources'] = {'old': str(args.old), 'new': str(args.new)}
    args.output_dir.mkdir(parents=True, exist_ok=True)
    (args.output_dir / 'comparison.json').write_text(json.dumps(result, indent=2) + '\n')
    lines = ['# Performance comparison', '', f'Old: `{args.old.name}`. New: `{args.new.name}`.', '',
             '| Metric | Old | New |', '| --- | ---: | ---: |']
    for key, value in result['old'].items():
        if key != 'failedThresholds':
            lines.append(f'| {key} | {value if value is not None else "unavailable"} | {result["new"][key] if result["new"][key] is not None else "unavailable"} |')
    lines += ['', '## Comparison conditions', ''] + [f'- {warning}' for warning in result['warnings']]
    lines += ['', 'Threshold PASS does not mean zero errors. Missing metrics are unavailable, not zero.',
              'Operation p95 changes and failed threshold details are in comparison.json.', '']
    (args.output_dir / 'comparison.md').write_text('\n'.join(lines))
    print(f'Comparison saved to {args.output_dir}')


if __name__ == '__main__':
    main()
