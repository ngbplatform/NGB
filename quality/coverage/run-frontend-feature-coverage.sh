#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
base_ref="${NGB_COVERAGE_BASE_REF:-}"

cd "${repository_root}/ui"
npm run test:coverage:feature
npm run test:coverage:diff

if [[ -n "${base_ref}" ]]; then
  node "${repository_root}/quality/coverage/verify-lcov-diff.mjs" \
    --report "${repository_root}/artifacts/coverage/frontend-diff/lcov.info" \
    --base-ref "${base_ref}"
elif [[ "${CI:-false}" == "true" ]]; then
  echo "NGB_COVERAGE_BASE_REF is required in CI for the frontend diff-coverage gate." >&2
  exit 1
else
  echo "Frontend diff coverage skipped locally. Set NGB_COVERAGE_BASE_REF to enable it."
fi
