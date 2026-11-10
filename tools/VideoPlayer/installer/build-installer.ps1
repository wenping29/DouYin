$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\VideoPlayer\VideoPlayer.csproj'
# Publish to a fresh empty directory every time: dotnet publish triggers a
# .NET 10 MSBuild CopyParallel Stack overflow when the target already has many
# files, and it still returns exit code 0 (silently packaging stale binaries).
$publishPath = Join-Path $repositoryRoot "artifacts\publish\win-x64-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$installerScript = Join-Path $PSScriptRoot 'VideoPlayer.iss'

$compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($compilerCommand) {
    $compilerPath = $compilerCommand.Source
}
else {
    $compilerCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $compilerPath = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $compilerPath) {
    throw 'Inno Setup 6 was not found. Install it and ensure ISCC.exe is on PATH.'
}

[xml]$project = Get-Content -Path $projectPath -Raw
$version = $project.Project.PropertyGroup |
    Where-Object { $_.Version } |
    Select-Object -First 1 -ExpandProperty Version

if (-not $version) {
    throw "The project version was not found in $projectPath."
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Stack overflow still returns 0, so verify the output before Inno Setup packs it.
$expectedDll = Join-Path $publishPath 'VideoPlayer.dll'
if (-not (Test-Path -LiteralPath $expectedDll)) {
    throw "Publish output is incomplete: $expectedDll was not found."
}
$fileCount = (Get-ChildItem -LiteralPath $publishPath -Recurse -File).Count
if ($fileCount -lt 1000) {
    throw "Publish output looks incomplete: only $fileCount files in $publishPath."
}

& $compilerPath "/DAppVersion=$version" "/DPublishDir=$publishPath" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

Write-Output "Publish directory : $publishPath ($fileCount files)"
Write-Output "Installer created in $repositoryRoot\artifacts\installer."
Write-Output "Note: older publish directories can be removed manually; the installer uses the directory shown above."
