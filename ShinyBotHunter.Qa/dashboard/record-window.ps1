# Records one top-level window with PrintWindow(PW_RENDERFULLCONTENT) at 15 fps into an mp4 via ffmpeg,
# and writes a PNG snapshot whenever a file named <ShotDir>\shot-<name>.request appears.
# Stops when the file at -StopFile exists. gdigrab records BizHawk blank, which is why this exists.
param(
    [Parameter(Mandatory)] [string] $ProcessName,
    [string] $TitleMatch = "",
    [Parameter(Mandatory)] [string] $OutFile,
    [Parameter(Mandatory)] [string] $StopFile,
    [Parameter(Mandatory)] [string] $ShotDir,
    [int] $Fps = 15
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    public static IntPtr FindByTitlePart(string part) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => { if (!IsWindowVisible(h)) return true; var sb = new System.Text.StringBuilder(512); GetWindowText(h, sb, 512); if (sb.ToString().Contains(part)) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 60 -and $hwnd -eq [IntPtr]::Zero; $i++) {
    if ($TitleMatch -ne "") { $hwnd = [Win]::FindByTitlePart($TitleMatch) }
    else { $p = Get-Process $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1; if ($p) { $hwnd = $p.MainWindowHandle } }
    if ($hwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 500 }
}
if ($hwnd -eq [IntPtr]::Zero) { throw "no window for $ProcessName" }
$r = New-Object Win+RECT
[Win]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
if ($w -le 0 -or $h -le 0) { throw "window has no size" }
$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$ffmpeg = (Get-Command ffmpeg).Source
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $ffmpeg
$psi.Arguments = "-y -loglevel error -f rawvideo -pix_fmt bgra -s ${w}x${h} -r $Fps -i - -vf pad=ceil(iw/2)*2:ceil(ih/2)*2 -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p `"$OutFile`""
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$ff = [System.Diagnostics.Process]::Start($psi)
$stdin = $ff.StandardInput.BaseStream
$frameMs = [int](1000 / $Fps)
$rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
$buf = New-Object byte[] ($w * $h * 4)
$frames = 0
"recording $ProcessName hwnd=$hwnd ${w}x${h} -> $OutFile" | Out-File -FilePath "$OutFile.log" -Encoding utf8
try {
    while (-not (Test-Path $StopFile)) {
        $t0 = [Environment]::TickCount
        $hdc = $g.GetHdc()
        [Win]::PrintWindow($hwnd, $hdc, 2) | Out-Null
        $g.ReleaseHdc($hdc)
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $bmp.PixelFormat)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
        $bmp.UnlockBits($data)
        $stdin.Write($buf, 0, $buf.Length)
        $frames++
        Get-ChildItem -Path $ShotDir -Filter "shot-*.request" -ErrorAction SilentlyContinue | ForEach-Object {
            $name = $_.BaseName.Substring(5)
            $bmp.Save((Join-Path $ShotDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
            Remove-Item $_.FullName -Force
        }
        $dt = [Environment]::TickCount - $t0
        if ($dt -lt $frameMs) { Start-Sleep -Milliseconds ($frameMs - $dt) }
    }
}
finally {
    $stdin.Close()
    $ff.WaitForExit()
    "frames=$frames exit=$($ff.ExitCode)" | Out-File -FilePath "$OutFile.log" -Append -Encoding utf8
}
