# Runs the app in a throwaway, muted profile (Steam ignored), plays dev actions and screenshots the window.
# Never touches the real profile. Only one copy of the app runs at a time, so quit any running one first.
#
#   tools\dev\shoot.ps1 -Script "hard@3,moment:greet@8" -Shots 5,10 -Tag solve
#   tools\dev\shoot.ps1 -Exe builds\InsertCoin\InsertCoin.exe -Shots 6            # the standalone build
#   tools\dev\shoot.ps1 -Model C:\some\character.glb -Settings ', "Companion": "character"'
#
# -Script   dev actions "action@seconds,..." (see README); times count from app start, about 2 s after launch
# -Shots    seconds after launch at which to capture
# -Bg       colour of the backdrop put behind the transparent window, or "none" for the bare desktop
# -Settings extra JSON members for settings.json, starting with a comma
# -Model    file(s) copied into the profile's characters folder (wildcards allowed)
# -Window   also capture the app's own window with this title (a settings window, a dialog) at each shot, from the
#           window itself (PrintWindow), so nothing else on screen is ever captured
# Output: <Out>\<Tag>-<n>.png (and <Tag>-<n>-window.png), plus the app's log and errors.
param([string]$Script = "", [double[]]$Shots = @(6), [string]$Tag = "shot", [string]$Bg = "#F2EFF7", [string]$Size = "M",
      [int]$X = 1200, [int]$Y = 150, [string]$Settings = "", [string]$Model = "", [string]$Exe = "", [string]$Window = "",
      [string]$Out = (Join-Path $env:TEMP "insert-coin-shots"),
      [string]$Godot = "<tools>\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe")

$repo = Split-Path (Split-Path $PSScriptRoot)
New-Item -ItemType Directory $Out -Force | Out-Null
$profile = Join-Path $Out "profile-$Tag"
if (Test-Path $profile) { Get-ChildItem $profile -File | Remove-Item -Force } else { New-Item -ItemType Directory $profile | Out-Null }
if ($Model -ne "") {
  New-Item -ItemType Directory (Join-Path $profile "characters") -Force | Out-Null
  Copy-Item $Model (Join-Path $profile "characters") -Force
}
"{ `"Username`": `"BobSmith`", `"Initials`": `"BOB`", `"Muted`": true, `"AlwaysOnTop`": true, `"Size`": `"$Size`", `"WindowX`": $X, `"WindowY`": $Y $Settings }" |
  Out-File -Encoding ascii (Join-Path $profile "settings.json")

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Shoot { [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  public static IntPtr Find(uint pid, string title) { IntPtr found = IntPtr.Zero;
    EnumWindows((h, p) => { uint owner; GetWindowThreadProcessId(h, out owner); var t = new System.Text.StringBuilder(256); GetWindowText(h, t, 256);
      if (owner == pid && IsWindowVisible(h) && t.ToString().Contains(title)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found; } }
"@
[Shoot]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null       # true pixels on every monitor

$form = $null
if ($Bg -ne "none") {
  $form = New-Object System.Windows.Forms.Form
  $form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.ShowInTaskbar = $false; $form.TopMost = $true   # over whatever else is on screen
  $form.BackColor = [System.Drawing.ColorTranslator]::FromHtml($Bg)
  $form.Location = New-Object System.Drawing.Point(($X - 40), ($Y - 40)); $form.Size = New-Object System.Drawing.Size(760, 920)
  $form.Show(); [System.Windows.Forms.Application]::DoEvents()
}

$userArgs = "--data=`"$profile`" --no-steam"
if ($Script -ne "") { $userArgs += " --script=$Script" }
$log = Join-Path $Out "run-$Tag.log"; $err = Join-Path $Out "run-$Tag.err"
if ($Exe -ne "") {
  $p = Start-Process (Resolve-Path $Exe) -ArgumentList "-- $userArgs" -PassThru
} else {
  $p = Start-Process $Godot -ArgumentList "--path `"$repo\src\InsertCoin.Game`" -- $userArgs" -PassThru -WindowStyle Minimized `
    -RedirectStandardOutput $log -RedirectStandardError $err
}

$t0 = Get-Date; $i = 0
foreach ($s in $Shots) {
  while (((Get-Date) - $t0).TotalSeconds -lt $s) { Start-Sleep -Milliseconds 50; [System.Windows.Forms.Application]::DoEvents() }
  # run from source, the console wrapper starts the real process as its child
  $app = if ($Exe -ne "") { $p.Refresh(); if ($p.HasExited) { $null } else { $p } } else {
    $child = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | Where-Object { $_.Name -like "Godot*" } | Select-Object -First 1
    if ($child) { Get-Process -Id $child.ProcessId } else { $null }
  }
  if (-not $app) { "no app process at ${s}s (is another copy running?)"; continue }
  $r = New-Object Shoot+RECT; [Shoot]::GetWindowRect($app.MainWindowHandle, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $h = $r.B - $r.T
  if ($w -le 0) { "no window yet at ${s}s"; continue }
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp)
  $file = Join-Path $Out ("{0}-{1}.png" -f $Tag, $i)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $bmp.Save($file); $g.Dispose(); $bmp.Dispose()
  "$file  at ${s}s: ${w}x$h, working set $([int]($app.WorkingSet64 / 1MB)) MB, cpu $([math]::Round($app.TotalProcessorTime.TotalSeconds, 2))s"
  if ($Window -ne "") {
    $h2 = [Shoot]::Find([uint32]$app.Id, $Window)
    if ($h2 -eq [IntPtr]::Zero) { "  no window titled '$Window' at ${s}s" } else {
      $r2 = New-Object Shoot+RECT; [Shoot]::GetWindowRect($h2, [ref]$r2) | Out-Null
      $bmp = New-Object System.Drawing.Bitmap ($r2.R - $r2.L), ($r2.B - $r2.T); $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
      [Shoot]::PrintWindow($h2, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
      $file2 = Join-Path $Out ("{0}-{1}-window.png" -f $Tag, $i); $bmp.Save($file2); $g.Dispose(); $bmp.Dispose(); "  $file2"
    }
  }
  $i++
}

Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
if ($form) { $form.Close() }
Start-Sleep -Milliseconds 500
if ($Exe -eq "") {
  Get-Content $err -ErrorAction SilentlyContinue | Select-Object -First 25
  Get-Content $log -ErrorAction SilentlyContinue | Select-String -Pattern "ERROR|Exception|Companion '" | Select-Object -First 25
}
