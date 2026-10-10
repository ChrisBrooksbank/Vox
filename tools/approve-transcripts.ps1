# Approves the end-to-end transcripts a run received: each tests/Vox.E2E.Tests/Transcripts/<name>.received.txt
# replaces <name>.approved.txt. Review the differences first (git diff shows them after approving).
param([string]$Name = "*")

$directory = Join-Path $PSScriptRoot "..\tests\Vox.E2E.Tests\Transcripts"
$received = Get-ChildItem -Path $directory -Filter "$Name.received.txt" -ErrorAction SilentlyContinue
if (-not $received) {
    Write-Host "No received transcripts to approve."
    exit 0
}
foreach ($file in $received) {
    $approved = Join-Path $file.DirectoryName ($file.Name -replace '\.received\.txt$', '.approved.txt')
    Move-Item -Force $file.FullName $approved
    Write-Host "Approved $(Split-Path -Leaf $approved)"
}
