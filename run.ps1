<#
.SYNOPSIS
    StoreOS Local Development Runner (PowerShell).
.DESCRIPTION
    Starts the full StoreOS stack locally: Docker services, database migrations,
    demo tenant seeding, .NET 10 API, and Angular 21 frontend.
.EXAMPLE
    .\run.ps1
    .\run.ps1 --spa
    .\run.ps1 --watch
    .\run.ps1 --api-only
    .\run.ps1 --down
#>

[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ScriptArgs
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Look for python or python3
$pythonCmd = $null
foreach ($cmd in @("python", "python3", "py")) {
    $found = Get-Command $cmd -ErrorAction SilentlyContinue
    if ($found) {
        $pythonCmd = $found.Source
        break
    }
}

if (-not $pythonCmd) {
    Write-Host "[error] Python 3 is required to run StoreOS locally." -ForegroundColor Red
    Write-Host "Please install Python 3 (https://www.python.org/downloads/) or add it to PATH." -ForegroundColor Yellow
    exit 1
}

$runScript = Join-Path $ScriptDir "run.py"
& $pythonCmd $runScript @ScriptArgs
exit $LASTEXITCODE
