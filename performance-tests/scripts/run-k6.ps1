[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $EnvFile,

    [Parameter(Mandatory = $true)]
    [string] $TestFile,

    [ValidateSet('local', 'cloud', 'prometheus-remote-write')]
    [string] $Output = 'local',

    [string] $SummaryExport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    throw "Env file not found: $EnvFile"
}

if (-not (Test-Path -LiteralPath $TestFile -PathType Leaf)) {
    throw "Test file not found: $TestFile"
}

if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) {
    throw 'k6 is not installed or is not on PATH.'
}

function Import-EnvFile {
    param([string] $Path)

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
            continue
        }

        $separator = $line.IndexOf('=')
        if ($separator -lt 1) {
            continue
        }

        $key = $line.Substring(0, $separator).Trim()
        $value = $line.Substring($separator + 1).TrimEnd("`r")

        if ($key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            throw "Invalid env key in $Path: $key"
        }

        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        if ([Environment]::GetEnvironmentVariable($key, 'Process') -ne $null) {
            continue
        }

        [Environment]::SetEnvironmentVariable($key, $value, 'Process')
    }
}

Import-EnvFile -Path $EnvFile

if ($SummaryExport) {
    [Environment]::SetEnvironmentVariable('NGB_K6_SUMMARY_EXPORT', $SummaryExport, 'Process')
} else {
    $existingSummaryPath = [Environment]::GetEnvironmentVariable('NGB_K6_SUMMARY_EXPORT', 'Process')
    if (-not $existingSummaryPath -or $existingSummaryPath -eq 'artifacts/k6-summary.json') {
        $testParts = $TestFile -split '[\\/]'
        $testPackage = $testParts[0]
        $testName = [System.IO.Path]::GetFileNameWithoutExtension($TestFile)
        $runTimestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
        [Environment]::SetEnvironmentVariable('NGB_K6_SUMMARY_EXPORT', "artifacts/$testPackage-$testName-$runTimestamp.summary.json", 'Process')
    }
}

$summaryPath = [Environment]::GetEnvironmentVariable('NGB_K6_SUMMARY_EXPORT', 'Process')
if ($summaryPath) {
    $summaryDirectory = Split-Path -Path $summaryPath -Parent
    if ($summaryDirectory) {
        New-Item -ItemType Directory -Path $summaryDirectory -Force | Out-Null
    }
}

Write-Host "Starting k6: test=$TestFile output=$Output env_file=$EnvFile summary_export=$summaryPath"

switch ($Output) {
    'local' {
        & k6 run $TestFile
    }
    'cloud' {
        & k6 cloud $TestFile
    }
    'prometheus-remote-write' {
        if (-not [Environment]::GetEnvironmentVariable('K6_PROMETHEUS_RW_SERVER_URL', 'Process')) {
            throw 'K6_PROMETHEUS_RW_SERVER_URL must be set for prometheus-remote-write output.'
        }

        & k6 run -o experimental-prometheus-rw $TestFile
    }
}
