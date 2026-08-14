#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
coverage_root="${repository_root}/artifacts/coverage/backend"
raw_root="${coverage_root}/raw"
report_root="${coverage_root}/report"
base_ref="${NGB_COVERAGE_BASE_REF:-}"

rm -rf "${coverage_root}"
mkdir -p "${raw_root}" "${report_root}"

dotnet tool restore
node --test "${repository_root}/quality/coverage/verify-cobertura-diff.test.mjs"

test_projects=(
  "NGB.Runtime.Tests/NGB.Runtime.Tests.csproj"
  "NGB.PostgreSql.Tests/NGB.PostgreSql.Tests.csproj"
  "NGB.CRM.Runtime.Tests/NGB.CRM.Runtime.Tests.csproj"
  "NGB.PropertyManagement.Api.IntegrationTests/NGB.PropertyManagement.Api.IntegrationTests.csproj"
)

for project in "${test_projects[@]}"; do
  project_name="$(basename "$(dirname "${project}")")"
  test_args=(
    "${repository_root}/${project}"
    --configuration Release
    --no-restore
    -m:1
  )
  if [[ "${project_name}" == "NGB.PropertyManagement.Api.IntegrationTests" ]]; then
    # External Keycloak reachability is covered by the normal integration suite.
    # It is intentionally excluded from the Document Actions + Work Center
    # coverage run because it is unrelated and can time out under the profiler.
    test_args+=(--filter "FullyQualifiedName!~PmApi_Health_HttpSurface_P0Tests")
  fi
  dotnet test "${test_args[@]}" \
    --collect:"XPlat Code Coverage" \
    --results-directory "${raw_root}/${project_name}" \
    -- \
    DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
done

dotnet reportgenerator \
  "-reports:${raw_root}/**/coverage.cobertura.xml" \
  "-targetdir:${report_root}" \
  "-reporttypes:Html;Cobertura;TextSummary" \
  "-filefilters:+*NGB.Api/Controllers/DocumentControllerBase.cs;+*NGB.Api/Controllers/WorkCenterControllerBase.cs;+*NGB.Api/WorkCenter/*;+*NGB.Core/Documents/Actions/*;+*NGB.Core/Events/*;+*NGB.Core/WorkCenter/*;+*NGB.Contracts/Documents/*;+*NGB.Contracts/WorkCenter/*;+*NGB.Definitions/Documents/Actions/*;+*NGB.Metadata/Documents/Actions/*;+*NGB.Persistence/Documents/Actions/*;+*NGB.Persistence/Outbox/*;+*NGB.Persistence/WorkCenter/*;+*NGB.PostgreSql/Documents/Actions/*;+*NGB.PostgreSql/Outbox/*;+*NGB.PostgreSql/WorkCenter/*;+*NGB.Runtime/Documents/Actions/*;+*NGB.Runtime/Observability/*;+*NGB.Runtime/WorkCenter/*;+*NGB.PropertyManagement.Runtime/DocumentActions/*;+*NGB.PropertyManagement.Runtime/WorkCenter/*;+*NGB.CRM.Runtime/DocumentActions/*;+*NGB.CRM.Runtime/WorkCenter/*"

node "${repository_root}/quality/coverage/verify-cobertura.mjs" \
  --report "${report_root}/Cobertura.xml" \
  --lines 100 \
  --branches 100

if [[ -n "${base_ref}" ]]; then
  node "${repository_root}/quality/coverage/verify-cobertura-diff.mjs" \
    --report "${report_root}/Cobertura.xml" \
    --base-ref "${base_ref}"
elif [[ "${CI:-false}" == "true" ]]; then
  echo "NGB_COVERAGE_BASE_REF is required in CI for the 100% diff-coverage gate." >&2
  exit 1
else
  echo "Diff coverage skipped locally. Set NGB_COVERAGE_BASE_REF to enable it."
fi

echo "Document Actions + Work Center backend coverage report: ${report_root}/index.html"
