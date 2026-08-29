[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$TraceChaserRoot,
    [Parameter(Mandatory=$true)][string]$InputRepositoryRoot,
    [Parameter(Mandatory=$true)][string]$OutputRoot
)

$ErrorActionPreference = "Stop"
$policy = Join-Path $PSScriptRoot "../traces/output_policy.py"
$python = Get-Command python3 -ErrorAction SilentlyContinue
if ($null -eq $python) { $python = Get-Command python -ErrorAction SilentlyContinue }
if ($null -eq $python) { throw "Python is required to enforce canonical output-root safety" }
$resolved = & $python.Source $policy `
    --tracechaser-root $TraceChaserRoot `
    --input-repository-root $InputRepositoryRoot `
    --output-root $OutputRoot
if ($LASTEXITCODE -ne 0) { throw "OutputRoot must be absolute and outside both source trees" }
Write-Output $resolved
