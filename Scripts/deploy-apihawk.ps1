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

# The config edit runs through Python, deliberately. PowerShell's ConvertFrom-Json parses date-like
# strings into DateTime objects and re-emits them in a different shape, and its round-trip is not
# faithful for a structure this large -- a previous version of this script went through it and
# controller bindings did not survive. Python's json round-trip changes only what it is told to.
$py = @'
import io, json, sys
target, hash_value = sys.argv[1], sys.argv[2]
for name in ("config.ini", "config_sound_on.ini"):
    path = sys.argv[3] + "\\" + name
    try:
        cfg = json.load(io.open(path, encoding="utf-8-sig"))
    except FileNotFoundError:
        continue
    cfg.setdefault("TrustedExtTools", {})[target] = hash_value
    io.open(path, "w", encoding="utf-8", newline="").write(json.dumps(cfg, indent=2))
    print(f"Trusted in {name}")
'@
$pyFile = Join-Path $env:TEMP "apihawk-trust.py"
Set-Content -Path $pyFile -Value $py -Encoding utf8
python $pyFile $target $hash $BizHawkDir
if ($LASTEXITCODE -ne 0) { throw "Config trust update failed." }

Write-Host "Deployed $([math]::Round((Get-Item $target).Length / 1kb))kb, $hash"
