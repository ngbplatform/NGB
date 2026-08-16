#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
solution="${repository_root}/NGB.sln"
runsettings="${repository_root}/quality/coverage/backend-full-coverage.runsettings"
coverage_root="${repository_root}/artifacts/coverage/backend-full"
raw_root="${coverage_root}/raw"
report_root="${coverage_root}/report"
log_root="${coverage_root}/logs"
summary_file="${coverage_root}/coverage-summary.json"
coverage_jobs="${NGB_BACKEND_COVERAGE_JOBS:-2}"

if [[ ! "${coverage_jobs}" =~ ^[1-9][0-9]*$ ]]; then
  echo "NGB_BACKEND_COVERAGE_JOBS must be a positive integer; received: ${coverage_jobs}" >&2
  exit 1
fi

if [[ "${coverage_root}" != "${repository_root}/artifacts/coverage/backend-full" ]]; then
  echo "Refusing to clean an unexpected coverage directory: ${coverage_root}" >&2
  exit 1
fi

rm -rf "${coverage_root}"
mkdir -p "${raw_root}" "${report_root}" "${log_root}"

cd "${repository_root}"

dotnet tool restore
node --test "${repository_root}/quality/coverage/verify-backend-full-coverage.test.mjs"

test_projects=()
while IFS= read -r project; do
  [[ -z "${project}" ]] && continue
  if [[ "${project}" =~ Tests\.csproj$ ]] \
    || grep -Eq '<IsTestProject>[[:space:]]*true[[:space:]]*</IsTestProject>|Microsoft\.NET\.Test\.Sdk' "${repository_root}/${project}"; then
    test_projects+=("${project}")
  fi
done < <(dotnet sln "${solution}" list | sed -n '/\.csproj$/p')

if [[ ${#test_projects[@]} -eq 0 ]]; then
  echo "No backend test projects were discovered in ${solution}." >&2
  exit 1
fi

echo "Discovered ${#test_projects[@]} backend test projects."

if [[ "${NGB_BACKEND_COVERAGE_NO_RESTORE:-false}" == "true" ]]; then
  echo "Skipping solution restore because NGB_BACKEND_COVERAGE_NO_RESTORE=true."
else
  dotnet restore "${solution}"
fi
if [[ "${NGB_BACKEND_COVERAGE_NO_BUILD:-false}" == "true" ]]; then
  echo "Skipping solution build because NGB_BACKEND_COVERAGE_NO_BUILD=true."
else
  dotnet build "${solution}" --configuration Release --no-restore -m:1
fi

run_test_project() {
  local project="$1"
  local project_name="$(basename "${project}" .csproj)"
  local project_log="${log_root}/${project_name}.log"
  local project_report

  echo "Running ${project_name} with coverage..." > "${project_log}"
  if NGB_RUN_VOLUME_TESTS=true dotnet test "${repository_root}/${project}" \
    --configuration Release \
    --no-build \
    --no-restore \
    -m:1 \
    --settings "${runsettings}" \
    --collect:"XPlat Code Coverage" \
    --results-directory "${raw_root}/${project_name}" >> "${project_log}" 2>&1; then
    project_report="$(find "${raw_root}/${project_name}" -name 'coverage.cobertura.xml' -type f -print -quit)"
    if [[ -z "${project_report}" ]]; then
      echo "Coverage report was not produced for: ${project}" >> "${project_log}"
      return 1
    fi
  else
    echo "Test project failed: ${project}" >> "${project_log}"
    return 1
  fi
}

echo "Running coverage with ${coverage_jobs} parallel test project(s)."
test_failures=0
test_pids=()
semaphore_path="${coverage_root}/.test-project-semaphore"
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
  project_name="$(basename "${project}" .csproj)"

  {
    if run_test_project "${project}"; then
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

for project in "${test_projects[@]}"; do
  project_name="$(basename "${project}" .csproj)"
  project_status="1"
  if [[ -f "${log_root}/${project_name}.status" ]]; then
    project_status="$(< "${log_root}/${project_name}.status")"
  fi

  if [[ "${project_status}" == "0" ]]; then
    echo "Passed: ${project_name}"
  else
    echo "Failed: ${project_name}" >&2
    test_failures=$((test_failures + 1))
  fi

  cat "${log_root}/${project_name}.log"
done

report_count="$(find "${raw_root}" -name 'coverage.cobertura.xml' -type f | wc -l | tr -d '[:space:]')"
if [[ "${report_count}" -eq 0 ]]; then
  echo "No Cobertura reports were produced." >&2
  exit 1
fi

echo "Merging ${report_count} coverage reports..."
dotnet reportgenerator \
  "-reports:${raw_root}/**/coverage.cobertura.xml" \
  "-targetdir:${report_root}" \
  "-reporttypes:Html;Cobertura;TextSummary" \
  "-assemblyfilters:+NGB.*;-*.Tests;-*.IntegrationTests" \
  "-filefilters:-**/bin/**;-**/obj/**"

gate_failure=0
if node "${repository_root}/quality/coverage/verify-backend-full-coverage.mjs" \
  --repository-root "${repository_root}" \
  --solution "${solution}" \
  --report "${report_root}/Cobertura.xml" \
  --output "${summary_file}" \
  --lines 100 \
  --branches 100 \
  --methods 100; then
  :
else
  gate_failure=1
fi

echo "Backend full coverage HTML report: ${report_root}/index.html"
echo "Backend full coverage JSON summary: ${summary_file}"

if [[ "${test_failures}" -ne 0 ]]; then
  echo "${test_failures} backend test project(s) failed." >&2
fi
if [[ "${test_failures}" -ne 0 || "${gate_failure}" -ne 0 ]]; then
  exit 1
fi
