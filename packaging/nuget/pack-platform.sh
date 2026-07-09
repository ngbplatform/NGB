#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${1:-1.3.0}"
output_directory="${repository_root}/artifacts/nuget-release"
project_list="${repository_root}/packaging/nuget/projects.txt"
project_version="$(dotnet msbuild "${repository_root}/NGB.Tools/NGB.Tools.csproj" -nologo -getProperty:Version)"

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

echo "Packed ${package_count} NGB.Platform packages and ${symbol_count} symbol packages for version ${version}."
