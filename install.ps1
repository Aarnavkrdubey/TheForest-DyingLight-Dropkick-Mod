param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\The Forest"
)

$ErrorActionPreference = "Stop"
$stageDir = Join-Path $PSScriptRoot "Release\PackageStaging"
$dllSource = Join-Path $PSScriptRoot "bin\Release\TheForestDropkickMod.dll"
$cfgSource = Join-Path $PSScriptRoot "config\com.theforest.dyinglightdropkick.cfg"

if (-not (Test-Path $GameDir)) {
    throw "The Forest installation directory not found at: $GameDir"
}

Write-Host "Installing BepInEx 5.4.23.5 (x64) and Dying Light Dropkick Mod to '$GameDir'..." -ForegroundColor Cyan

if (Test-Path $stageDir) {
    Copy-Item -Path "$stageDir\winhttp.dll" -Destination "$GameDir\winhttp.dll" -Force
    Copy-Item -Path "$stageDir\doorstop_config.ini" -Destination "$GameDir\doorstop_config.ini" -Force
    New-Item -ItemType Directory -Force -Path "$GameDir\BepInEx\core" | Out-Null
    Copy-Item -Path "$stageDir\BepInEx\core\*" -Destination "$GameDir\BepInEx\core\" -Force
}

New-Item -ItemType Directory -Force -Path "$GameDir\BepInEx\plugins\TheForestDropkickMod" | Out-Null
New-Item -ItemType Directory -Force -Path "$GameDir\BepInEx\config" | Out-Null

Copy-Item -Path $dllSource -Destination "$GameDir\BepInEx\plugins\TheForestDropkickMod\TheForestDropkickMod.dll" -Force

$destCfg = "$GameDir\BepInEx\config\com.theforest.dyinglightdropkick.cfg"
if (-not (Test-Path $destCfg)) {
    Copy-Item -Path $cfgSource -Destination $destCfg -Force
}

Write-Host "Installation complete! Launch The Forest normally via Steam." -ForegroundColor Green
