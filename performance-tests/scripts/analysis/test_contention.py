"""Literal synthetic evidence tests: no NGB server or database mutations."""
import gzip
import json
from datetime import datetime, timezone, timedelta
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from contention import analyze


def at(second):
    return (datetime(2026, 9, 27, tzinfo=timezone.utc) + timedelta(seconds=second)).isoformat()


class ContentionTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.config = {'mode': 'overlap', 'rampSeconds': 5, 'holdSeconds': 40, 'durationSeconds': 50}
        self.events = [
            {'kind': 'snapshot', 'at': at(0), 'last_job_id': '1', 'job': None},
            {'kind': 'origin', 'at': at(0)},
            {'kind': 'trigger-attempt', 'at': at(14)},
            {'kind': 'triggered', 'id': '2', 'at': at(14)},
            {'kind': 'finalization-start', 'at': at(15)},
            {'kind': 'finalization-finished', 'at': at(25), 'finalized': 6},
            {'kind': 'finalization-summary', 'at': at(25), 'outcome': 'Succeeded'},
            {'kind': 'snapshot', 'at': at(30), 'last_job_id': '2', 'job': {'id': '2', 'state': 'Succeeded'}}]
        self.samples = []
        for second in range(1, 51):
            for metric, value, tags in [
                ('http_req_duration', 100, {'operation': 'platform.documents.post', 'status': '200'}),
                ('ngb_pm_posting_succeeded', 1, {'postingMode': 'fresh'}),
                ('http_req_duration', 120, {'operation': 'platform.reports.execute', 'reportId': 'trial', 'status': '429' if second == 20 else '200'})]:
                self.samples.append({'type': 'Point', 'metric': metric, 'data': {'time': at(second), 'value': value, 'tags': tags}})
        self.meters = []
        for second in range(1, 52):
            for name in ['active', 'queued']:
                self.meters.append({'kind': 'meter', 'eventName': 'GaugeValuePublished', 'at': at(second),
                                    'payload': {'instrumentName': f'ngb.report.admission.{name}', 'lastValue': '1'}})
            self.meters.append({'kind': 'meter', 'eventName': 'CounterRateValuePublished', 'at': at(second),
                                'payload': {'instrumentName': 'ngb.report.admission.requests', 'rate': '1', 'tags': 'outcome=acquired'}})
            self.meters.append({'kind': 'meter', 'eventName': 'HistogramValuePublished', 'at': at(second),
                                'payload': {'instrumentName': 'ngb.report.admission.duration', 'count': 1}})
        self.meters.append({'kind': 'collector-stopped', 'at': at(52)})

    def run_analysis(self):
        (self.root / 'contention-config.json').write_text(json.dumps(self.config))
        for name, values in [('contention.jsonl', self.events), ('report-admission.jsonl', self.meters)]:
            (self.root / name).write_text(''.join(json.dumps(value) + '\n' for value in values))
        with gzip.open(self.root / 'samples.json.gz', 'wt') as output:
            for sample in self.samples:
                output.write(json.dumps(sample) + '\n')
        return analyze(self.root)

    def test_actual_overlap_passes_with_exact_counts_and_status_timeline(self):
        result = self.run_analysis()
        self.assertTrue(result['passed'], result['failures'])
        self.assertEqual(result['job']['finalizedMonths'], 6)
        self.assertEqual(result['windows']['during']['successfulRentPosts'], 7)
        self.assertEqual(result['report429'], 1)
        self.assertEqual(result['reportRequests'], 50)
        self.assertEqual(result['windows']['during']['postLatencyMs']['p95'], 100)
        timeline = [json.loads(line) for line in (self.root / 'report-admission-timeline.jsonl').read_text().splitlines()]
        self.assertEqual(next(row for row in timeline if row['second'] == 20)['http']['statuses']['429'], 1)

    def test_empty_finalization_fails(self):
        self.events[5]['finalized'] = 0
        self.assertIn('Finalization completed without actual work.', self.run_analysis()['failures'])

    def test_hangfire_success_without_worker_success_is_insufficient(self):
        self.events[6]['outcome'] = 'SkippedOverlap'
        self.assertFalse(self.run_analysis()['passed'])

    def test_successful_posts_outside_finalization_do_not_prove_overlap(self):
        self.samples = [row for row in self.samples if not (row['metric'] == 'ngb_pm_posting_succeeded' and at(15) <= row['data']['time'] <= at(25))]
        self.assertIn('No verified fresh posting in during window.', self.run_analysis()['failures'])

    def test_one_post_failure_is_not_diluted_by_many_successes(self):
        self.samples[0]['data']['tags']['status'] = '500'
        self.assertFalse(self.run_analysis()['passed'])

    def test_window_latency_gate_is_not_diluted_by_the_whole_run(self):
        for row in self.samples:
            if row['metric'] == 'http_req_duration' and row['data']['tags']['operation'] == 'platform.documents.post' and row['data']['time'] == at(21):
                row['data']['value'] = 4000
        self.assertIn('Posting p95 exceeded 2500 ms in during.', self.run_analysis()['failures'])

    def test_missing_final_state_and_uncontrolled_job_fail(self):
        self.events[-1]['job']['state'] = 'Processing'
        self.events[-1]['last_job_id'] = '3'
        result = self.run_analysis()
        self.assertFalse(result['passed'])
        self.assertIn('Uncontrolled finalization job detected.', result['failures'])

    def test_missing_origin_fails_instead_of_inventing_a_time(self):
        self.events.pop(1)
        self.assertFalse(self.run_analysis()['passed'])

    def test_report_metric_gaps_and_unclean_stop_fail(self):
        self.meters = [event for event in self.meters if not (at(10) < event['at'] < at(30))][:-1]
        result = self.run_analysis()
        self.assertFalse(result['passed'])
        self.assertIn('Metric stream did not finish cleanly.', result['failures'])
        self.assertIn('Admission active telemetry has gaps exceeding 5 seconds.', result['failures'])

    def test_empty_idle_counter_values_are_not_fabricated_counts(self):
        for event in self.meters:
            if event.get('eventName') == 'CounterRateValuePublished' and event['at'] == at(5):
                event['payload']['rate'] = ''
        self.assertEqual(self.run_analysis()['admissionOutcomeDeltas']['outcome=acquired'], 50)

    def test_short_workload_fails(self):
        self.samples = [row for row in self.samples if row['data']['time'] <= at(30)]
        self.assertIn('Workload ended before the configured duration.', self.run_analysis()['failures'])

    def test_control_and_reports_allow_no_job_and_reject_contamination(self):
        for mode in ['control', 'reports']:
            self.config['mode'] = mode
            self.events = self.events[:2] + [{'kind': 'snapshot', 'at': at(50), 'last_job_id': '1', 'job': None}]
            self.assertTrue(self.run_analysis()['passed'])
            self.events.append({'kind': 'finalization-start', 'at': at(20)})
            self.assertFalse(self.run_analysis()['passed'])


if __name__ == '__main__':
    unittest.main()
