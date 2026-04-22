<#
.SYNOPSIS
    Run dotnet tests with minimal output.

.DESCRIPTION
    Runs dotnet tests with quiet verbosity and outputs results to a .trx file.
    Uses -v quiet --nologo to minimize console output.

.PARAMETER Filter
    Optional test filter expression (e.g., "Category=Unit")

.EXAMPLE
    .\dotnet-test.ps1
    .\dotnet-test.ps1 -Filter "Category=Integration"
#>

param(
    [string]$Filter
)

$ErrorActionPreference = "Stop"

# Setup paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDir = Resolve-Path (Join-Path $scriptDir "..")
$testResultsDir = Join-Path $solutionDir ".testresults"
$trxFile = Join-Path $testResultsDir "results.trx"

# Create test results directory if it doesn't exist
if (-not (Test-Path $testResultsDir)) {
    New-Item -ItemType Directory -Path $testResultsDir -Force | Out-Null
}

# Build the dotnet test command arguments
$testArgs = @(
    "test"
    "-v"
    "quiet"
    "--nologo"
    "--logger:trx;LogFileName=$trxFile"
)

if ($Filter) {
    $testArgs += "--filter"
    $testArgs += $Filter
}

# Run dotnet test
try {
    Push-Location $solutionDir

    # Run dotnet test (ignore exit code, we'll check .trx instead)
    & dotnet @testArgs

    # Always run test-errors.ts to parse results
    # Use -SummaryOnly because dotnet test stdout already shows error details
    $testErrorsScript = Join-Path $scriptDir "test-errors.ts"
    & bun $testErrorsScript --SummaryOnly

    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
