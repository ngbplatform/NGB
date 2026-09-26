"""202 requests to an ephemeral loopback fixture; run only between benchmarks."""
import json
import os
import subprocess
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import argparse
from datetime import datetime, timezone
from uuid import uuid4

parser = argparse.ArgumentParser(description=__doc__)
parser.parse_args()
script_root = Path(__file__).resolve().parent
workspace = script_root.parents[1]
root = workspace / 'artifacts' / 'probes' / (datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid4().hex[:8])
root.mkdir(parents=True)
label = 'after'
name = 'metric-grouping'
summary_path = root / f'{name}.summary.json'
samples_path = root / f'{name}.samples.json'
paths = []

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        paths.append(self.path)
        payload = json.dumps({'path': self.path}).encode()
        self.send_response(404 if self.path == '/documents/199?version=199' else 200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, *_):
        pass

with ThreadingHTTPServer(('127.0.0.1', 0), Handler) as server:
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    env = dict(os.environ, NGB_PROBE_API=f'http://127.0.0.1:{server.server_port}',
               NGB_PROBE_SUMMARY=str(summary_path), K6_NO_USAGE_REPORT='true')
    try:
        with (root/f'{name}.log').open('w') as log:
            run = subprocess.run(['k6','run','--out',f'json={samples_path}',str(script_root/'metric-grouping-probe.ts')],
                                 env=env, stdout=log, stderr=subprocess.STDOUT, timeout=45)
        assert run.returncode == 0, f'k6 exit {run.returncode}; inspect {name}.log'
    finally:
        server.shutdown()
        thread.join(timeout=5)

expected_paths = [f'/documents/{i}?version={i}' for i in range(200)] + ['/untagged/first','/untagged/second']
assert paths == expected_paths, 'Request routing changed.'
summary = json.loads(summary_path.read_text())['metrics']
assert summary['http_reqs']['values']['count'] == 202
assert summary['http_req_failed']['values']['passes'] == 1
assert summary['ngb_business_operation_failed']['values']['passes'] == 1
assert summary['checks{probe:routing}']['values']['fails'] == 0
points = []
with samples_path.open() as stream:
    for line in stream:
        record = json.loads(line)
        if record.get('type') == 'Point' and record.get('metric') == 'http_req_duration':
            points.append(record['data']['tags'])
assert len(points) == 202
known = [tags for tags in points if tags.get('operation') == 'platform.documents.open']
unknown = [tags for tags in points if not tags.get('operation')]
assert len(known) == 200 and len(unknown) == 2
distinct = len({json.dumps(tags,sort_keys=True) for tags in points})
assert all(tags['name'] == tags['url'] == 'platform.documents.open' for tags in known)
assert distinct == 4, f'Expected 2 grouped status series + 2 untagged URLs, got {distinct}.'
assert all('/untagged/' in tags['name'] and tags['name'] == tags['url'] for tags in unknown)
result = {'label':label,'requests':len(paths),'distinctHttpTimeSeries':distinct,
          'routingChecksPassed':202,'synthetic404CountedByHttpAndBusinessMetrics':True,
          'requestsWithoutOperationRetainUrl':True,'passed':True}
(root/f'{name}.verification.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result))
