#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output_directory="${repository_root}/artifacts/nuget-release"
project_list="${repository_root}/packaging/nuget/projects.txt"
project_version="$(dotnet msbuild "${repository_root}/NGB.Tools/NGB.Tools.csproj" -nologo -getProperty:PackageVersion)"
version="${1:-${project_version}}"

if [[ "${version}" != "${project_version}" ]]; then
  echo "Requested package version ${version} does not match Directory.Build.props version ${project_version}." >&2
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

unzip -Z1 "${output_directory}/NGB.Platform.BackgroundJobs.${version}.nupkg" \
  | grep -Fxq contentFiles/any/any/hangfire-dashboard.css
unzip -Z1 "${output_directory}/NGB.Platform.Watchdog.${version}.nupkg" \
  | grep -Fxq contentFiles/any/any/dashboard.css

echo "Verified ${package_count} NGB.Platform ${version} packages and required content assets."
