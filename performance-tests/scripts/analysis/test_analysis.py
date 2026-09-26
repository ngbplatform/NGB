"""Offline contract tests use known expected values, not previous analyzer output."""
import gzip
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from analyze import analyze_samples, analyze_resources, counter_delta, memory_gib, hold_windows
from compare import compare, summarize


class AnalysisTests(unittest.TestCase):
    def test_counter_resets_and_missing_data_are_unavailable(self):
        def sample(value, reset):
            return {'wal': {'wal_bytes': value, 'stats_reset': reset}}
        self.assertEqual(counter_delta([sample(100, 'a'), sample(130, 'a')], 'wal', 'wal_bytes'), 30)
        self.assertIsNone(counter_delta([sample(100, 'a'), sample(5, 'b'), sample(150, 'a')], 'wal', 'wal_bytes'))
        self.assertIsNone(counter_delta([sample(100, 'a'), sample(5, 'a'), sample(150, 'a')], 'wal', 'wal_bytes'))
        self.assertIsNone(counter_delta([], 'wal', 'wal_bytes'))

    def test_missing_metrics_do_not_become_zero_or_pass(self):
        result = summarize({'metrics': {}})
        self.assertIsNone(result['drops'])
        self.assertEqual(result['thresholdStatus'], 'UNKNOWN')
        report = compare({'metrics': {}}, {'metrics': {}})
        self.assertIsNone(report['manifestSettingsMatch'])
        self.assertIsNone(report['runConfigurationMatch'])
        self.assertTrue(report['warnings'])

    def test_threshold_pass_does_not_hide_business_errors(self):
        result = summarize({'metrics': {'ngb_business_operation_failed': {
            'values': {'passes': 3}, 'thresholds': {'rate<0.01': {'ok': True}}}}})
        self.assertEqual(result['thresholdStatus'], 'PASS')
        self.assertEqual(result['businessErrors'], 3)

    def test_hold_windows_follow_saved_custom_schedule(self):
        summary = {'runConfiguration': {'scenarios': {'capacity': {'executor': 'ramping-vus', 'stages': [
            {'duration': '30s', 'target': 10}, {'duration': '2m', 'target': 10},
            {'duration': '1m', 'target': 25}, {'duration': '3m', 'target': 25}]}}}}
        windows = hold_windows(summary)
        self.assertEqual([(w['target'], w['startSeconds'], w['endSeconds']) for w in windows],
                         [(10, 32, 148), (25, 212, 388)])

    def test_sample_percentiles_and_truncation_against_literal_expectations(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'samples.json.gz'
            samples = []
            def point(metric, value, time, tags):
                samples.append({'type': 'Point', 'metric': metric,
                                'data': {'time': time, 'value': value, 'tags': tags}})
            tags = {'scenario': 'capacity', 'operation': 'post', 'name': 'post', 'url': 'post'}
            for i, value in enumerate([10, 20, 30, 40]):
                point('http_req_duration', value, f'2026-01-01T00:00:0{i + 1}Z', tags)
                point('http_req_failed', int(i == 3), f'2026-01-01T00:00:0{i + 1}Z', tags)
            point('iteration_duration', 5000, '2026-01-01T00:00:05Z', tags)
            point('iterations', 1, '2026-01-01T00:00:05Z', tags)
            point('vus', 10, '2026-01-01T00:00:03Z', {})
            summary = {'metrics': {'http_reqs': {'values': {'count': 4}},
                'http_req_duration': {'values': {'p(95)': 38.5, 'p(99)': 39.7, 'max': 40}},
                'http_req_failed': {'values': {'passes': 1}}, 'iterations': {'values': {'count': 1}}}}
            with gzip.open(path, 'wt') as stream:
                stream.write('\n'.join(json.dumps(p) for p in samples))
            result = analyze_samples(path, summary)
            self.assertTrue(result['summaryMatchesSamples'])
            self.assertEqual(result['total']['http']['p50'], 25)
            self.assertEqual(result['total']['counts']['httpErrors'], 1)
            self.assertIsNone(result['total']['counts']['drops'])
            self.assertEqual(result['cardinality']['distinctHttpDurationSeries'], 1)
            self.assertEqual(result['startEstimateUtc'], '2026-01-01T00:00:00+00:00')
            summary['metrics']['http_reqs']['values']['count'] = 5
            self.assertFalse(analyze_samples(path, summary)['summaryMatchesSamples'])

    def test_resource_errors_and_runs_longer_than_thirty_minutes(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'resources.jsonl'
            path.write_text(json.dumps({'at': '2026-01-01T01:02:00Z', 'containers': {'error': 'unavailable'},
                'load_generator': {'error': 'unavailable'}, 'postgres_mcp': {'rows': [{'sample': {
                    'at': '2026-01-01T01:02:00Z', 'database': {'connections': 7}, 'sessions': [],
                    'transactions': []}}]}}) + '\n')
            result = analyze_resources(path, '2026-01-01T00:00:00+00:00')
            self.assertEqual(result['minuteResources']['62']['maxConnections'], 7)
            self.assertEqual(len(result['errors']), 2)
            self.assertIsNone(result['k6']['peakRssGiB'])
            self.assertIsNone(result['counterDeltas']['database']['deadlocks'])
            self.assertAlmostEqual(memory_gib('1024MiB / 4GiB'), 1)
            self.assertAlmostEqual(memory_gib('1GB / 4GB'), 1e9 / 2**30)


if __name__ == '__main__':
    unittest.main()
