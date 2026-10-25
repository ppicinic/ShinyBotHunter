# Installs the bot's Dolphin configuration from Dolphin\Config into <DolphinBaseDirectory>\User-ShinyBotHunter.
#
# The bot launches Dolphin with -u on that folder, so it gets its own user directory and the
# operator's own Dolphin settings are never touched. The files set the integrated GBA on port 2
# with this machine's BIOS, background input, and GCPad1 driven by the bot's DSU (cemuhook) pad
# server; in this Dolphin build the DSU server list key is Entries, not Servers. The ini files carry
# no comments because Dolphin drops them when it rewrites its config on exit, so the reasons live
# here. The ROM and the saves path are not set here: they are chosen per launch.
#
# Paths come from the same sources the backend reads: appsettings.json, then user secrets
# (flat "ApplicationSettings:<Key>" entries, as dotnet user-secrets set writes them) on top.
param(
    [string]$RepoDir = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

if (Get-Process Dolphin -ErrorAction SilentlyContinue) {
    throw "Dolphin is running. Close it first: it rewrites its config files when it exits, which would undo this deploy."
}

$settings = (Get-Content "$RepoDir\ShinyBotHunter\appsettings.json" -Raw | ConvertFrom-Json).ApplicationSettings
$dolphinDir = $settings.DolphinBaseDirectory
$biosPath = $settings.GbaBiosPath

$secretsId = ([xml](Get-Content "$RepoDir\ShinyBotHunter\ShinyBotHunter.csproj" -Raw)).Project.PropertyGroup.UserSecretsId | Where-Object { $_ }
$secretsFile = "$env:APPDATA\Microsoft\UserSecrets\$secretsId\secrets.json"
if (Test-Path $secretsFile) {
    $secrets = Get-Content $secretsFile -Raw | ConvertFrom-Json
    if ($secrets."ApplicationSettings:DolphinBaseDirectory") { $dolphinDir = $secrets."ApplicationSettings:DolphinBaseDirectory" }
    if ($secrets."ApplicationSettings:GbaBiosPath") { $biosPath = $secrets."ApplicationSettings:GbaBiosPath" }
}

if (-not (Test-Path "$dolphinDir/Dolphin.exe" -PathType Leaf)) {
    throw "Dolphin.exe not found in $dolphinDir; set ApplicationSettings:DolphinBaseDirectory in user secrets."
}
if (-not (Test-Path $biosPath -PathType Leaf)) {
    throw "GBA BIOS not found at $biosPath; set ApplicationSettings:GbaBiosPath in user secrets."
}

$userDir = Join-Path $dolphinDir "User-ShinyBotHunter"
$configDir = Join-Path $userDir "Config"
New-Item -ItemType Directory -Force $configDir | Out-Null

Copy-Item "$RepoDir\Dolphin\Config\DSUClient.ini" $configDir -Force
Copy-Item "$RepoDir\Dolphin\Config\GCPadNew.ini" $configDir -Force

# String.Replace, not -replace, so backslashes and $ in the path stay literal; written without a
# BOM, which PowerShell 5.1's Set-Content -Encoding utf8 would put in front of [Core].
$ini = [IO.File]::ReadAllText("$RepoDir\Dolphin\Config\Dolphin.ini").Replace("{GbaBiosPath}", $biosPath)
[IO.File]::WriteAllText((Join-Path $configDir "Dolphin.ini"), $ini, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Deployed Dolphin config to $userDir with GBA BIOS $biosPath"
