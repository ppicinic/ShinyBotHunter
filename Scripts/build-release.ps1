# Builds the release assets for a GitHub release, into <repo>\release\:
#   ShinyBotHunter-<version>-win-x64.zip   backend (framework-dependent, needs the .NET 10 runtime),
#                                          its run-time data folders, the prebuilt ApiHawk BotServer.dll,
#                                          Scripts\, Dolphin\Config\, docs\guide\ and README.md
#   Pokemon Dashboard Setup <version>.exe  the Electron installer from `npm run dist`
# Needs a dev PC: the .NET 10 SDK, Node, and the fork's reference DLLs in ApiHawk\lib (see lib\.gitkeep),
# because ApiHawk cannot build without them. Run from anywhere: powershell -File Scripts\build-release.ps1
# The backend reads .\Configs, .\Macro\Configs, .\Phases and .\Data relative to its working folder, so the
# zip lays them out beside ShinyBotHunter.exe and the guide says to start it from that folder.
param(
    [string]$Version = (git describe --tags --abbrev=0),
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "release")
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path "$repo\ApiHawk\lib\EmuHawk.exe")) {
    throw "ApiHawk\lib is missing the fork's DLLs; see ApiHawk\lib\.gitkeep. The release needs a prebuilt BotServer.dll."
}

$stage = Join-Path $OutDir "stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $OutDir | Out-Null
New-Item -ItemType Directory -Force $stage | Out-Null

# Backend: publish, then the data folders the app reads from its working folder.
$backend = Join-Path $stage "backend"
dotnet publish "$repo\ShinyBotHunter\ShinyBotHunter.csproj" -c Release -r win-x64 --self-contained false -o $backend --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
foreach ($d in 'Configs', 'Macro\Configs', 'Phases', 'Data') {
    $dst = Join-Path $backend $d
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item -Path "$repo\ShinyBotHunter\$d\*" -Destination $dst -Recurse -Force
}

# Migration bundle: applies the EF migrations without the SDK or the source. It reads the same
# appsettings.json and user secrets as the bot when run from the backend folder.
dotnet ef migrations bundle --project "$repo\ShinyBotHunter\ShinyBotHunter.csproj" --configuration Release --output (Join-Path $backend "migrate.exe") --force
if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations bundle failed ($LASTEXITCODE); is the dotnet-ef tool installed?" }

# ApiHawk: the connector DLL, built against the fork's DLLs in ApiHawk\lib.
dotnet build "$repo\ApiHawk\ApiHawk.csproj" -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "ApiHawk build failed ($LASTEXITCODE)." }
New-Item -ItemType Directory -Force (Join-Path $stage "apihawk") | Out-Null
Copy-Item "$repo\ApiHawk\bin\Release\net48\BotServer.dll" (Join-Path $stage "apihawk\BotServer.dll")

# Scripts, Dolphin config, the guide, the front door.
foreach ($d in 'Scripts', 'Dolphin\Config', 'docs\guide') {
    $dst = Join-Path $stage $d
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item -Path "$repo\$d\*" -Destination $dst -Recurse -Force
}
Copy-Item "$repo\README.md" $stage

$zip = Join-Path $OutDir "ShinyBotHunter-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
# Entry names are written with forward slashes by hand: Compress-Archive and .NET Framework's
# ZipFile.CreateFromDirectory both store backslashes, which some unzip tools flatten into one folder.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $name = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Remove-Item -Recurse -Force $stage

# Dashboard installer. electron-builder names it from package.json's version and productName.
Push-Location "$repo\PokemonDashboard"
try {
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed ($LASTEXITCODE)." }
    npm run dist
    if ($LASTEXITCODE -ne 0) { throw "npm run dist failed ($LASTEXITCODE)." }
} finally { Pop-Location }
$installer = Get-ChildItem "$repo\PokemonDashboard\release\*.exe" | Sort-Object LastWriteTime | Select-Object -Last 1
Copy-Item $installer.FullName $OutDir -Force

Write-Host "Release assets in ${OutDir}:"
Get-ChildItem $OutDir -File | ForEach-Object { Write-Host ("  {0}  {1:N0} bytes" -f $_.Name, $_.Length) }
