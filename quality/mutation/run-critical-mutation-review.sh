#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

dotnet test "${repository_root}/NGB.Runtime.Tests/NGB.Runtime.Tests.csproj" \
  --no-restore \
  -m:1 \
  --filter "FullyQualifiedName~DocumentActionPlatformTests|FullyQualifiedName~DocumentActionDefinitionAndCoreCoverageTests|FullyQualifiedName~DocumentActionEvaluatorCoverageTests|FullyQualifiedName~WorkCenterServicesTests|FullyQualifiedName~OutboxProcessorTests"

dotnet test "${repository_root}/NGB.CRM.Runtime.Tests/NGB.CRM.Runtime.Tests.csproj" \
  --no-restore \
  -m:1 \
  --filter "FullyQualifiedName~CrmWorkCenterPolicyTests"

dotnet test "${repository_root}/NGB.PropertyManagement.Api.IntegrationTests/NGB.PropertyManagement.Api.IntegrationTests.csproj" \
  --no-restore \
  -m:1 \
  --filter "FullyQualifiedName~PropertyManagementWorkCenterPolicyTests|FullyQualifiedName~PmWorkCenter_HttpAndPersistence_P0Tests"

echo "Critical mutation-equivalent negative-test review passed."
