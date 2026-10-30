param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\The Forest",
    [switch]$RemoveBepInExLoader
)

$ErrorActionPreference = "Stop"

$pluginDir = Join-Path $GameDir "BepInEx\plugins\TheForestDropkickMod"
$cfgFile = Join-Path $GameDir "BepInEx\config\com.theforest.dyinglightdropkick.cfg"

if (Test-Path $pluginDir) {
    Remove-Item -Recurse -Force $pluginDir
    Write-Host "Removed plugin folder: $pluginDir" -ForegroundColor Yellow
}

if (Test-Path $cfgFile) {
    Remove-Item -Force $cfgFile
    Write-Host "Removed config file: $cfgFile" -ForegroundColor Yellow
}

if ($RemoveBepInExLoader) {
    foreach ($item in @("winhttp.dll", "doorstop_config.ini", "BepInEx")) {
        $target = Join-Path $GameDir $item
        if (Test-Path $target) {
            Remove-Item -Recurse -Force $target
            Write-Host "Removed BepInEx component: $target" -ForegroundColor Yellow
        }
    }
}

Write-Host "Uninstallation complete. Original E keybindings are automatically active (no game files or PlayerPrefs were permanently altered)." -ForegroundColor Green
