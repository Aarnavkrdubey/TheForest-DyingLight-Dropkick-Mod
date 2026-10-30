param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\The Forest"
)

$ErrorActionPreference = "Stop"
$msbuild = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$projFile = Join-Path $PSScriptRoot "TheForestDropkickMod.csproj"

Write-Host "Building TheForestDropkickMod.csproj against assemblies in '$GameDir'..." -ForegroundColor Cyan
& $msbuild $projFile /p:Configuration=Release /p:TheForestDir="$GameDir" /t:Rebuild /v:minimal

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$dllPath = Join-Path $PSScriptRoot "bin\Release\TheForestDropkickMod.dll"
Write-Host "Build succeeded: $dllPath ($((Get-Item $dllPath).Length) bytes)" -ForegroundColor Green
