#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${repository_root}"

k6 version
bash run-backend-full-coverage.sh
bash run-frontend-full-coverage.sh
npm --prefix ui run test:browser:framework:cross-browser
node quality/external-consumer/test-tooling.mjs
npm --prefix performance-tests ci
npm --prefix performance-tests run typecheck
npm --prefix performance-tests run test:tooling
npm --prefix performance-tests run test:metric-grouping

# This receipt is written only after every complete gate above exits successfully.
node --input-type=module <<'JS'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { createHash } from 'node:crypto'

const reports = {}
for (const [gate, path] of Object.entries({
  backend: 'artifacts/coverage/backend-full/coverage-summary.json',
  frontend: 'artifacts/coverage/frontend-full/coverage-summary.json',
  tooling: 'artifacts/certification/tooling.json',
})) {
  const bytes = await readFile(path)
  reports[gate] = { path, sha256: createHash('sha256').update(bytes).digest('hex') }
}
await mkdir('artifacts/certification', { recursive: true })
await writeFile('artifacts/certification/full-quality.json', JSON.stringify({
  schemaVersion: 1, status: 'passed', reports,
  performance: 'Existing volume regression tests and offline diagnostic/metric contracts passed.',
}, null, 2) + '\n')
JS
