#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output_directory="${repository_root}/artifacts/nuget-release"
local_feed_directory="${repository_root}/artifacts/nuget"
local_package_cache="${repository_root}/artifacts/nuget-cache"
project_list="${repository_root}/packaging/nuget/projects.txt"
project_version="$(dotnet msbuild "${repository_root}/NGB.Tools/NGB.Tools.csproj" -nologo -getProperty:PackageVersion)"
version="${1:-${project_version}}"

if [[ ! "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid package version: ${version}" >&2
  exit 1
fi

if [[ "${version}" != "${project_version}" ]]; then
  echo "Requested version ${version} does not match Directory.Build.props version ${project_version}." >&2
  exit 1
fi

rm -rf "${output_directory}"
mkdir -p "${output_directory}"
mkdir -p "${local_feed_directory}"

while IFS= read -r project; do
  [[ -z "${project}" ]] && continue

  dotnet pack "${repository_root}/${project}" \
    --configuration Release \
    --output "${output_directory}" \
    -p:ContinuousIntegrationBuild=true \
    -p:PackageVersion="${version}"
done < "${project_list}"

expected_count="$(grep -cve '^[[:space:]]*$' "${project_list}")"
package_count="$(find "${output_directory}" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.snupkg' | wc -l | tr -d ' ')"
symbol_count="$(find "${output_directory}" -maxdepth 1 -type f -name '*.snupkg' | wc -l | tr -d ' ')"

if [[ "${package_count}" != "${expected_count}" || "${symbol_count}" != "${expected_count}" ]]; then
  echo "Expected ${expected_count} packages and symbols, found ${package_count} packages and ${symbol_count} symbols." >&2
  exit 1
fi

find "${local_feed_directory}" -maxdepth 1 -type f \( -name '*.nupkg' -o -name '*.snupkg' \) -delete
cp "${output_directory}"/*.nupkg "${local_feed_directory}/"
cp "${output_directory}"/*.snupkg "${local_feed_directory}/"

while IFS= read -r project; do
  [[ -z "${project}" ]] && continue

  package_id="$(dotnet msbuild "${repository_root}/${project}" -nologo -getProperty:PackageId)"
  package_id_lower="$(printf '%s' "${package_id}" | tr '[:upper:]' '[:lower:]')"
  cache_version_directory="${local_package_cache}/${package_id_lower}/${version}"

  if [[ "${cache_version_directory}" != "${local_package_cache}/"* ]]; then
    echo "Refusing to invalidate an unexpected package cache path: ${cache_version_directory}" >&2
    exit 1
  fi

  rm -rf "${cache_version_directory}"
done < "${project_list}"

echo "Packed ${package_count} NGB.Platform packages and ${symbol_count} symbol packages for version ${version}."
echo "Refreshed local NuGet feed in ${local_feed_directory}."
echo "Invalidated replaced NGB.Platform packages in ${local_package_cache}."
