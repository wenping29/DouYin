# Publish script - WorkLogTool
param(
    [string]$Configuration = 'Release',
    [switch]$Clean,
    [string]$OutputPath = './publish'
)

$ErrorActionPreference = 'Stop'
$ProjectPath = '$PSScriptRoot/WorkLogTool.csproj'

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  WorkLogTool Publish Script' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan

if ($Clean) {
    Write-Host '
[1/3] Cleaning...' -ForegroundColor Yellow
    if (Test-Path $OutputPath) {
        Remove-Item -Recurse -Force $OutputPath
    }
}

Write-Host '
[2/3] Restoring packages...' -ForegroundColor Yellow
dotnet restore $ProjectPath
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }

Write-Host '
[3/3] Publishing...' -ForegroundColor Yellow
dotnet publish $ProjectPath -c $Configuration -o $OutputPath -p:PublishSingleFile=true -p:SelfContained=true -p:RuntimeIdentifier=win-x64

if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

$ExePath = '$OutputPath/WorkLogTool.exe'
if (Test-Path $ExePath) {
    $FileSize = (Get-Item $ExePath).Length / 1MB
    Write-Host '
========================================' -ForegroundColor Green
    Write-Host '  Publish SUCCESS!' -ForegroundColor Green
    Write-Host '========================================' -ForegroundColor Green
    Write-Host ('Size: ' + [math]::Round($FileSize, 2) + ' MB')
}
