<#
.SYNOPSIS
  Builds and runs the end-to-end tests to completion, printing a summary.

.DESCRIPTION
  Stops testhost processes left by an interrupted run (they lock Vox.Core.dll and make the next
  build fail), stops if the build fails (so tests never run against a stale binary), runs the tests
  with the full output going to a log file, and prints the result and the failed test names.
  Don't pipe the run into a truncating filter (head, Select-Object -First): that abandons the
  test host mid-run.

.EXAMPLE
  tools/run-e2e.ps1                  # every E2E test
  tools/run-e2e.ps1 CorpusTests      # tests whose name contains CorpusTests
#>
param([string]$Filter = "")

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet build tests/Vox.E2E.Tests --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed; not running the tests."; exit 1 }

$log = Join-Path ([IO.Path]::GetTempPath()) "vox-e2e.log"
$env:VOX_E2E = "1"
$testArgs = @("test", "tests/Vox.E2E.Tests", "--no-build")
if ($Filter) { $testArgs += @("--filter", "FullyQualifiedName~$Filter") }
$ErrorActionPreference = "Continue"
dotnet @testArgs *> $log
$code = $LASTEXITCODE

Select-String -Path $log -Pattern "^\s*(Passed|Failed)!" | ForEach-Object { $_.Line.Trim() }
Select-String -Path $log -Pattern "^\s*Failed Vox" | ForEach-Object { $_.Line.Trim() }
Write-Host "Full output: $log"
exit $code
