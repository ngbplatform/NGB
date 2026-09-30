#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output_directory="${repository_root}/artifacts/nuget-release"
project_list="${repository_root}/packaging/nuget/projects.txt"
project_version="$(dotnet msbuild "${repository_root}/NGB.Tools/NGB.Tools.csproj" -nologo -getProperty:PackageVersion)"
package_validation="$(dotnet msbuild "${repository_root}/NGB.Tools/NGB.Tools.csproj" -nologo -getProperty:EnablePackageValidation)"
api_baseline="$(dotnet msbuild "${repository_root}/NGB.Tools/NGB.Tools.csproj" -nologo -getProperty:NgbPlatformApiCompatibilityBaselineVersion)"
assembly_version="$(dotnet msbuild "${repository_root}/NGB.Tools/NGB.Tools.csproj" -nologo -getProperty:AssemblyVersion)"
version="${1:-${project_version}}"

if [[ "${version}" != "${project_version}" ]]; then
  echo "Requested package version ${version} does not match Directory.Build.props version ${project_version}." >&2
  exit 1
fi

if [[ "${package_validation}" != "true" ]]; then
  echo "NuGet PackageValidation/ApiCompat must remain enabled for platform packages." >&2
  exit 1
fi

if [[ ! "${api_baseline}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Invalid NGB.Platform API compatibility baseline: ${api_baseline}." >&2
  exit 1
fi

if [[ "${version%%.*}" != "${api_baseline%%.*}" ]]; then
  echo "Release ${version} and API compatibility baseline ${api_baseline} must use the same major version." >&2
  exit 1
fi

if [[ "${assembly_version%%.*}" != "${version%%.*}" ]]; then
  echo "Assembly version ${assembly_version} and release ${version} must use the same major version." >&2
  exit 1
fi

projects=()
while IFS= read -r project; do
  [[ -z "${project}" || "${project}" =~ ^[[:space:]]*# ]] && continue
  projects+=("${project}")
done < "${project_list}"

for project in "${projects[@]}"; do
  project_file="${repository_root}/${project}"
  package_id="$(dotnet msbuild "${project_file}" -nologo -getProperty:PackageId)"
  test -f "${output_directory}/${package_id}.${version}.nupkg"
  test -f "${output_directory}/${package_id}.${version}.snupkg"
done

package_count="$(find "${output_directory}" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.snupkg' | wc -l | tr -d ' ')"
symbol_count="$(find "${output_directory}" -maxdepth 1 -type f -name '*.snupkg' | wc -l | tr -d ' ')"

if [[ "${package_count}" != "${#projects[@]}" || "${symbol_count}" != "${#projects[@]}" ]]; then
  echo "Expected ${#projects[@]} packages and symbols, found ${package_count} packages and ${symbol_count} symbols." >&2
  exit 1
fi

assert_package_entry() {
  local archive="$1"
  local entry="$2"
  local resolved_entry

  resolved_entry="$(unzip -Z1 "${archive}" "${entry}")"
  if [[ "${resolved_entry}" != "${entry}" ]]; then
    echo "Package $(basename "${archive}") is missing required entry: ${entry}" >&2
    exit 1
  fi
}

assert_package_entry \
  "${output_directory}/NGB.Platform.BackgroundJobs.${version}.nupkg" \
  "contentFiles/any/any/hangfire-dashboard.css"
assert_package_entry \
  "${output_directory}/NGB.Platform.Watchdog.${version}.nupkg" \
  "contentFiles/any/any/dashboard.css"

echo "Verified ${package_count} NGB.Platform ${version} packages, API compatibility configuration, and required content assets."
