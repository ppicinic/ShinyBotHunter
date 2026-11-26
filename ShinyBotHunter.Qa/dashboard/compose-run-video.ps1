# Puts a run's emulator.mp4, its Admin window video and one counts window's video side by side in <RunDir>\combined.mp4,
# on one wall-clock time line, at 900 px high. Run it after the scenario, from any cwd:
#   powershell -ExecutionPolicy Bypass -File compose-run-video.ps1 -RunDir <run folder> -Card <videos\page@....webm>
# The Admin's video is the file in videos\ with admin.webm's size (shutdown() saves a copy of it); -Card is the counts
# window's, found by looking at a frame of each. Times: Playwright closes every window video when the dashboard closes, so
# a window video starts at its file's last write minus its last frame's time; the emulator's runs from the first to the last
# frame times record-window.ps1 logs, and is stretched to that span (its frames come slower than 15 fps under load).
# Before a view starts and after it ends, its place is black.
param(
    [Parameter(Mandatory)] [string] $RunDir,
    [Parameter(Mandatory)] [string] $Card
)
$ErrorActionPreference = "Stop"
$inv = [Globalization.CultureInfo]::InvariantCulture

function Get-LastPts([string] $file) {
    $pts = & ffprobe -v error -select_streams v -show_entries packet=pts_time -of csv=p=0 $file | Where-Object { $_ -ne "" } | Select-Object -Last 1
    [double]::Parse($pts.Trim().TrimEnd(','), $inv)
}

# A window video: [path, start, length] on the wall clock (UTC).
function Get-WindowVideo([string] $file) {
    $length = Get-LastPts $file
    $end = (Get-Item $file).LastWriteTimeUtc
    [pscustomobject]@{ Path = $file; Start = $end.AddSeconds(-$length); Length = $length; Stretch = 1.0 }
}

$adminSize = (Get-Item (Join-Path $RunDir "admin.webm")).Length
$adminFile = Get-ChildItem (Join-Path $RunDir "videos") -Filter *.webm | Where-Object { $_.Length -eq $adminSize } | Select-Object -First 1
if (-not $adminFile) { throw "no video in videos\ has admin.webm's size" }
$admin = Get-WindowVideo $adminFile.FullName
$counts = Get-WindowVideo (Join-Path $RunDir $Card)

$emuFile = Join-Path $RunDir "emulator.mp4"
$log = Get-Content "$emuFile.log" -Raw
if ($log -notmatch "first=(\S+) last=(\S+)") { throw "$emuFile.log has no first and last frame times (recorded before they were logged)" }
$first = [DateTime]::Parse($Matches[1], $inv, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
$last = [DateTime]::Parse($Matches[2], $inv, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
$emuLength = Get-LastPts $emuFile
$wall = ($last - $first).TotalSeconds
$emu = [pscustomobject]@{ Path = $emuFile; Start = $first; Length = $wall; Stretch = $wall / $emuLength }

$views = @($emu, $admin, $counts)
$t0 = ($views | ForEach-Object { $_.Start } | Sort-Object | Select-Object -First 1)
$t1 = ($views | ForEach-Object { $_.Start.AddSeconds($_.Length) } | Sort-Object | Select-Object -Last 1)
$total = ($t1 - $t0).TotalSeconds
$inputs = @()
$chains = @()
for ($i = 0; $i -lt $views.Count; $i++) {
    $v = $views[$i]
    $before = ($v.Start - $t0).TotalSeconds
    $after = $total - $before - $v.Length
    $inputs += @("-i", $v.Path)
    $chains += "[${i}:v]setpts=(PTS-STARTPTS)*$($v.Stretch.ToString($inv)),fps=15,scale=-2:900," +
        "tpad=start_duration=$($before.ToString('0.###', $inv)):stop_duration=$([Math]::Max(0, $after).ToString('0.###', $inv)):color=black[v$i]"
    "{0}: starts {1:o}, {2:0.0} s, stretched x{3:0.000}" -f (Split-Path $v.Path -Leaf), $v.Start, $v.Length, $v.Stretch
}
$filter = ($chains -join ";") + ";[v0][v1][v2]hstack=inputs=3:shortest=1[out]"
$out = Join-Path $RunDir "combined.mp4"
& ffmpeg -y -loglevel error @inputs -filter_complex $filter -map "[out]" -t $total.ToString('0.###', $inv) -c:v libx264 -preset veryfast -crf 26 -pix_fmt yuv420p $out
if ($LASTEXITCODE -ne 0) { throw "ffmpeg exited with $LASTEXITCODE" }
"combined.mp4: {0:0.0} s from {1:o}" -f $total, $t0
