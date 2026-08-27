# Builds ApiHawk and deploys BotServer.dll into the BizHawk install, marking it trusted.
#
# The trust step is why this script exists. BizHawk records external tools it may load without
# asking as (dll path -> SHA512) in config.ini, so every fresh build -- new hash -- makes EmuHawk
# stop on a yes/no dialog. Headless emulators never get their yes: the tool never starts, the
# bot's readiness check times out, and the hunt dies on what looks like an unresponsive emulator.
# Writing the new hash alongside the DLL is what a redeploy actually consists of.
param(
    [string]$BizHawkDir = "D:\BizHawk-2.11",
    [string]$RepoDir = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

if (Get-Process EmuHawk -ErrorAction SilentlyContinue) {
    throw "EmuHawk is running. Close it first: the DLL is locked, and a config written now would be overwritten when it exits."
}

dotnet build "$RepoDir\ApiHawk\ApiHawk.csproj" -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "ApiHawk build failed." }

$dll = "$RepoDir\ApiHawk\bin\Release\net48\BotServer.dll"
$target = "$BizHawkDir\ExternalTools\BotServer.dll"
Copy-Item $dll $target -Force

# Same prefixed-hex format BizHawk's SHA512Checksum.ComputePrefixedHex produces.
$hash = "SHA512:" + (Get-FileHash $target -Algorithm SHA512).Hash

foreach ($configName in "config.ini", "config_sound_on.ini") {
    $configPath = Join-Path $BizHawkDir $configName
    if (-not (Test-Path $configPath)) { continue }
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    if (-not $config.TrustedExtTools) {
        $config | Add-Member -MemberType NoteProperty -Name TrustedExtTools -Value ([pscustomobject]@{})
    }
    if ($config.TrustedExtTools.PSObject.Properties[$target]) {
        $config.TrustedExtTools.$target = $hash
    } else {
        $config.TrustedExtTools | Add-Member -MemberType NoteProperty -Name $target -Value $hash
    }
    # Depth matters: the config nests well past the default of 2, and a shallow serialize would
    # silently flatten it into a config BizHawk cannot read.
    $config | ConvertTo-Json -Depth 32 | Set-Content $configPath -Encoding utf8
    Write-Host "Trusted in $configName"
}

Write-Host "Deployed $([math]::Round((Get-Item $target).Length / 1kb))kb, $hash"
