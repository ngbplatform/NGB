#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ui_root="${repository_root}/ui"
coverage_root="${repository_root}/artifacts/coverage/frontend-full"
raw_root="${coverage_root}/raw"
report_root="${coverage_root}/report"
log_root="${coverage_root}/logs"
summary_file="${coverage_root}/coverage-summary.json"
coverage_jobs="${NGB_FRONTEND_COVERAGE_JOBS:-1}"

if [[ ! "${coverage_jobs}" =~ ^[1-9][0-9]*$ ]]; then
  echo "NGB_FRONTEND_COVERAGE_JOBS must be a positive integer; received: ${coverage_jobs}" >&2
  exit 1
fi
if [[ "${coverage_root}" != "${repository_root}/artifacts/coverage/frontend-full" ]]; then
  echo "Refusing to clean an unexpected coverage directory: ${coverage_root}" >&2
  exit 1
fi

rm -rf "${coverage_root}"
mkdir -p "${raw_root}" "${report_root}" "${log_root}"

cd "${ui_root}"
if [[ "${NGB_FRONTEND_COVERAGE_NO_INSTALL:-false}" == "true" ]]; then
  echo "Skipping npm ci because NGB_FRONTEND_COVERAGE_NO_INSTALL=true."
else
  npm ci
fi

node --test "${repository_root}/quality/coverage/verify-frontend-full-coverage.test.mjs"
node --test "${ui_root}/scripts/merge-frontend-full-coverage.test.mjs"

quality_failure=0
if npm run typecheck 2>&1 | tee "${log_root}/typecheck.log"; then
  :
else
  quality_failure=1
fi
if npm run test:api-compat 2>&1 | tee "${log_root}/api-compat.log"; then
  :
else
  quality_failure=1
fi

test_projects=()
while IFS= read -r project; do
  [[ -n "${project}" ]] && test_projects+=("${project}")
done < <(node "${ui_root}/scripts/discover-frontend-test-projects.mjs" "${ui_root}" vitest)
if [[ ${#test_projects[@]} -eq 0 ]]; then
  echo "No frontend Vitest projects were discovered." >&2
  exit 1
fi

run_vitest_project() {
  local descriptor="$1"
  local package_name config_name project_kind project_name project_report_root project_log
  IFS='|' read -r package_name config_name project_kind <<< "${descriptor}"
  project_name="${package_name}-${project_kind}"
  project_report_root="${raw_root}/${project_name}"
  project_log="${log_root}/${project_name}.log"

  echo "Running ${project_name} with coverage..." > "${project_log}"
  coverage_include='src/**/*.{ts,tsx,js,jsx,vue}'
  if [[ "${package_name}" == "ngb-ui-framework" && "${project_kind}" == "unit" ]]; then
    # The framework unit config intentionally has no Vue transform; its browser
    # project instruments every Vue SFC in the same package.
    coverage_include='src/**/*.{ts,tsx,js,jsx}'
  fi

  if NGB_UI_FRAMEWORK_BROWSER_MATRIX=chromium npx vitest run \
    --config "${package_name}/${config_name}" \
    --coverage \
    --coverage.provider v8 \
    --coverage.allowExternal \
    --coverage.reportOnFailure \
    --coverage.excludeAfterRemap \
    --coverage.include "${coverage_include}" \
    --coverage.exclude '**/*.d.ts' \
    --coverage.exclude '**/*.{g,generated,designer}.{ts,tsx,js,jsx}' \
    --coverage.reportsDirectory "${project_report_root}" \
    --coverage.reporter json >> "${project_log}" 2>&1; then
    if [[ ! -f "${project_report_root}/coverage-final.json" ]]; then
      echo "Coverage report was not produced for ${project_name}." >> "${project_log}"
      return 1
    fi
  else
    echo "Vitest project failed: ${project_name}" >> "${project_log}"
    return 1
  fi
}

echo "Running ${#test_projects[@]} Vitest projects with ${coverage_jobs} parallel project(s)."
test_pids=()
semaphore_path="${coverage_root}/.vitest-project-semaphore"
mkfifo "${semaphore_path}"
exec 9<> "${semaphore_path}"
rm "${semaphore_path}"
for ((slot = 0; slot < coverage_jobs; slot++)); do
  printf '\n' >&9
done

stop_parallel_tests() {
  local pid
  while IFS= read -r pid; do
    kill "${pid}" 2>/dev/null || true
  done < <(jobs -pr)
  exec 9>&-
  exit 130
}
trap stop_parallel_tests INT TERM

for project in "${test_projects[@]}"; do
  IFS= read -r -u 9
  IFS='|' read -r package_name config_name project_kind <<< "${project}"
  project_name="${package_name}-${project_kind}"
  {
    if run_vitest_project "${project}"; then
      printf '0\n' > "${log_root}/${project_name}.status"
    else
      printf '1\n' > "${log_root}/${project_name}.status"
    fi
    printf '\n' >&9
  } &
  test_pids+=("$!")
done
for pid in "${test_pids[@]}"; do
  wait "${pid}"
done
exec 9>&-
trap - INT TERM

vitest_failure=0
coverage_inputs=()
for project in "${test_projects[@]}"; do
  IFS='|' read -r package_name config_name project_kind <<< "${project}"
  project_name="${package_name}-${project_kind}"
  project_status="1"
  if [[ -f "${log_root}/${project_name}.status" ]]; then
    project_status="$(< "${log_root}/${project_name}.status")"
  fi
  if [[ "${project_status}" == "0" ]]; then
    echo "Passed: ${project_name}"
  else
    echo "Failed: ${project_name}" >&2
    vitest_failure=1
    cat "${log_root}/${project_name}.log" >&2
  fi
  if [[ -f "${raw_root}/${project_name}/coverage-final.json" ]]; then
    coverage_inputs+=(--input "${raw_root}/${project_name}/coverage-final.json")
  fi
done

if [[ ${#coverage_inputs[@]} -eq 0 ]]; then
  echo "No Vitest coverage reports were produced." >&2
  vitest_failure=1
else
  node "${ui_root}/scripts/merge-frontend-full-coverage.mjs" \
    "${coverage_inputs[@]}" \
    --output "${report_root}"
fi

e2e_failure=0
if [[ "${NGB_FRONTEND_COVERAGE_SKIP_E2E:-false}" == "true" ]]; then
  echo "Skipping Playwright E2E because NGB_FRONTEND_COVERAGE_SKIP_E2E=true."
else
  e2e_projects=()
  while IFS= read -r project; do
    [[ -n "${project}" ]] && e2e_projects+=("${project}")
  done < <(node "${ui_root}/scripts/discover-frontend-test-projects.mjs" "${ui_root}" e2e)
  echo "Running ${#e2e_projects[@]} Playwright E2E projects."
  for project in "${e2e_projects[@]}"; do
    IFS='|' read -r package_name config_name project_kind <<< "${project}"
    project_name="${package_name}-${project_kind}"
    if env -u NO_COLOR npx playwright test --config "${package_name}/${config_name}" > "${log_root}/${project_name}.log" 2>&1; then
      echo "Passed: ${project_name}"
    else
      echo "Failed: ${project_name}" >&2
      cat "${log_root}/${project_name}.log" >&2
      e2e_failure=1
    fi
  done
fi

gate_failure=0
if [[ ! -f "${report_root}/coverage-summary.json" ]]; then
  echo "Vitest coverage summary was not produced: ${report_root}/coverage-summary.json" >&2
  gate_failure=1
elif node "${repository_root}/quality/coverage/verify-frontend-full-coverage.mjs" \
  --repository-root "${repository_root}" \
  --workspace "${ui_root}" \
  --report "${report_root}/coverage-summary.json" \
  --output "${summary_file}" \
  --lines 100 \
  --branches 100 \
  --functions 100 \
  --statements 100; then
  :
else
  gate_failure=1
fi

echo "Frontend full coverage HTML report: ${report_root}/index.html"
echo "Frontend full coverage JSON summary: ${summary_file}"

if [[ "${quality_failure}" -ne 0 ]]; then
  echo "Frontend typecheck or API compatibility validation failed." >&2
fi
if [[ "${vitest_failure}" -ne 0 ]]; then
  echo "One or more frontend unit/browser tests failed." >&2
fi
if [[ "${e2e_failure}" -ne 0 ]]; then
  echo "One or more frontend Playwright E2E tests failed." >&2
fi
if [[ "${quality_failure}" -ne 0 || "${vitest_failure}" -ne 0 || "${e2e_failure}" -ne 0 || "${gate_failure}" -ne 0 ]]; then
  exit 1
fi
