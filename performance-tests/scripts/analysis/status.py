#!/usr/bin/env python3
"""Read saved run state and recent samples; never start, stop or query a live workload."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('run_directory', type=Path)
args = parser.parse_args()
state = json.loads((args.run_directory / 'run.json').read_text())
resource_file = args.run_directory / 'resources.jsonl'
latest = None
if resource_file.exists():
    with resource_file.open('rb') as stream:
        stream.seek(max(0, resource_file.stat().st_size - 512 * 1024))
        for line in reversed(stream.read().splitlines()):
            try:
                latest = json.loads(line)
                break
            except (ValueError, UnicodeDecodeError):
                continue
if latest:
    age = (datetime.now(timezone.utc) - datetime.fromisoformat(latest['at'].replace('Z', '+00:00'))).total_seconds()
    state['latestResourceSampleAt'] = latest['at']
    state['resourceSampleAgeSeconds'] = round(age, 1)
    state['sampleFresh'] = age < 30
state['note'] = 'Saved state is not proof of a live process. A stale running state may indicate interruption or host sleep.'
print(json.dumps(state, indent=2))
