# Puts a run's emulator recordings, its Admin window video and one counts window's video side by side in
# <RunDir>\combined.mp4, on one wall-clock time line, at 900 px high. Run it after the scenario, from any cwd:
#   powershell -ExecutionPolicy Bypass -File compose-run-video.ps1 -RunDir <run folder> [-Card <videos\page@....webm>]
# The Admin's video is the file in videos\ with admin.webm's size (shutdown() saves a copy of it); -Card is the counts
# window's, card.webm by default (milestones.ts cardWindow saves it), else found by looking at a frame of each. Times:
# Playwright closes every window video when the dashboard closes, so a window video starts at its file's last write minus
# its last frame's time. Every emulator*.mp4 (one per launch: emulator.mp4, emulator-2.mp4, ...) runs from the first to the
# last frame times its own .log holds (record-window.ps1), and is stretched to that span (its frames come slower than
# 15 fps under load). Before a view starts, between two launches and after it ends, its place is black.
param(
    [Parameter(Mandatory)] [string] $RunDir,
    [string] $Card = "card.webm"
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
$cardFile = Join-Path $RunDir $Card
if (-not (Test-Path $cardFile)) { throw "no $Card in $RunDir; pass -Card videos\page@<id>.webm" }
$counts = Get-WindowVideo $cardFile

# Each launch's recording, by its own log's first and last frame times, in order.
$emus = @(Get-ChildItem $RunDir -Filter "emulator*.mp4" | ForEach-Object {
    $log = Get-Content "$($_.FullName).log" -Raw
    $times = [regex]::Match($log, "first=(\S+) last=(\S+)")
    if (-not $times.Success) { throw "$($_.FullName).log has no first and last frame times (recorded before they were logged)" }
    $first = [DateTime]::Parse($times.Groups[1].Value, $inv, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    $last = [DateTime]::Parse($times.Groups[2].Value, $inv, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    $wall = ($last - $first).TotalSeconds
    [pscustomobject]@{ Path = $_.FullName; Start = $first; Length = $wall; Stretch = $wall / (Get-LastPts $_.FullName) }
} | Sort-Object Start)
if ($emus.Count -eq 0) { throw "no emulator*.mp4 in $RunDir" }
# Every recording is scaled to the first one's shape, so the segments join.
$size = (& ffprobe -v error -select_streams v -show_entries stream=width,height -of csv=p=0 $emus[0].Path).Trim().Split(',')
$emuWidth = 2 * [Math]::Round(900 * [double]$size[0] / [double]$size[1] / 2)

$views = @($emus) + @($admin, $counts)
$t0 = ($views | ForEach-Object { $_.Start } | Sort-Object | Select-Object -First 1)
$t1 = ($views | ForEach-Object { $_.Start.AddSeconds($_.Length) } | Sort-Object | Select-Object -Last 1)
$total = ($t1 - $t0).TotalSeconds
$inputs = @()
$chains = @()
$segments = ""
$at = $t0
for ($i = 0; $i -lt $emus.Count; $i++) {
    $v = $emus[$i]
    $gap = [Math]::Max(0, ($v.Start - $at).TotalSeconds)
    $at = $v.Start.AddSeconds($v.Length)
    # The black after the last launch is padded onto that segment: concat drops a pad placed after it.
    $after = if ($i -eq $emus.Count - 1) { [Math]::Max(0, ($t1 - $at).TotalSeconds) } else { 0 }
    $inputs += @("-i", $v.Path)
    $chains += "[${i}:v]setpts=(PTS-STARTPTS)*$($v.Stretch.ToString($inv)),fps=15,scale=${emuWidth}:900,setsar=1," +
        "tpad=start_duration=$($gap.ToString('0.###', $inv)):stop_duration=$($after.ToString('0.###', $inv)):color=black[e$i]"
    $segments += "[e$i]"
    "{0}: starts {1:o}, {2:0.0} s, stretched x{3:0.000}" -f (Split-Path $v.Path -Leaf), $v.Start, $v.Length, $v.Stretch
}
$chains += "${segments}concat=n=$($emus.Count):v=1:a=0[v0]"
$windows = @($admin, $counts)
for ($j = 0; $j -lt $windows.Count; $j++) {
    $v = $windows[$j]
    $i = $emus.Count + $j
    $before = ($v.Start - $t0).TotalSeconds
    $after = $total - $before - $v.Length
    $inputs += @("-i", $v.Path)
    $chains += "[${i}:v]setpts=(PTS-STARTPTS)*$($v.Stretch.ToString($inv)),fps=15,scale=-2:900," +
        "tpad=start_duration=$($before.ToString('0.###', $inv)):stop_duration=$([Math]::Max(0, $after).ToString('0.###', $inv)):color=black[v$($j + 1)]"
    "{0}: starts {1:o}, {2:0.0} s, stretched x{3:0.000}" -f (Split-Path $v.Path -Leaf), $v.Start, $v.Length, $v.Stretch
}
$filter = ($chains -join ";") + ";[v0][v1][v2]hstack=inputs=3:shortest=1[out]"
$out = Join-Path $RunDir "combined.mp4"
& ffmpeg -y -loglevel error @inputs -filter_complex $filter -map "[out]" -t $total.ToString('0.###', $inv) -c:v libx264 -preset veryfast -crf 26 -pix_fmt yuv420p $out
if ($LASTEXITCODE -ne 0) { throw "ffmpeg exited with $LASTEXITCODE" }
"combined.mp4: {0:0.0} s from {1:o}" -f $total, $t0
